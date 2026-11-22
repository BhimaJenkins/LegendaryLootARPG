using HarmonyLib;
using LegendaryLoot.Effects;
using UnityEngine;

namespace LegendaryLoot.Powers
{
	/// <summary>
	/// Where the triggers come from. Every trigger runs on the local player's own game:
	///  - onHit:   the player's hit, just before it's sent to the creature (so effects can change it)
	///  - whenHit / onBlock / onParry / onLethal: damage arriving at the player, on the player's game
	///  - onKill:  reported back to the killer by whichever game owned the creature
	///  - every:   a timer
	/// </summary>
	public static class PowerPatches
	{
		private static bool IsLocal(Character c) => c != null && c == Player.m_localPlayer;

		private static PowerContext ForTarget(Player self, Character target, HitData hit)
		{
			return new PowerContext
			{
				Self = self,
				Target = target,
				Hit = hit,
				Damage = hit != null ? hit.GetTotalDamage() : 0f,
				TargetIsBoss = target != null && target.IsBoss(),
				TargetStars = target != null ? Mathf.Max(0, target.GetLevel() - 1) : 0,
				TargetHealthPercent = target != null ? target.GetHealthPercentage() : 1f,
				Position = target != null ? target.transform.position : Vector3.zero,
			};
		}

		// onHit + crits ---------------------------------------------------------------------------------

		/// <summary>The local player's direct hits (melee, arrows, spells), before they're sent to the target.</summary>
		[HarmonyPatch(typeof(Character), nameof(Character.Damage))]
		[HarmonyPriority(Priority.High)]
		private static class Patch_Damage
		{
			private static void Prefix(Character __instance, HitData hit)
			{
				if (hit == null || hit.m_hitType != HitData.HitType.PlayerHit) return; // skips poison/burn ticks, falls, etc.
				if (!(hit.GetAttacker() is Player self) || !IsLocal(self) || __instance == self || __instance.IsPlayer()) return;

				PowerContext ctx = ForTarget(self, __instance, hit);
				ctx.Weapon = self.GetCurrentWeapon();
				ctx.FromThrow = Areas.InThrownHit;
				PowerEngine.Run("onHit", ctx);

				// Temporary damage buffs (e.g. Berserker), then the crit roll.
				float bonus = (Buffs.Get("damage") + Areas.ZoneBonus(self, "damage")) / 100f;
				if (Buffs.Has("berserk"))
				{
					float missing = 1f - self.GetHealthPercentage();
					bonus += Mathf.Lerp(0.5f, 2f, missing);
				}
				if (bonus > 0f) hit.m_damage.Modify(1f + bonus);

				Crits.Roll(self, __instance, hit, ctx.ForceCrit);
			}
		}

		// whenHit / onLethal ---------------------------------------------------------------------------

		/// <summary>Damage arriving at the local player, before armor and blocking.</summary>
		[HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
		private static class Patch_WhenHit
		{
			private static void Prefix(Character __instance, HitData hit)
			{
				if (!IsLocal(__instance) || hit == null) return;
				Player self = (Player)__instance;
				if (Buffs.Has("immune")) { hit.m_damage = new HitData.DamageTypes(); return; }

				Character attacker = hit.GetAttacker();
				if (attacker == null || attacker == self) return;
				PowerContext ctx = ForTarget(self, attacker, hit);
				PowerEngine.Run("whenHit", ctx);

				float taken = Buffs.Get("damageTaken"); // negative = less damage
				if (taken != 0f) hit.m_damage.Modify(Mathf.Max(0f, 1f + taken / 100f));
			}
		}

		/// <summary>Last chance before a killing blow lands (Undying).</summary>
		[HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
		private static class Patch_Lethal
		{
			private static void Prefix(Character __instance, HitData hit)
			{
				if (!IsLocal(__instance) || hit == null) return;
				if (hit.GetTotalDamage() < __instance.GetHealth()) return;
				var ctx = new PowerContext { Self = (Player)__instance, Hit = hit, Damage = hit.GetTotalDamage() };
				PowerEngine.Run("onLethal", ctx);
				if (!ctx.PreventDeath) return;
				hit.m_damage = new HitData.DamageTypes();
				__instance.Heal(__instance.GetMaxHealth(), true);
			}
		}

		// onBlock / onParry ----------------------------------------------------------------------------

		[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.BlockAttack))]
		private static class Patch_Block
		{
			private static void Prefix(Humanoid __instance, out bool __state)
			{
				// Vanilla counts a block as a parry during the first 0.25 s of raising the shield.
				__state = __instance.m_blockTimer >= 0f && __instance.m_blockTimer < 0.25f;
			}

