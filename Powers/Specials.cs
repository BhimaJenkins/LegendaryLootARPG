using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using LegendaryLoot.Effects;
using UnityEngine;

namespace LegendaryLoot.Powers
{
	/// <summary>
	/// One-of-a-kind powers that need a little code of their own:
	///  storeBurst (Throat-Cutter), drain (Life Siphon), blink (Bifrost Eye), refundCost (Galdr Focus),
	///  split (Twin Embers), vanish (Mistcloak), feathers (Raven Mantle), shield (Path of the Righteous),
	///  root (Rooted Revenge), coinDamage (Greed's Creed), and the Bog Walkers passives.
	/// </summary>
	public static class Specials
	{
		// Throat-Cutter: part of the damage is stored on the target, then bursts out ----------------------

		private class Stored { public float Amount; }
		private static readonly Dictionary<Character, Stored> StoredDamage = new Dictionary<Character, Stored>();

		public static void StoreBurst(Player owner, Character target, float amount, float delay, float radius,
			string fx, float fxScale, string sound)
		{
			if (target == null || amount <= 0f) return;
			if (StoredDamage.TryGetValue(target, out Stored s)) { s.Amount += amount; return; }
			s = new Stored { Amount = amount };
			StoredDamage[target] = s;
			Plugin.Instance.StartCoroutine(Burst(owner, target, s, delay, radius, fx, fxScale, sound));
		}

		private static IEnumerator Burst(Player owner, Character target, Stored s, float delay, float radius,
			string fx, float fxScale, string sound)
		{
			yield return new WaitForSeconds(delay);
			StoredDamage.Remove(target);
			if (owner == null || target == null) yield break;
			Vector3 at = target.IsDead() ? target.transform.position : target.GetCenterPoint();
			Fx.Play(fx, at, fxScale);
			if (!string.IsNullOrEmpty(sound)) RavenSound.Broadcast(sound, at);
			Areas.Nova(owner, at, radius, Areas.Typed("", s.Amount), 0f, "none", Skills.SkillType.Knives);
		}

		// Life Siphon: a drained foe can't heal, and everyone who hits it heals ---------------------------

		public const string KeyDrainedUntil = "LLA_DrainedUntil";
		public const string KeyDrainHeal = "LLA_DrainHeal";

		public static bool IsDrained(Character c, out float healPercent)
		{
			healPercent = 0f;
			ZDO zdo = c != null && c.m_nview != null ? c.m_nview.GetZDO() : null;
			if (zdo == null || ZNet.instance == null) return false;
			if (zdo.GetFloat(KeyDrainedUntil) < (float)ZNet.instance.GetTimeSeconds()) return false;
			healPercent = zdo.GetFloat(KeyDrainHeal);
			return true;
		}

		[HarmonyPatch(typeof(Character), nameof(Character.Heal))]
		private static class Patch_NoHealWhileDrained
		{
			private static bool Prefix(Character __instance) => __instance.IsPlayer() || !IsDrained(__instance, out _);
		}

		/// <summary>Your hits on a drained foe heal you (each player's game heals its own player).</summary>
		[HarmonyPatch(typeof(Character), nameof(Character.Damage))]
		private static class Patch_DrainHeal
		{
			private static void Postfix(Character __instance, HitData hit)
			{
				Player me = Player.m_localPlayer;
				if (me == null || hit == null || hit.GetAttacker() != me || !IsDrained(__instance, out float pct) || pct <= 0f) return;
				float heal = hit.GetTotalDamage() * pct / 100f;
				if (heal > 0f) me.Heal(heal, true);
			}
		}

		// Bifrost Eye: blink out of danger ------------------------------------------------------------------

