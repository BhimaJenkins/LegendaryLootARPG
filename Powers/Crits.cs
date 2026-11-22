using System.Collections.Generic;
using HarmonyLib;
using LegendaryLoot.Effects;
using UnityEngine;

namespace LegendaryLoot.Powers
{
	/// <summary>
	/// Critical strikes:
	///  - Chance = 5% base plus Critical Strike Chance from gear, capped at 55%.
	///  - Damage = 1.5x base, plus Critical Strike Damage from gear, capped at 2.5x.
	///  - Only direct hits (no poison or burn ticks), and never on sneak attacks, which already get the backstab bonus.
	///  - A crit adds damage but no extra stagger: the hit staggers exactly as much as a normal hit would.
	///  - Rolled on the attacker's game. Crit numbers show in gold, larger, with a "!" for the player who crit.
	/// </summary>
	public static class Crits
	{
		public const float BaseMultiplier = 1.5f;
		public const float MaxMultiplier = 2.5f;
		public const float BaseChance = 5f;
		public const float MaxChance = 55f;

		private struct Marker
		{
			public Vector3 Point;
			public float Time;
		}

		private static readonly List<Marker> Pending = new List<Marker>();

		public static float Chance(Player p) => Mathf.Min(MaxChance, BaseChance + PlayerStats.Get(p, "CritChance") + Buffs.Get("critChance"));

		public static float Multiplier(Player p) => Mathf.Min(MaxMultiplier, BaseMultiplier + (PlayerStats.Get(p, "CritDamage") + Buffs.Get("critDamage")) / 100f);

		public static void Roll(Player self, Character target, HitData hit, bool force)
		{
			if (IsSneakAttack(target, hit)) return;
			if (!force && Random.value * 100f >= Chance(self)) return;
			float mult = Multiplier(self);
			hit.m_damage.Modify(mult);
			// Stagger build-up = damage x stagger multiplier, so dividing the multiplier keeps stagger the same as a
			// normal hit. (100+ means "always staggers"; those attacks are left alone.)
			if (hit.m_staggerMultiplier < 100f) hit.m_staggerMultiplier /= mult;
			Pending.Add(new Marker { Point = hit.m_point, Time = Time.time });
			if (Pending.Count > 32) Pending.RemoveAt(0);
		}

		/// <summary>A creature that hasn't noticed anyone takes vanilla's sneak-attack bonus instead of a crit.</summary>
		/// <summary>Same test vanilla uses: an unalerted creature, a weapon with a backstab bonus, no sneak hit on it in the last 5 minutes.</summary>
		private static bool IsSneakAttack(Character target, HitData hit)
		{
			BaseAI ai = target != null ? target.GetBaseAI() : null;
			return ai != null && !ai.IsAlerted() && hit.m_backstabBonus > 1f && Time.time - target.m_backstabTime > 300f;
		}

		/// <summary>
		/// Turns the damage number for a crit gold and larger, with a "!", on the attacker's screen. Damage numbers
		/// are plain text (no color tags), so the new number object is restyled right after vanilla creates it;
		/// vanilla keeps that color and only fades it out.
		/// </summary>
		[HarmonyPatch(typeof(DamageText), nameof(DamageText.AddInworldText))]
		private static class Patch_CritText
		{
			private static readonly Color Gold = new Color32(0xFF, 0xB2, 0x1E, 0xFF);

			private static void Prefix(Vector3 pos, string text, out bool __state)
			{
				__state = false;
				if (Pending.Count == 0 || string.IsNullOrEmpty(text)) return;
				float now = Time.time;
				for (int i = Pending.Count - 1; i >= 0; i--)
				{
					Marker m = Pending[i];
					if (now - m.Time > 2f) { Pending.RemoveAt(i); continue; }
					if ((m.Point - pos).sqrMagnitude > 2.5f * 2.5f) continue;
					Pending.RemoveAt(i);
					__state = true;
					return;
				}
			}

			private static void Postfix(DamageText __instance, bool __state)
			{
				if (!__state || __instance.m_worldTexts == null || __instance.m_worldTexts.Count == 0) return;
				DamageText.WorldTextInstance w = __instance.m_worldTexts[__instance.m_worldTexts.Count - 1];
				if (w?.m_textField == null) return;
				w.m_textField.text = w.m_textField.text + "!";
				w.m_textField.color = Gold;
				w.m_textField.fontSize *= 1.35f;
			}
		}
	}
}
