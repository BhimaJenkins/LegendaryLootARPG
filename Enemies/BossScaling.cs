using System;
using System.Collections.Generic;
using LegendaryLoot.Data;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LegendaryLoot.Enemies
{
	/// <summary>
	/// Bosses (enemies.yml "bosses"): each boss rolls 0-2 stars (up to 5 by config), gets one boss affix, plus one
	/// normal effect or infusion per star. Stars don't change its level, so its health and damage stay vanilla;
	/// they raise its Unique chance (DropPatches).
	/// </summary>
	public static class BossScaling
	{
		private static EnemyFile Data => DataStore.Enemies;
		private static BossSettings S => DataStore.Enemies.Bosses;

		public static bool Eligible(Character c) =>
			Data.Enabled && S.Enabled && !c.IsTamed() && !Data.ExcludedSet.Contains(Utils.GetPrefabName(c.gameObject));

		public static void Roll(Character c, ZDO zdo)
		{
			string name = Utils.GetPrefabName(c.gameObject);
			int cap = Mathf.Clamp(S.MaxStars, 0, 5);
			float chance = S.StarChance / 100f;
			int stars = 0;
			while (stars < cap && Random.value < chance) stars++;

			int step = EnemyScaling.BiomeStepAt(c.transform.position);
			var affixes = new List<string>();
			S.ExcludedAffixes.TryGetValue(name, out List<string> noBoss);
			var bossTable = Without(Data.BossAffixes, noBoss);
			for (int i = 0; i < Mathf.Max(0, S.BossAffixes); i++)
			{
				string b = EnemyScaling.Pick(bossTable, 99, step);
				if (b == null) break;
				affixes.Add(b);
				bossTable.Remove(b);
			}

			// One normal affix per star: no repeats, one infusion at most.
			var normal = Without(Data.Effects, S.ExcludedEffects);
			var infusions = Without(Data.Infusions, S.ExcludedEffects);
			for (int i = 0; i < stars; i++)
			{
				var table = new Dictionary<string, Dictionary<string, float>>(normal);
				foreach (var kv in infusions) table[kv.Key] = kv.Value;
				string e = EnemyScaling.Pick(table, stars, step);
				if (e == null) break;
				affixes.Add(e);
				normal.Remove(e);
				if (infusions.ContainsKey(e)) infusions.Clear();
			}

			zdo.Set(EnemyScaling.KeyStars, stars + 1);
			zdo.Set(EnemyScaling.KeyAffixes, string.Join(",", affixes));
			zdo.Set(EnemyScaling.KeyBoss, true);
			ApplyHealth(c);
			Plugin.Debug($"[Enemies] Boss {name}: {stars} stars, {string.Join(" ", affixes)}");
		}

		/// <summary>Re-runs the health setup so the star bonus (Patch_TwinHealth) applies, and starts at full health.</summary>
		public static void ApplyHealth(Character c)
		{
			c.SetupMaxHealth();
			c.SetHealth(c.GetMaxHealth());
		}

		public static float HealthMultiplier(int stars) => 1f + Mathf.Max(0f, S.HealthPerStar) / 100f * stars;
		public static float DamageMultiplier(int stars) => 1f + Mathf.Max(0f, S.DamagePerStar) / 100f * stars;

		private static Dictionary<string, Dictionary<string, float>> Without(Dictionary<string, Dictionary<string, float>> table, List<string> skip)
		{
			var copy = new Dictionary<string, Dictionary<string, float>>();
			foreach (var kv in table)
			{
				bool excluded = false;
				if (skip != null)
					foreach (string x in skip)
						if (string.Equals(x, kv.Key, StringComparison.OrdinalIgnoreCase)) { excluded = true; break; }
				if (!excluded) copy[kv.Key] = kv.Value;
			}
			return copy;
		}
	}
}