		public static void Blink(Player p, float distance, Character attacker)
		{
			Vector3 away = attacker != null ? p.transform.position - attacker.transform.position : -p.transform.forward;
			away.y = 0f;
			away = away.sqrMagnitude > 0.01f ? away.normalized : -p.transform.forward;
			Vector3 chest = p.transform.position + Vector3.up * 1f;
			// Stop short of walls and trees.
			if (Physics.SphereCast(chest, 0.4f, away, out RaycastHit hitInfo, distance, LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "vehicle")))
				distance = Mathf.Max(0f, hitInfo.distance - 0.6f);
			if (distance < 1f) return;
			Vector3 to = p.transform.position + away * distance;
			if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(to, out float h)) to.y = Mathf.Max(to.y, h) + 0.1f;
			Fx.Play("frost", chest, 0.8f);
			p.transform.position = to;
			if (p.m_body != null) { p.m_body.position = to; p.m_body.velocity = Vector3.zero; }
			Fx.Play("frost", to + Vector3.up, 0.8f);
		}

		// Galdr Focus: refund what an attack cost -------------------------------------------------------------

		public static void Refund(Player p, Attack attack, string resource)
		{
			if (p == null || attack == null) return;
			float amount = resource == "stamina" ? attack.m_attackStamina : attack.m_attackEitr;
			if (resource == "stamina") p.AddStamina(amount);
			else p.AddEitr(amount);
			Plugin.Debug($"Free cast: refunded {amount:0.#} {resource}.");
		}

		// Mistcloak: enemies lose sight of you -----------------------------------------------------------------

		private static float _vanishUntil;
		private static float _vanishRadius = 30f;

		public static void Vanish(Player p, float duration, float radius, string fx, string sound)
		{
			_vanishUntil = Time.time + duration;
			_vanishRadius = radius;
			Fx.Broadcast(fx, p.transform.position + Vector3.up * 0.9f, 1f);
			Sound(sound, p.transform.position);
		}

		/// <summary>While vanished, enemies near you forget you (re-applied every frame so they can't re-find you).</summary>
		private static void VanishTick(Player p)
		{
			if (Time.time > _vanishUntil) return;
			foreach (Character c in Areas.EnemiesNear(p, p.transform.position, _vanishRadius))
			{
				if (!c.m_nview.IsOwner()) continue;
				MonsterAI ai = c.GetComponent<MonsterAI>();
				if (ai == null || ai.m_targetCreature != p) continue;
				ai.m_targetCreature = null;
				ai.SetAlerted(false);
			}
		}

		/// <summary>Creatures don't pick you as a target while you're vanished.</summary>
		[HarmonyPatch(typeof(BaseAI), nameof(BaseAI.FindEnemy))]
		private static class Patch_Unseen
		{
			private static void Postfix(ref Character __result)
			{
				if (__result != null && __result == Player.m_localPlayer && Time.time <= _vanishUntil) __result = null;
			}
		}

		// Raven Mantle: feathers that each stop one projectile ---------------------------------------------------

		private static int _feathers = -1;
		private static float _nextFeather;
		private static string _featherFx = "vfx_seagull_death", _featherSound = "sfx_archery_target_hit";

		private static void FeatherTick(Player p)
		{
			if (!PowerEngine.FindEffect(p, "feathers", out Dictionary<string, string> e, out float x)) { _feathers = -1; return; }
			int max = Mathf.RoundToInt(PowerMath.Get(e, "charges", x, 3f));
			float regrow = PowerMath.Get(e, "regrow", x, 10f);
			_featherFx = PowerMath.GetText(e, "fx", "vfx_seagull_death");
			_featherSound = PowerMath.GetText(e, "sound", "sfx_archery_target_hit");
			if (_feathers < 0) { _feathers = max; _nextFeather = Time.time + regrow; }
			if (_feathers >= max) { _nextFeather = Time.time + regrow; return; }
			if (Time.time >= _nextFeather) { _feathers++; _nextFeather = Time.time + regrow; }
		}

		public static int Feathers => Mathf.Max(0, _feathers);

		[HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
		private static class Patch_Feathers
		{
			[HarmonyPriority(Priority.High)]
			private static void Prefix(Character __instance, HitData hit)
			{
				if (hit == null || !hit.m_ranged || _feathers <= 0) return;
				if (!(__instance is Player p) || p != Player.m_localPlayer) return;
				_feathers--;
				hit.m_damage = new HitData.DamageTypes();
				hit.m_pushForce = 0f;
				hit.m_staggerMultiplier = 0f;
				Fx.Broadcast(_featherFx, hit.m_point, 1f);
				Sound(_featherSound, hit.m_point);
				p.Message(MessageHud.MessageType.TopLeft, $"A feather turns the shot aside ({_feathers} left)");
			}
		}

		/// <summary>A cue sound: a .ogg from the audio folder, or one of the game's own sound prefabs (sfx_...).</summary>
		public static void Sound(string name, Vector3 at)
		{
			if (string.IsNullOrEmpty(name)) return;
			if (name.EndsWith(".ogg", System.StringComparison.OrdinalIgnoreCase)) RavenSound.Broadcast(name, at);
			else Fx.Broadcast(name, at, 1f);
		}

		// Path of the Righteous: a bubble shield ----------------------------------------------------------------

		public static void Shield(Player p, float percentMaxHealth, float duration)
		{
			if (p == null || ObjectDB.instance == null) return;
			SE_Shield template = null;
			foreach (StatusEffect se in ObjectDB.instance.m_StatusEffects)
				if (se is SE_Shield sh) { template = sh; break; }
			if (template == null) { Plugin.Debug("Shield: no vanilla shield effect to copy."); return; }
			var shield = (SE_Shield)template.Clone();
			shield.m_absorbDamage = p.GetMaxHealth() * percentMaxHealth / 100f;
			shield.m_absorbDamageWorldLevel = 0f;
			shield.m_absorbDamagePerSkillLevel = 0f;
			shield.m_ttl = duration;
			p.GetSEMan().AddStatusEffect(shield, true);
		}

		// Greed's Creed: damage from the coins you carry ----------------------------------------------------------

		/// <summary>Percent bonus: rises quickly at first, then levels off toward the maximum.</summary>
		public static float CoinBonus(Player p, float max, float scale)
		{
			if (p == null) return 0f;
			int coins = p.GetInventory().CountItems("$item_coins");
			return max * (1f - Mathf.Exp(-coins / Mathf.Max(1f, scale)));
		}

		// Bog Walkers ---------------------------------------------------------------------------------------------

		private static float _baseSwim = -1f;

		private static void SwimTick(Player p)
		{
			if (_baseSwim < 0f) _baseSwim = p.m_swimSpeed;
			float bonus = PlayerStats.Get(p, "SwimSpeed");
			p.m_swimSpeed = _baseSwim * (1f + bonus / 100f);
		}

		/// <summary>Tarred slows you less with the right gear (the effect on you is your own copy).</summary>
		[HarmonyPatch(typeof(SEMan), nameof(SEMan.Internal_AddStatusEffect))]
		private static class Patch_TarResist
		{
			private static void Postfix(SEMan __instance, StatusEffect __result)
			{
				if (!(__result is SE_Stats stats) || __instance.m_character != Player.m_localPlayer) return;
				if (__result.name == null || __result.name.IndexOf("Tared", System.StringComparison.OrdinalIgnoreCase) < 0) return;
				float cut = Mathf.Clamp(PlayerStats.Get((Player)__instance.m_character, "TarSlowReduction"), 0f, 100f);
				if (cut > 0f) stats.m_speedModifier *= 1f - cut / 100f;
			}
		}

		/// <summary>Every frame for the local player.</summary>
		public static void Tick(Player p)
		{
			VanishTick(p);
			FeatherTick(p);
			SwimTick(p);
		}

		[HarmonyPatch(typeof(Game), nameof(Game.Start))]
		private static class Patch_Reset
		{
			private static void Postfix()
			{
				StoredDamage.Clear();
				_vanishUntil = 0f;
				_feathers = -1;
				_baseSwim = -1f;
			}
		}
	}

	/// <summary>
	/// Twin Embers: a tagged projectile bursts into smaller copies of itself where it hits, flying outward.
	/// The fragments don't split again.
	/// </summary>
	public static class Fragments
	{
		public static void Split(Projectile parent, Vector3 point, int count, float percent)
		{
			if (!(parent.m_owner is Player owner) || owner != Player.m_localPlayer || ZNetScene.instance == null) return;
			GameObject prefab = ZNetScene.instance.GetPrefab(Utils.GetPrefabName(parent.gameObject));
			if (prefab == null) return;
			HitData.DamageTypes d = parent.m_damage;
			d.Modify(percent / 100f);
			for (int i = 0; i < count; i++)
			{
				float yaw = 360f / count * i + Random.Range(-20f, 20f);
				// A low arc outward: just enough lift to clear the ground, then gravity brings it down nearby.
				Vector3 dir = Quaternion.Euler(-18f, yaw, 0f) * Vector3.forward;
				Vector3 start = point + Vector3.up * 0.4f + dir * 0.3f;
				GameObject go = Object.Instantiate(prefab, start, Quaternion.LookRotation(dir));
				go.transform.localScale *= 0.6f;
				IProjectile proj = go.GetComponent<IProjectile>();
				if (proj == null) { Object.Destroy(go); continue; }
				var hit = new HitData { m_damage = d, m_hitType = HitData.HitType.Undefined, m_skill = Skills.SkillType.ElementalMagic };
				hit.SetAttacker(owner);
				proj.Setup(owner, dir * 9f, 10f, hit, null, null);
				if (proj is Projectile p)
				{
					p.m_gravity = Mathf.Max(p.m_gravity, 18f); // fireballs don't fall on their own
					p.m_ttl = Mathf.Min(p.m_ttl > 0f ? p.m_ttl : 2f, 2f); // never fly off into the sky
				}
			}
		}
	}
}
