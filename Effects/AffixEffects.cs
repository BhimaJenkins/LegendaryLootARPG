using System;
using System.Collections.Generic;
using HarmonyLib;
using LegendaryLoot.Core;
using UnityEngine;

namespace LegendaryLoot.Effects
{
	/// <summary>
	/// Affix IDs (from the Sheet's Internal ID column) that have working code. Only these can roll, so a player
	/// never gets an affix that does nothing. Add an ID here when its effect is written.
	/// </summary>
	public static class AffixEffects
	{
		private static readonly HashSet<string> Implemented = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
		{
			// Item stats (apply to the item itself)
			"ModifyArmor", "ModifyDamage", "ModifyBlockPower",
			"AddFireDamage", "AddFrostDamage", "AddLightningDamage", "AddPoisonDamage", "AddSpiritDamage",
			"AddBluntDamage", "AddSlashingDamage",

			// Resistances (sum of all equipped gear)
			"AddBluntResistancePercentage", "AddSlashingResistancePercentage", "AddPiercingResistancePercentage",
			"AddFireResistancePercentage", "AddFrostResistancePercentage", "AddLightningResistancePercentage",
			"AddPoisonResistancePercentage", "AddSpiritResistancePercentage",
			"AddElementalResistancePercentage", "AddPhysicalResistancePercentage",

			// Health, stamina, eitr, carry weight
			"IncreaseHealth", "IncreaseStamina", "IncreaseEitr",
			"ModifyHealthRegen", "ModifyStaminaRegen", "ModifyEitrRegen",
			"AddCarryWeight", "hercules",

			// Movement and stamina costs (fed into vanilla's equipment modifiers)
			"ModifyMovementSpeed", "RemoveSpeedPenalty",
			"ModifyJumpStaminaUse", "ModifySprintStaminaUse", "ModifyDodgeStaminaUse",
			"ModifyAttackStaminaUse", "ModifyBlockStaminaUse",

			// Skills
			"AddSwordsSkill", "AddKnivesSkill", "AddClubsSkill", "AddPolearmsSkill", "AddSpearsSkill",
			"AddBowsSkill", "AddCrossbowsSkill", "AddUnarmedSkill", "AddBlockingSkill",
			"AddElementalMagicSkill", "AddBloodMagicSkill", "AddMovementSkills", "AddAllSkills",

			// Loot
			"Luck",

			// Crits (Powers/Crits.cs)
			"CritChance", "CritDamage",
			"ModifyAttackSpeed", "QuickDraw", // Powers/AttackSpeed.cs
		};

		/// <summary>Coded here, or given a power in powers.yml.</summary>
		public static bool IsImplemented(string id) => Implemented.Contains(id) || Powers.PowerStore.HasAffix(id);

		/// <summary>Lets other code (or future addons) register working affixes.</summary>
		public static void Register(string id) => Implemented.Add(id);
	}

	/// <summary>Totals of every affix on a player's equipped gear, cached for one frame.</summary>
	public static class PlayerStats
	{
		private class Entry
		{
			public int Frame = -1;
			public readonly Dictionary<string, float> Totals = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
		}

		private static readonly Dictionary<Humanoid, Entry> Cache = new Dictionary<Humanoid, Entry>();

		public static float Get(Humanoid h, string affixId)
		{
			if (h == null) return 0f;
			Entry e = GetEntry(h);
			return e.Totals.TryGetValue(affixId, out float v) ? v : 0f;
		}

		public static bool Has(Humanoid h, string affixId) => Get(h, affixId) > 0f;

		public static IEnumerable<ItemDrop.ItemData> EquippedItems(Humanoid h)
		{
			if (h.m_rightItem != null) yield return h.m_rightItem;
			if (h.m_leftItem != null) yield return h.m_leftItem;
			if (h.m_chestItem != null) yield return h.m_chestItem;
			if (h.m_legItem != null) yield return h.m_legItem;
			if (h.m_helmetItem != null) yield return h.m_helmetItem;
			if (h.m_shoulderItem != null) yield return h.m_shoulderItem;
			if (h.m_utilityItem != null) yield return h.m_utilityItem;
			if (h.m_trinketItem != null) yield return h.m_trinketItem;
		}

