using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LegendaryLoot.Powers
{
	public enum DebuffKind
	{
		Slow = 1,       // Value = percent slower
		Paralyze = 2,   // staggered, then can't move
		Bleed = 3,      // Value = total damage over the duration, ignores armor
		Vulnerable = 4, // Value = percent more damage taken
		Drained = 5,    // Value = percent of damage dealt to it that heals whoever deals it; it can't heal
	}

	/// <summary>
	/// The mod's status effect. Built at runtime (no ObjectDB entry needed) and carried on creatures and players.
	/// Based on vanilla's SE_Stats, so movement slows and damage over time use the game's own code.
	/// </summary>
	public class SE_LLA : SE_Stats
	{
		public DebuffKind Kind;
		public float Value;
		public int Stacks = 1;
	}

	/// <summary>
	/// Debuffs on creatures: the attacker's game decides a debuff happens, then asks the game that owns the
	/// creature to apply it, because only the owner moves and damages that creature.
	/// </summary>
	public static class Debuffs
	{
		private const string Rpc = "LLA_Debuff";

		public static int Hash(DebuffKind kind) => ("LLA_" + kind).GetStableHashCode();

		public static bool Has(Character c, DebuffKind kind) => c?.GetSEMan() != null && c.GetSEMan().HaveStatusEffect(Hash(kind));

		public static SE_LLA Get(Character c, DebuffKind kind) => c?.GetSEMan()?.GetStatusEffect(Hash(kind)) as SE_LLA;

		public static void Send(Character target, DebuffKind kind, float value, float duration, int maxStacks = 1)
		{
			if (target == null || target.m_nview == null || !target.m_nview.IsValid() || duration <= 0f) return;
			target.m_nview.InvokeRPC(Rpc, (int)kind, value, duration, maxStacks);
		}

		[HarmonyPatch(typeof(Character), nameof(Character.Awake))]
		private static class Patch_Register
		{
			private static void Postfix(Character __instance)
			{
				if (__instance.m_nview == null || __instance.m_nview.GetZDO() == null) return;
				Character c = __instance;
				c.m_nview.Register<int, float, float, int>(Rpc, (sender, kind, value, duration, maxStacks) => Apply(c, (DebuffKind)kind, value, duration, maxStacks));
			}
		}

		private static void Apply(Character c, DebuffKind kind, float value, float duration, int maxStacks)
		{
			if (c == null || c.IsDead() || !c.m_nview.IsOwner()) return;
			SEMan seman = c.GetSEMan();
			if (seman == null) return;

			if (kind == DebuffKind.Bleed)
			{
				// Bleeds stack up to maxStacks: each stack adds its damage, and the timer restarts.
				SE_LLA existing = Get(c, kind);
				if (existing != null && existing.Stacks < Mathf.Max(1, maxStacks))
				{
					existing.Stacks++;
					existing.Value += value;
					existing.m_healthPerTick = -existing.Value / Mathf.Max(1f, duration / existing.m_tickInterval);
					existing.ResetTime();
					return;
				}
			}

			var se = ScriptableObject.CreateInstance<SE_LLA>();
			se.name = "LLA_" + kind;
			se.m_name = "";
			se.m_nameHash = Hash(kind);
			se.Kind = kind;
			se.Value = value;
			se.m_ttl = duration;
			switch (kind)
			{
				case DebuffKind.Slow:
					se.m_speedModifier = -Mathf.Clamp01(value / 100f);
					break;
				case DebuffKind.Paralyze:
					se.m_speedModifier = -1f;
					c.Stagger(-c.transform.forward);
					break;
				case DebuffKind.Drained:
				{
					// Marked on the creature for every player's game to see (they heal when they hit it).
					ZDO zdo = c.m_nview.GetZDO();
					zdo.Set(Specials.KeyDrainedUntil, (float)ZNet.instance.GetTimeSeconds() + duration);
					zdo.Set(Specials.KeyDrainHeal, value);
					break;
				}
				case DebuffKind.Bleed:
					se.m_tickInterval = 0.5f;
					se.m_healthPerTick = -value / Mathf.Max(1f, duration / se.m_tickInterval);
					break;
			}
			seman.AddStatusEffect(se, true, 0, 0f);
		}

		/// <summary>Vulnerable: the creature takes more damage while it lasts (applied by its owner).</summary>
		[HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
		private static class Patch_Vulnerable
		{
			private static void Prefix(Character __instance, HitData hit)
			{
				SE_LLA v = Get(__instance, DebuffKind.Vulnerable);
				if (v != null && hit != null) hit.m_damage.Modify(1f + v.Value / 100f);
			}
		}
	}

	/// <summary>
	/// Temporary buffs on the local player (berserk, bonus damage, speed, damage taken, immunity).
	/// Kept here rather than as status effects, because they only matter on the player's own game.
	/// </summary>
	public static class Buffs
	{
		private class Entry
		{
			public float Value;
			public float Until;
			public int Stacks;
		}

		private static readonly Dictionary<string, Entry> Active = new Dictionary<string, Entry>(System.StringComparer.OrdinalIgnoreCase);

		public static void Add(string stat, float value, float duration, int maxStacks)
		{
			if (string.IsNullOrEmpty(stat)) return;
			float until = Time.time + duration;
			if (Active.TryGetValue(stat, out Entry e) && Time.time < e.Until)
			{
				if (e.Stacks < Mathf.Max(1, maxStacks)) { e.Stacks++; e.Value += value; }
				e.Until = until;
				return;
			}
			Active[stat] = new Entry { Value = value, Until = until, Stacks = 1 };
		}

		public static float Get(string stat) => Active.TryGetValue(stat, out Entry e) && Time.time < e.Until ? e.Value : 0f;

		public static bool Has(string stat) => Active.TryGetValue(stat, out Entry e) && Time.time < e.Until;

		public static void Clear() => Active.Clear();

		/// <summary>Buffs running right now: stat, value, seconds left, stacks.</summary>
		public static List<(string Stat, float Value, float Left, int Stacks)> Current()
		{
			var list = new List<(string, float, float, int)>();
			foreach (KeyValuePair<string, Entry> kv in Active)
				if (Time.time < kv.Value.Until) list.Add((kv.Key, kv.Value.Value, kv.Value.Until - Time.time, kv.Value.Stacks));
			list.Sort((a, b) => string.CompareOrdinal(a.Item1, b.Item1));
			return list;
		}
	}
}
