using System.Collections.Generic;
using LegendaryLoot.Core;
using LegendaryLoot.Data;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LegendaryLoot.Loot
{
	/// <summary>
	/// The loot roll, in the order from the design:
	///   1. drop chance  2. item biome  3. weapon or armor  4. Unique check  5. tier  6. star bump.
	/// Bosses: guaranteed drops, 3x Unique chance with no cap, and a 55% chance to move up a tier instead of the star bump.
	/// </summary>
	public static class LootRoller
	{
		public struct Context
		{
			public int BiomeStep;     // the mob's biome on the ladder
			public int Stars;         // 0-5
			public bool Boss;
			public bool Swarm;
			public float UniqueFind;  // bonus percent from the owner's Unique Find gear
			public bool Guaranteed;   // skip the drop-chance roll (bosses, unique enemies, event chests)
			public float UniqueMultiplier; // unique enemies: x2 Unique chance (and cap); 0 = normal
			public float UniquePercent;    // a fixed base Unique chance (boss event chests); 0 = normal
			public float DropMultiplier;   // Apex Predators: x2 drop chance; 0 = normal
			public float Quality;          // night kills: 1.5 = each tier step 1.5x as likely, star bump x1.5; 0 = normal
			public string Source;     // for logs
		}

		public class Result
		{
			public GameObject Prefab;
			public MagicItem Magic;
		}

		/// <summary>Chance (0-1) that a kill drops gear at all.</summary>
		public static float DropChance(in Context ctx)
		{
			LootTables t = DataStore.Loot;
			float chance = t.DropChanceBase + t.DropChancePerBiomeStep * ctx.BiomeStep + t.DropChancePerStar * ctx.Stars;
			chance *= Plugin.DropMultiplier.Value;
			if (ctx.Swarm) chance *= t.SwarmDropMultiplier;
			if (ctx.DropMultiplier > 0f) chance *= ctx.DropMultiplier;
			return Mathf.Clamp01(chance);
		}

		/// <summary>Runs the whole roll. Returns null if nothing drops.</summary>
		public static Result Roll(Context ctx)
		{
			LootTables t = DataStore.Loot;

			// 1. Drop chance
			if (!ctx.Guaranteed)
			{
				float chance = DropChance(ctx);
				if (Random.value >= chance) return null;
			}

			// 2. Item biome: mostly the mob's biome, small chance of the next one up. Tier odds stay the mob's.
			int itemStep = ctx.BiomeStep;
			if (Random.value < t.NextBiomeChance && itemStep < t.BiomeOrder.Count - 1) itemStep++;

			// 3. Weapon or armor/trinket
			bool weapon = Random.value < Plugin.WeaponShare.Value;
			List<ItemPool.Entry> pool = ItemPool.Get(itemStep, weapon) ?? ItemPool.Get(itemStep, !weapon);
			if (pool == null || pool.Count == 0) return null;

			// 4. Unique check. If no Unique fits the chosen item, it becomes a Legendary instead.
			bool unique = Random.value * 100f < UniqueChancePercent(ctx);
			ItemPool.Entry entry = pool[Random.Range(0, pool.Count)];
			UniqueDef uniqueDef = null;
			if (unique)
			{
				uniqueDef = AffixRoller.PickUnique(entry.Shared, itemStep);
				if (uniqueDef == null)
				{
					// Try other items of the same kind that have a Unique, before falling back to Legendary.
					var withUnique = new List<ItemPool.Entry>();
					foreach (ItemPool.Entry e in pool)
						if (AffixRoller.AnyUniqueFits(e.Shared, itemStep)) withUnique.Add(e);
					if (withUnique.Count > 0)
					{
						entry = withUnique[Random.Range(0, withUnique.Count)];
						uniqueDef = AffixRoller.PickUnique(entry.Shared, itemStep);
					}
				}
			}

			// 5 + 6. Tier, then the star bump (or the boss bump).
			Tier tier;
			if (uniqueDef != null)
			{
				tier = Tier.Unique;
			}
			else
			{
				tier = unique ? Tier.Legendary : RollTier(ctx.BiomeStep, ctx.Quality);
				if (!unique)
				{
					if (ctx.Boss)
					{
						if (Random.value < t.BossTierBumpChance) tier = Tiers.Bump(tier);
					}
					else
					{
						float bump = Mathf.Min(t.StarBumpPerStar * ctx.Stars, t.StarBumpMax);
						if (ctx.Quality > 1f) bump = Mathf.Min(1f, bump * ctx.Quality);
						if (Random.value < bump) tier = Tiers.Bump(tier);
					}
				}
			}

			var temp = new ItemDrop.ItemData { m_shared = entry.Shared };
			MagicItem magic = AffixRoller.Roll(temp, tier, uniqueDef);
			// If the item has no affixes that fit at this tier, try lower tiers before giving up.
			while (magic == null && tier > Tier.Uncommon && tier != Tier.Unique)
			{
				tier = tier - 1;
				magic = AffixRoller.Roll(temp, tier, null);
			}
			if (magic == null) return null;

			Plugin.Debug($"[Loot] {ctx.Source}: {Tiers.GetLabel(magic.Tier)} {entry.Prefab.name}" +
			             (magic.IsUnique ? $" ({magic.UniqueId})" : "") + $" [{string.Join(", ", magic.Affixes.ConvertAll(a => a.Id + "=" + a.Value))}]");
			return new Result { Prefab = entry.Prefab, Magic = magic };
		}

		/// <summary>Builds the mod data for a freshly crafted item: rolls like a 0-star drop from the item's own biome.</summary>
		public static MagicItem RollForCrafted(ItemDrop.ItemData item, GameObject prefab)
		{
			int step = ItemPool.GetItemBiomeStep(prefab);
			if (step < 0) step = 0;
			var ctx = new Context { BiomeStep = step, Stars = 0, Source = "crafted " + prefab.name };

			bool unique = Random.value * 100f < UniqueChancePercent(ctx);
			UniqueDef uniqueDef = unique ? AffixRoller.PickUnique(item.m_shared, step) : null;
			Tier tier = uniqueDef != null ? Tier.Unique : unique ? Tier.Legendary : RollTier(step);

			MagicItem magic = AffixRoller.Roll(item, tier, uniqueDef);
			while (magic == null && tier > Tier.Uncommon && tier != Tier.Unique)
			{
				tier = tier - 1;
				magic = AffixRoller.Roll(item, tier, null);
			}
			if (magic != null) magic.Crafted = true;
			return magic;
		}

		public static float UniqueChancePercent(in Context ctx)
		{
			LootTables t = DataStore.Loot;
			UniqueChancePreset preset = Plugin.UniqueChance.Value;
			if (preset == UniqueChancePreset.Off) return 0f;
			if (preset == UniqueChancePreset.Always) return 100f;
			// Every chance below is scaled by loot.yml's uniqueChanceScale (0.5 by default) and the config's Unique drop rate (1-5x).
			float scale = Mathf.Max(0f, t.UniqueChanceScale) * Mathf.Clamp(Plugin.UniqueDropRate.Value, 1f, 5f);
			if (ctx.UniquePercent > 0f) return Mathf.Min(100f, ctx.UniquePercent * scale);

			string biome = t.BiomeAt(ctx.BiomeStep);
			float chance = t.UniqueBaseChance != null && t.UniqueBaseChance.TryGetValue(biome, out float b) ? b : 1f;
			chance += t.UniqueChancePerStar * ctx.Stars + ctx.UniqueFind;
			switch (preset)
			{
				case UniqueChancePreset.Half: chance *= 0.5f; break;
				case UniqueChancePreset.Triple: chance *= 3f; break;
				case UniqueChancePreset.Ten: chance *= 10f; break;
			}
			if (ctx.Boss) return Mathf.Min(100f, chance * t.BossUniqueMultiplier * scale); // bosses can go past the cap
			if (ctx.UniqueMultiplier > 0f) return Mathf.Min(100f, Mathf.Min(chance * ctx.UniqueMultiplier, t.UniqueChanceCap * ctx.UniqueMultiplier) * scale);
			return Mathf.Min(100f, Mathf.Min(chance, t.UniqueChanceCap) * scale);
		}

		/// <summary>Tier from the biome's odds table (Uncommon, Magic, Rare, Epic, Legendary).</summary>
		public static Tier RollTier(int biomeStep, float quality = 0f)
		{
			float q = quality > 1f ? quality : 1f;   // each tier step up is q times as likely
			LootTables t = DataStore.Loot;
			string biome = t.BiomeAt(biomeStep);
			if (t.TierOdds == null || !t.TierOdds.TryGetValue(biome, out List<float> odds) || odds == null || odds.Count == 0)
				return Tier.Uncommon;
			float total = 0f;
			for (int i = 0; i < odds.Count && i < 5; i++) total += Mathf.Max(0f, odds[i]) * Mathf.Pow(q, i);
			float r = Random.value * total;
			for (int i = 0; i < odds.Count && i < 5; i++)
			{
				r -= Mathf.Max(0f, odds[i]) * Mathf.Pow(q, i);
				if (r <= 0f) return (Tier)(i + 1);
			}
			return (Tier)Mathf.Min(odds.Count, 5);
		}
	}
}