		private static Entry GetEntry(Humanoid h)
		{
			if (!Cache.TryGetValue(h, out Entry e))
			{
				if (Cache.Count > 64) Cache.Clear(); // drop entries for players who left
				e = new Entry();
				Cache[h] = e;
			}
			if (e.Frame == Time.frameCount) return e;
			e.Frame = Time.frameCount;
			e.Totals.Clear();
			foreach (ItemDrop.ItemData item in EquippedItems(h))
			{
				MagicItem magic = item.GetMagic();
				if (magic == null) continue;
				foreach (RolledAffix a in magic.Affixes)
				{
					e.Totals.TryGetValue(a.Id, out float v);
					e.Totals[a.Id] = v + a.Value;
				}
				Powers.PowerEngine.AddPassiveStats(magic, e.Totals); // Unique stats and immunities
			}
			return e;
		}
	}

	// ----------------------------------------------------------------------------------------------
	// Item stats. These hook the same functions vanilla uses for both combat and tooltips, so the numbers
	// in the tooltip (and in Keen Combat's tooltip) always match what the weapon really does.
	// ----------------------------------------------------------------------------------------------

	[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetDamage), typeof(int), typeof(float))]
	public static class Patch_GetDamage
	{
		private static void Postfix(ItemDrop.ItemData __instance, ref HitData.DamageTypes __result)
		{
			MagicItem magic = __instance.GetMagic();
			if (magic == null) return;

			// "X% of weapon damage as <type>" uses the unenchanted total, so affixes don't feed each other.
			float baseTotal = __result.GetTotalDamage();
			float allDamage = 0f;
			foreach (RolledAffix a in magic.Affixes)
			{
				float pct = a.Value / 100f;
				switch (a.Id)
				{
					case "AddFireDamage": __result.m_fire += baseTotal * pct; break;
					case "AddFrostDamage": __result.m_frost += baseTotal * pct; break;
					case "AddLightningDamage": __result.m_lightning += baseTotal * pct; break;
					case "AddPoisonDamage": __result.m_poison += baseTotal * pct; break;
					case "AddSpiritDamage": __result.m_spirit += baseTotal * pct; break;
					case "AddBluntDamage": __result.m_blunt += baseTotal * pct; break;
					case "AddSlashingDamage":
						__result.m_slash += baseTotal * pct;
						if (__instance.m_shared.m_skillType == Skills.SkillType.Axes) __result.m_chop += baseTotal * pct;
						break;
					case "ModifyDamage": allDamage += pct; break;
				}
			}
			// "All Damage +X%" applies last, including the added elemental damage (as the Sheet describes).
			if (allDamage > 0f) __result.Modify(1f + allDamage);
		}
	}

	[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetArmor), typeof(int), typeof(float))]
	public static class Patch_GetArmor
	{
		private static void Postfix(ItemDrop.ItemData __instance, ref float __result)
		{
			float bonus = __instance.GetAffix("ModifyArmor");
			if (bonus > 0f) __result += bonus;
		}
	}

	[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetBaseBlockPower), typeof(int))]
	public static class Patch_GetBaseBlockPower
	{
		private static void Postfix(ItemDrop.ItemData __instance, ref float __result)
		{
			float pct = __instance.GetAffix("ModifyBlockPower");
			// Blocking mastery: every 5 points over 100 adds 1% block power to the shield or weapon you block with.
			Player p = Player.m_localPlayer;
			if (p != null && p.IsItemEquiped(__instance)) pct += Powers.AttackSpeed.MasteryBonus(p, Skills.SkillType.Blocking);
			if (pct > 0f) __result *= 1f + pct / 100f;
		}
	}

	// ----------------------------------------------------------------------------------------------
	// Player stats (sum of all equipped gear)
	// ----------------------------------------------------------------------------------------------

	[HarmonyPatch(typeof(Player), nameof(Player.GetTotalFoodValue))]
	public static class Patch_GetTotalFoodValue
	{
		private static void Postfix(Player __instance, ref float hp, ref float stamina, ref float eitr)
		{
			hp += PlayerStats.Get(__instance, "IncreaseHealth");
			stamina += PlayerStats.Get(__instance, "IncreaseStamina");
			eitr += PlayerStats.Get(__instance, "IncreaseEitr");
			// Percent bonuses (Uniques) apply to the full pool, base included.
			float hpPct = PlayerStats.Get(__instance, "HealthPercent");
			if (hpPct != 0f) hp += (hp + __instance.m_baseHP) * hpPct / 100f;
			float stPct = PlayerStats.Get(__instance, "StaminaPercent");
			if (stPct != 0f) stamina += (stamina + __instance.m_baseStamina) * stPct / 100f;
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.GetMaxCarryWeight))]
	public static class Patch_GetMaxCarryWeight
	{
		private static void Postfix(Player __instance, ref float __result)
		{
			float pct = PlayerStats.Get(__instance, "hercules");
			if (pct > 0f) __result *= 1f + pct / 100f;
			__result += PlayerStats.Get(__instance, "AddCarryWeight");
		}
	}

	[HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyHealthRegen))]
	public static class Patch_HealthRegen
	{
		private static void Postfix(SEMan __instance, ref float regenMultiplier)
		{
			if (__instance.m_character is Player p) regenMultiplier *= 1f + PlayerStats.Get(p, "ModifyHealthRegen") / 100f;
		}
	}

	[HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyStaminaRegen))]
	public static class Patch_StaminaRegen
	{
		private static void Postfix(SEMan __instance, ref float staminaMultiplier)
		{
			if (__instance.m_character is Player p) staminaMultiplier *= 1f + PlayerStats.Get(p, "ModifyStaminaRegen") / 100f;
		}
	}

	[HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyEitrRegen))]
	public static class Patch_EitrRegen
	{
		private static void Postfix(SEMan __instance, ref float eitrMultiplier)
		{
			if (__instance.m_character is Player p) eitrMultiplier *= 1f + PlayerStats.Get(p, "ModifyEitrRegen") / 100f;
		}
	}

	/// <summary>Skill bonuses go through vanilla's own skill-level hook, so they show in the skills screen too.</summary>
	[HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifySkillLevel))]
	public static class Patch_SkillLevel
	{
		private static void Postfix(SEMan __instance, Skills.SkillType skill, ref float level)
		{
			if (!(__instance.m_character is Player p)) return;
			float bonus = PlayerStats.Get(p, "AddAllSkills");
			switch (skill)
			{
				case Skills.SkillType.Swords: bonus += PlayerStats.Get(p, "AddSwordsSkill"); break;
				case Skills.SkillType.Knives: bonus += PlayerStats.Get(p, "AddKnivesSkill"); break;
				case Skills.SkillType.Clubs: bonus += PlayerStats.Get(p, "AddClubsSkill"); break;
				case Skills.SkillType.Polearms: bonus += PlayerStats.Get(p, "AddPolearmsSkill"); break;
				case Skills.SkillType.Spears: bonus += PlayerStats.Get(p, "AddSpearsSkill"); break;
				case Skills.SkillType.Bows: bonus += PlayerStats.Get(p, "AddBowsSkill"); break;
				case Skills.SkillType.Crossbows: bonus += PlayerStats.Get(p, "AddCrossbowsSkill"); break;
				case Skills.SkillType.Unarmed: bonus += PlayerStats.Get(p, "AddUnarmedSkill"); break;
				case Skills.SkillType.Blocking: bonus += PlayerStats.Get(p, "AddBlockingSkill"); break;
				case Skills.SkillType.ElementalMagic: bonus += PlayerStats.Get(p, "AddElementalMagicSkill"); break;
				case Skills.SkillType.BloodMagic: bonus += PlayerStats.Get(p, "AddBloodMagicSkill"); break;
				case Skills.SkillType.Run:
				case Skills.SkillType.Jump:
				case Skills.SkillType.Swim:
				case Skills.SkillType.Sneak:
					bonus += PlayerStats.Get(p, "AddMovementSkills"); break;
			}
			level += bonus;
		}
	}

	/// <summary>
	/// Movement speed and stamina costs are added to vanilla's equipment modifiers, right after vanilla sums
	/// them from gear. Vanilla stores reductions as negatives (-0.1 = 10% less), so "-X%" affixes add -X/100.
	/// </summary>
	[HarmonyPatch(typeof(Player), nameof(Player.UpdateModifiers))]
	public static class Patch_UpdateModifiers
	{
		private static void Postfix(Player __instance)
		{
			float[] values = __instance.m_equipmentModifierValues;
			var fields = Player.s_equipmentModifierSourceFields;
			if (values == null || fields == null) return;

			for (int i = 0; i < fields.Length && i < values.Length; i++)
			{
				switch (fields[i].Name)
				{
					case "m_movementModifier":
						values[i] += PlayerStats.Get(__instance, "ModifyMovementSpeed") / 100f;
						if (__instance == Player.m_localPlayer) values[i] += Powers.Buffs.Get("moveSpeed") / 100f;
						values[i] += UnhinderedRefund(__instance);
						break;
					case "m_jumpStaminaModifier": values[i] -= PlayerStats.Get(__instance, "ModifyJumpStaminaUse") / 100f; break;
					case "m_runStaminaModifier": values[i] -= PlayerStats.Get(__instance, "ModifySprintStaminaUse") / 100f; break;
					case "m_dodgeStaminaModifier": values[i] -= PlayerStats.Get(__instance, "ModifyDodgeStaminaUse") / 100f; break;
					case "m_attackStaminaModifier": values[i] -= PlayerStats.Get(__instance, "ModifyAttackStaminaUse") / 100f; break;
					case "m_blockStaminaModifier": values[i] -= PlayerStats.Get(__instance, "ModifyBlockStaminaUse") / 100f; break;
				}
			}
		}

		/// <summary>Unhindered: the item's own movement penalty is cancelled out.</summary>
		private static float UnhinderedRefund(Player p)
		{
			float refund = 0f;
			foreach (ItemDrop.ItemData item in PlayerStats.EquippedItems(p))
				if (item.m_shared.m_movementModifier < 0f && item.GetAffix("RemoveSpeedPenalty") > 0f)
					refund -= item.m_shared.m_movementModifier;
			return refund;
		}
	}

	/// <summary>
	/// Resistances: damage to the local player is reduced before armor, on the player's own game
	/// (vanilla applies damage on the owner's side, which for a player is always their own client).
	/// Each type is capped at 75% so gear can never make a player immune.
	/// </summary>
	[HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
	public static class Patch_Resistances
	{
		private const float Cap = 0.75f;

		private static void Prefix(Character __instance, HitData hit)
		{
			if (!(__instance is Player p) || p != Player.m_localPlayer || hit == null) return;

			float physical = PlayerStats.Get(p, "AddPhysicalResistancePercentage");
			float elemental = PlayerStats.Get(p, "AddElementalResistancePercentage");

			hit.m_damage.m_blunt *= Mult(physical + PlayerStats.Get(p, "AddBluntResistancePercentage"));
			hit.m_damage.m_slash *= Mult(physical + PlayerStats.Get(p, "AddSlashingResistancePercentage"));
			hit.m_damage.m_pierce *= Mult(physical + PlayerStats.Get(p, "AddPiercingResistancePercentage"));
			hit.m_damage.m_fire *= Mult(elemental + PlayerStats.Get(p, "AddFireResistancePercentage"));
			hit.m_damage.m_frost *= Mult(elemental + PlayerStats.Get(p, "AddFrostResistancePercentage"));
			hit.m_damage.m_lightning *= Mult(elemental + PlayerStats.Get(p, "AddLightningResistancePercentage"));
			hit.m_damage.m_poison *= Mult(elemental + PlayerStats.Get(p, "AddPoisonResistancePercentage"));
			hit.m_damage.m_spirit *= Mult(PlayerStats.Get(p, "AddSpiritResistancePercentage"));
		}

		private static float Mult(float percent) => 1f - Mathf.Clamp(percent / 100f, 0f, Cap);
	}
}