			private static void Postfix(Humanoid __instance, bool __result, HitData hit, Character attacker, bool __state)
			{
				if (!__result || !IsLocal(__instance)) return;
				PowerContext ctx = ForTarget((Player)__instance, attacker, hit);
				PowerEngine.Run("onBlock", ctx);
				if (__state) PowerEngine.Run("onParry", ctx);
			}
		}

		// onKill -----------------------------------------------------------------------------------------

		/// <summary>On the game that owns a creature: notice when a player's hit killed it, and tell that player.</summary>
		[HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
		private static class Patch_KillReport
		{
			private static void Prefix(Character __instance, out float __state) => __state = __instance.GetHealth();

			private static void Postfix(Character __instance, HitData hit, float __state)
			{
				if (hit == null || __state <= 0f || __instance.GetHealth() > 0f || __instance.IsPlayer()) return;
				if (hit.m_attacker.IsNone()) return;
				ZDO attackerZdo = ZDOMan.instance.GetZDO(hit.m_attacker);
				if (attackerZdo == null) return;
				bool boss = __instance.IsBoss();
				int stars = Mathf.Max(0, __instance.GetLevel() - 1);
				long owner = attackerZdo.GetOwner();
				Vector3 pos = __instance.transform.position;
				if (owner == ZNet.GetUID()) OnKill(boss, stars, hit.m_attacker, pos);
				else ZRoutedRpc.instance.InvokeRoutedRPC(owner, KillRpc, boss, stars, hit.m_attacker, pos);
			}
		}

		private const string KillRpc = "LLA_Kill";

		[HarmonyPatch(typeof(Game), nameof(Game.Start))]
		private static class Patch_RegisterKill
		{
			private static void Postfix()
			{
				ZRoutedRpc.instance.Register<bool, int, ZDOID, Vector3>(KillRpc, (sender, boss, stars, attacker, pos) => OnKill(boss, stars, attacker, pos));
				Buffs.Clear();
			}
		}

		private static void OnKill(bool boss, int stars, ZDOID attacker, Vector3 pos)
		{
			Player self = Player.m_localPlayer;
			if (self == null || self.GetZDOID() != attacker) return;
			var ctx = new PowerContext { Self = self, Weapon = self.GetCurrentWeapon(), TargetIsBoss = boss, TargetStars = stars, TargetHealthPercent = 0f, Position = pos };
			PowerEngine.Run("onKill", ctx);
		}

		// every + effect hooks on the player ---------------------------------------------------------

		[HarmonyPatch(typeof(Player), nameof(Player.Update))]
		private static class Patch_Tick
		{
			private static void Postfix(Player __instance)
			{
				if (IsLocal(__instance)) PowerEngine.Tick(__instance);
			}
		}

		/// <summary>Berserk: no health regeneration while it lasts.</summary>
		[HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyHealthRegen))]
		private static class Patch_NoRegen
		{
			private static void Postfix(SEMan __instance, ref float regenMultiplier)
			{
				if (IsLocal(__instance.m_character) && Buffs.Has("berserk")) regenMultiplier = 0f;
			}
		}

		/// <summary>Immunities from gear (e.g. Surtling Mantle: Burning): the status effect never starts.</summary>
		[HarmonyPatch(typeof(SEMan), nameof(SEMan.Internal_AddStatusEffect))]
		private static class Patch_Immunity
		{
			private static bool Prefix(SEMan __instance, int nameHash, ref StatusEffect __result)
			{
				if (!IsLocal(__instance.m_character)) return true;
				foreach (string name in ImmunityNames)
				{
					if (nameHash != name.GetStableHashCode()) continue;
					if (PlayerStats.Get((Player)__instance.m_character, "Immune" + name) <= 0f) continue;
					__result = null;
					return false;
				}
				return true;
			}

			private static readonly string[] ImmunityNames = { "Burning", "Freezing", "Frost", "Cold", "Wet", "Poison", "Tared" };
		}
	
		/// <summary>onDodge: an attack would have hit you, but you dodged through it (the game's perfect dodge).</summary>
		[HarmonyPatch(typeof(Player), nameof(Player.RPC_HitWhileDodging))]
		private static class Patch_Dodge
		{
			private static void Postfix(Player __instance)
			{
				if (__instance != Player.m_localPlayer) return;
				PowerEngine.Run("onDodge", new PowerContext { Self = __instance, Weapon = __instance.GetCurrentWeapon() });
			}
		}
	}
}
