using System;
using System.Collections.Generic;
using LegendaryLoot.Core;
using LegendaryLoot.Data;
using LegendaryLoot.Effects;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LegendaryLoot.Loot
{
	/// <summary>Picks which affixes and which Unique fit a given item, and rolls their values.</summary>
	public static class AffixRoller
	{
		/// <summary>Builds the mod data for an item at a tier. Returns null if nothing fits.</summary>
		public static MagicItem Roll(ItemDrop.ItemData item, Tier tier, UniqueDef unique = null)
		{
			var magic = new MagicItem { Tier = tier };
			var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (tier == Tier.Unique)
			{
				if (unique == null) return null;
				magic.UniqueId = unique.Id;
			}

			int count = Tiers.AffixCount(tier);
			List<AffixDef> pool = GetEligibleAffixes(item, tier);
			for (int i = 0; i < count; i++)
			{
				AffixDef pick = PickWeighted(pool, used);
				if (pick == null) break;
				used.Add(pick.Id);
				magic.Affixes.Add(new RolledAffix(pick.Id, RollValue(pick, tier)));
			}

			if (magic.Affixes.Count == 0 && tier != Tier.Unique) return null;
			return magic;
		}

		public static List<AffixDef> GetEligibleAffixes(ItemDrop.ItemData item, Tier tier)
		{
			var result = new List<AffixDef>();
			ItemCategory cat = ItemClassifier.Classify(item.m_shared);
			string skill = ItemClassifier.SkillName(item.m_shared.m_skillType);
			foreach (AffixDef def in DataStore.Affixes)
			{
				if (def.RangeFor(tier) == null) continue;
				if (!AffixEffects.IsImplemented(def.Id)) continue; // never roll an affix that does nothing
				if (!Fits(def.AllowedOn, def.AllowedSkills, def.RequiredSkills, cat, skill)) continue;
				if (!MeetsRequirements(def, item)) continue;
				result.Add(def);
			}
			return result;
		}

		/// <summary>
		/// The shared fit rule for affixes and Uniques:
		/// allowed if the category is in AllowedOn OR the skill is in AllowedSkills (or both lists are empty),
		/// and, if RequiredSkills is set, the skill must also be one of those.
		/// </summary>
		public static bool Fits(List<string> allowedOn, List<string> allowedSkills, List<string> requiredSkills, ItemCategory cat, string skill)
		{
			// Armor and trinkets carry a weapon-skill field too (it defaults to Swords in Valheim), but it means
			// nothing on them: never let it match a skill list, or sword-only powers land on chestpieces.
			if (!UsesSkill(cat)) skill = "None";
			bool anyList = (allowedOn != null && allowedOn.Count > 0) || (allowedSkills != null && allowedSkills.Count > 0);
			bool ok = !anyList
				|| (allowedOn != null && MatchesTarget(allowedOn, cat.ToString(), skill))
				|| (allowedSkills != null && Contains(allowedSkills, skill));
			if (!ok) return false;
			if (requiredSkills != null && requiredSkills.Count > 0 && !Contains(requiredSkills, skill)) return false;
			return true;
		}

		private static bool UsesSkill(ItemCategory cat)
		{
			switch (cat)
			{
				case ItemCategory.Helmet:
				case ItemCategory.Chest:
				case ItemCategory.Legs:
				case ItemCategory.Shoulder:
				case ItemCategory.Utility:
				case ItemCategory.Trinket:
					return false;
				default:
					return true; // weapons, staffs, shields (Blocking)
			}
		}

		/// <summary>Entries are a category ("Bow") or a category plus skill ("OneHanded+Clubs" = one-handed maces only).</summary>
		private static bool MatchesTarget(List<string> list, string category, string skill)
		{
			foreach (string s in list)
			{
				int plus = s.IndexOf('+');
				if (plus < 0)
				{
					if (string.Equals(s, category, StringComparison.OrdinalIgnoreCase)) return true;
				}
				else if (string.Equals(s.Substring(0, plus), category, StringComparison.OrdinalIgnoreCase) &&
				         string.Equals(s.Substring(plus + 1), skill, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
			return false;
		}

		private static bool Contains(List<string> list, string value)
		{
			foreach (string s in list)
				if (string.Equals(s, value, StringComparison.OrdinalIgnoreCase)) return true;
			return false;
		}

		/// <summary>Checks that depend on the actual item, e.g. Frost Cone only on weapons that deal frost damage.</summary>
		private static bool MeetsRequirements(AffixDef def, ItemDrop.ItemData item)
		{
			if (def.Requires == null) return true;
			foreach (string req in def.Requires)
			{
				HitData.DamageTypes d = item.m_shared.m_damages;
				switch (req)
				{
					case "frostDamage": if (d.m_frost <= 0f) return false; break;
					case "fireDamage": if (d.m_fire <= 0f) return false; break;
					case "lightningDamage": if (d.m_lightning <= 0f) return false; break;
					case "poisonDamage": if (d.m_poison <= 0f) return false; break;
					case "spiritDamage": if (d.m_spirit <= 0f) return false; break;
					case "chopDamage": if (d.m_chop <= 0f) return false; break;
					case "secondaryAttack": if (!item.HaveSecondaryAttack()) return false; break;
					case "movementPenalty": if (item.m_shared.m_movementModifier >= 0f) return false; break; // Unhindered needs a penalty to remove
				}
			}
			return true;
		}

		private static AffixDef PickWeighted(List<AffixDef> pool, HashSet<string> used)
		{
			float total = 0f;
			foreach (AffixDef a in pool)
				if (!used.Contains(a.Id)) total += Mathf.Max(0f, a.Weight);
			if (total <= 0f) return null;
			float r = Random.value * total;
			foreach (AffixDef a in pool)
			{
				if (used.Contains(a.Id)) continue;
				r -= Mathf.Max(0f, a.Weight);
				if (r <= 0f) return a;
			}
			return null;
		}

		/// <summary>Whole numbers if both ends of the range are whole, otherwise one decimal. Toggles are always 1.</summary>
		public static float RollValue(AffixDef def, Tier tier)
		{
			if (def.Toggle) return 1f;
			List<float> range = def.RangeFor(tier);
			float min = range[0];
			float max = range.Count > 1 ? range[1] : range[0];
			if (max < min) (min, max) = (max, min);
			bool whole = Mathf.Approximately(min, Mathf.Round(min)) && Mathf.Approximately(max, Mathf.Round(max));
			if (whole) return Random.Range((int)min, (int)max + 1);
			return Mathf.Round(Random.Range(min, max) * 10f) / 10f;
		}

		/// <summary>Picks a Unique that fits this item and whose minimum biome is at or below the drop's biome.</summary>
		public static UniqueDef PickUnique(ItemDrop.ItemData.SharedData shared, int biomeStep)
		{
			ItemCategory cat = ItemClassifier.Classify(shared);
			string skill = ItemClassifier.SkillName(shared.m_skillType);
			var candidates = new List<UniqueDef>();
			float total = 0f;
			foreach (UniqueDef u in DataStore.Uniques)
			{
				if (!u.Enabled || !UniqueEffects.IsImplemented(u.Id)) continue;
				int min = string.IsNullOrEmpty(u.MinBiome) || u.MinBiome == "Any" ? 0 : DataStore.Loot.BiomeStep(u.MinBiome);
				if (min > biomeStep) continue;
				if (!Fits(u.AllowedOn, u.AllowedSkills, u.RequiredSkills, cat, skill) || !u.AllowsItem(shared)) continue;
				candidates.Add(u);
				total += Mathf.Max(0f, u.Weight);
			}
			if (total <= 0f) return null;
			float r = Random.value * total;
			foreach (UniqueDef u in candidates)
			{
				r -= Mathf.Max(0f, u.Weight);
				if (r <= 0f) return u;
			}
			return candidates[candidates.Count - 1];
		}

		/// <summary>True if at least one implemented Unique could ever appear on this item at this biome.</summary>
		public static bool AnyUniqueFits(ItemDrop.ItemData.SharedData shared, int biomeStep) => PickUnique(shared, biomeStep) != null;
	}
}
