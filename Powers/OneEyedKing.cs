using System.Collections.Generic;
using HarmonyLib;
using LegendaryLoot.Core;
using LegendaryLoot.Data;
using UnityEngine;

namespace LegendaryLoot.Powers
{
	/// <summary>
	/// One-Eyed King (Dead Raiser only): instead of a host of skeletons, the staff raises a single champion with
	/// double health, +150% damage and 50% more size (uniques.yml values). Casting again replaces it, as vanilla
	/// does once the limit is reached.
	/// </summary>
	public static class OneEyedKing
	{
		public const string Id = "OneEyedKing";
		public const string KeyKing = "LLA_King";        // the champion: its multipliers live on it so every game agrees
		public const string KeyHealth = "LLA_KingHealth";
		public const string KeyDamage = "LLA_KingDamage";
		public const string KeyScale = "LLA_KingScale";
		public const string KeyOwner = "LLA_KingOwner";   // the player who raised it

		private static float _pendingUntil;
		private static readonly HashSet<string> PendingPrefabs = new HashSet<string>();

		private static UniqueDef Def
		{
			get
			{
				foreach (UniqueDef u in DataStore.Uniques) if (u.Id == Id) return u;
				return null;
			}
		}

		[HarmonyPatch(typeof(Game), nameof(Game.Start))]
		private static class Patch_Register
		{
			private static void Postfix() => Effects.UniqueEffects.Register(Id);
		}

		/// <summary>When the local player's One-Eyed King staff casts: only one skeleton, and the next one to rise is the king.</summary>
		[HarmonyPatch(typeof(SpawnAbility), nameof(SpawnAbility.Setup))]
		private static class Patch_Cast
		{
			private static void Postfix(SpawnAbility __instance, Character owner, ItemDrop.ItemData item)
			{
				if (owner == null || owner != Player.m_localPlayer || item == null) return;
				MagicItem magic = item.GetMagic();
				if (magic == null || !magic.IsUnique || magic.UniqueId != Id) return;
				__instance.m_setMaxInstancesFromWeaponLevel = false;
				__instance.m_maxSpawned = 1;
				__instance.m_minToSpawn = 1;
				__instance.m_maxToSpawn = 1;
				PendingPrefabs.Clear();
				foreach (GameObject p in __instance.m_spawnPrefab) if (p != null) PendingPrefabs.Add(p.name);
				_pendingUntil = Time.time + 6f;
			}
		}

		/// <summary>The skeleton that rises from that cast becomes the king (on the caster's game, which owns it).</summary>
		// Humanoid.Start doesn't call Character.Start (skeletons are humanoids), so both are patched.
		[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Start))]
		private static class Patch_RiseHumanoid
		{
			private static void Postfix(Humanoid __instance) => Rise(__instance);
		}

		[HarmonyPatch(typeof(Character), nameof(Character.Start))]
		private static class Patch_Rise
		{
			private static void Postfix(Character __instance) => Rise(__instance);
		}

		private static void Rise(Character __instance)
		{
			{
				if (Time.time > _pendingUntil || __instance.m_nview == null || !__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner()) return;
				if (!PendingPrefabs.Contains(Utils.GetPrefabName(__instance.gameObject))) return;
				_pendingUntil = 0f;
				UniqueDef def = Def;
				ZDO zdo = __instance.m_nview.GetZDO();
				// Only one champion: any earlier one of yours is dismissed as the new one rises.
				long me = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0L;
				Dismiss(__instance, me);
				zdo.Set(KeyKing, true);
				zdo.Set(KeyOwner, me);
				zdo.Set(KeyHealth, 1f + (def?.Value("health", 100f) ?? 100f) / 100f);
				zdo.Set(KeyDamage, 1f + (def?.Value("damage", 150f) ?? 150f) / 100f);
				zdo.Set(KeyScale, 1f + (def?.Value("size", 50f) ?? 50f) / 100f);
				__instance.SetupMaxHealth();
				__instance.SetHealth(__instance.GetMaxHealth());
				Apply(__instance);
				Plugin.Debug("[Powers] One-Eyed King: the champion rises.");
			}
		}

		/// <summary>Removes every other champion this player raised (loaded nearby; they follow their summoner).</summary>
		private static void Dismiss(Character keep, long owner)
		{
			if (owner == 0L || ZNetScene.instance == null) return;
			foreach (Character c in new List<Character>(Character.GetAllCharacters()))
			{
				if (c == null || c == keep || c.m_nview == null || !c.m_nview.IsValid()) continue;
				ZDO zdo = c.m_nview.GetZDO();
				if (!zdo.GetBool(KeyKing) || zdo.GetLong(KeyOwner) != owner) continue;
				if (!c.m_nview.IsOwner()) c.m_nview.ClaimOwnership();
				c.m_deathEffects?.Create(c.transform.position, c.transform.rotation, c.transform);
				ZNetScene.instance.Destroy(c.gameObject);
			}
		}

		/// <summary>Size and name on every game.</summary>
		private static void Apply(Character c)
		{
			ZDO zdo = c.m_nview.GetZDO();
			float scale = zdo.GetFloat(KeyScale, 1f);
			if (scale > 0f) c.transform.localScale = Vector3.one * scale;
		}

		[HarmonyPatch(typeof(Character), nameof(Character.Start))]
		private static class Patch_Look
		{
			private static void Postfix(Character __instance) => Look(__instance);
		}

		[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.Start))]
		private static class Patch_LookHumanoid
		{
			private static void Postfix(Humanoid __instance) => Look(__instance);
		}

		private static void Look(Character c)
		{
			if (c.m_nview != null && c.m_nview.IsValid() && c.m_nview.GetZDO().GetBool(KeyKing)) Apply(c);
		}

		[HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
		private static class Patch_Name
		{
			private static void Postfix(Character __instance, ref string __result)
			{
				if (__instance.m_nview != null && __instance.m_nview.IsValid() && __instance.m_nview.GetZDO().GetBool(KeyKing))
					__result = "<color=#D4AF37>One-Eyed King</color>";
			}
		}

		/// <summary>Double health (applied whenever the game works out its max health).</summary>
		[HarmonyPatch(typeof(Character), nameof(Character.SetupMaxHealth))]
		private static class Patch_Health
		{
			private static void Postfix(Character __instance)
			{
				if (__instance.m_nview == null || !__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner()) return;
				float m = __instance.m_nview.GetZDO().GetFloat(KeyHealth, 1f);
				if (m > 1.001f) __instance.SetMaxHealth(__instance.GetMaxHealth() * m);
			}
		}

		/// <summary>+150% damage on everything it hits.</summary>
		[HarmonyPatch(typeof(Character), nameof(Character.Damage))]
		private static class Patch_Damage
		{
			private static void Prefix(HitData hit)
			{
				Character attacker = hit?.GetAttacker();
				if (attacker == null || attacker.m_nview == null || !attacker.m_nview.IsValid()) return;
				float m = attacker.m_nview.GetZDO().GetFloat(KeyDamage, 1f);
				if (m > 1.001f) hit.m_damage.Modify(m);
			}
		}
	}
}
