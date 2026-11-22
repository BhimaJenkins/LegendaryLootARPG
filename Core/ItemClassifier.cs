using System;
using UnityEngine;
using System.Collections.Generic;
using LegendaryLoot.Data;

namespace LegendaryLoot.Core
{
	/// <summary>What kind of gear an item is, for deciding which affixes and Uniques fit it.</summary>
	public enum ItemCategory
	{
		None,
		OneHanded,
		TwoHanded,
		TwoHandedLeft,  // dual knives, fist weapons and other off-hand-pair weapons
		Bow,
		Crossbow,
		Staff,
		Torch,
		Shield,
		Helmet,
		Chest,
		Legs,
		Shoulder,       // capes
		Utility,        // belts, wishbone, etc.
		Trinket,
	}

	public static class ItemClassifier
	{
		public static bool IsWeaponCategory(ItemCategory c) =>
			c == ItemCategory.OneHanded || c == ItemCategory.TwoHanded || c == ItemCategory.TwoHandedLeft ||
			c == ItemCategory.Bow || c == ItemCategory.Crossbow || c == ItemCategory.Staff || c == ItemCategory.Torch;

		public static bool IsArmorCategory(ItemCategory c) =>
			c == ItemCategory.Shield || c == ItemCategory.Helmet || c == ItemCategory.Chest || c == ItemCategory.Legs ||
			c == ItemCategory.Shoulder || c == ItemCategory.Utility || c == ItemCategory.Trinket;

		/// <summary>
		/// Classifies by item type first, then by skill type, so modded weapons (Therzie's, WoW Weapons) work
		/// without a list. Staffs are any weapon whose skill is on the magic skill list (ElementalMagic, BloodMagic,
		/// plus any modded magic schools added in loot.yml).
		/// </summary>
		public static ItemCategory Classify(ItemDrop.ItemData.SharedData shared)
		{
			if (shared == null) return ItemCategory.None;
			Skills.SkillType skill = shared.m_skillType;
			string skillName = SkillName(skill);

			switch (shared.m_itemType)
			{
				case ItemDrop.ItemData.ItemType.OneHandedWeapon:
				case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
				case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:
				case ItemDrop.ItemData.ItemType.Bow:
					if (DataStore.Loot.IsMagicSkill(skillName)) return ItemCategory.Staff;
					if (skill == Skills.SkillType.Crossbows) return ItemCategory.Crossbow;
					if (skill == Skills.SkillType.Bows || shared.m_itemType == ItemDrop.ItemData.ItemType.Bow) return ItemCategory.Bow;
					if (shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon) return ItemCategory.OneHanded;
					if (shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft) return ItemCategory.TwoHandedLeft;
					return ItemCategory.TwoHanded;
				case ItemDrop.ItemData.ItemType.Torch: return ItemCategory.Torch;
				case ItemDrop.ItemData.ItemType.Shield: return ItemCategory.Shield;
				case ItemDrop.ItemData.ItemType.Helmet: return ItemCategory.Helmet;
				case ItemDrop.ItemData.ItemType.Chest: return ItemCategory.Chest;
				case ItemDrop.ItemData.ItemType.Legs: return ItemCategory.Legs;
				case ItemDrop.ItemData.ItemType.Shoulder: return ItemCategory.Shoulder;
				case ItemDrop.ItemData.ItemType.Utility: return ItemCategory.Utility;
				case ItemDrop.ItemData.ItemType.Trinket: return ItemCategory.Trinket;
				default: return ItemCategory.None;
			}
		}

		/// <summary>True if this item can carry tiers and affixes at all (drops, crafting, vendors).</summary>
		public static bool CanBeMagic(ItemDrop.ItemData.SharedData shared)
		{
			ItemCategory c = Classify(shared);
			if (c == ItemCategory.None) return false;
			// Bombs, throwables and other consumables: stackable, or used up when you attack with them.
			if (shared.m_maxStackSize > 1) return false;
			if (shared.m_attack != null && shared.m_attack.m_consumeItem) return false;
			// A secondary attack that uses up the item is fine when it's a throw that lands back on the ground as
			// the item (spears): the weapon isn't consumed, it's picked up again.
			if (shared.m_secondaryAttack != null && shared.m_secondaryAttack.m_consumeItem && !IsThrownWeapon(shared)) return false;
			if (DataStore.Loot.ExcludedCategories.Contains(c.ToString())) return false;
			if (DataStore.Loot.IsExcludedSkill(SkillName(shared.m_skillType))) return false; // pickaxes, fishing rods
			return true;
		}

		/// <summary>True for weapons whose throw drops the weapon itself where it lands (spears and the like).</summary>
		public static bool IsThrownWeapon(ItemDrop.ItemData.SharedData shared)
		{
			if (shared.m_skillType == Skills.SkillType.Spears) return true;
			GameObject proj = shared.m_secondaryAttack?.m_attackProjectile;
			Projectile p = proj != null ? proj.GetComponent<Projectile>() : null;
			return p != null && p.m_respawnItemOnHit;
		}

		/// <summary>Skill name as text. Modded skills (registered as big numbers) come back as their number.</summary>
		public static string SkillName(Skills.SkillType skill)
		{
			return Enum.IsDefined(typeof(Skills.SkillType), skill) ? skill.ToString() : ((int)skill).ToString();
		}

		/// <summary>Short display name for tooltips and logs.</summary>
		public static string DisplayName(ItemCategory c)
		{
			switch (c)
			{
				case ItemCategory.OneHanded: return "One-handed weapon";
				case ItemCategory.TwoHanded: return "Two-handed weapon";
				case ItemCategory.TwoHandedLeft: return "Dual weapon";
				case ItemCategory.Shoulder: return "Cape";
				default: return c.ToString();
			}
		}

		public static readonly ItemCategory[] AllCategories = (ItemCategory[])Enum.GetValues(typeof(ItemCategory));
	}
}
