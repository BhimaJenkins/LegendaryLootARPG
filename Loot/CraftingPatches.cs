using HarmonyLib;
using LegendaryLoot.Core;
using UnityEngine;

namespace LegendaryLoot.Loot
{
	/// <summary>
	/// Crafting:
	///  - A newly crafted weapon, armor piece or trinket rolls a tier and affixes like a 0-star drop from its biome.
	///  - Upgrading keeps the item's tier and affixes exactly as they were. Only vanilla stats change.
	/// Vanilla upgrades by removing the old item and adding a fresh one, so the mod data is copied across here.
	/// </summary>
	[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.DoCrafting))]
	public static class CraftingPatches
	{
		private enum Mode { None, NewItem, Upgrade }

		private static Mode _mode = Mode.None;
		private static MagicItem _upgradeMagic;

		private static void Prefix(InventoryGui __instance)
		{
			if (__instance.m_craftUpgradeItem != null)
			{
				_mode = Mode.Upgrade;
				_upgradeMagic = __instance.m_craftUpgradeItem.GetMagic();
			}
			else
			{
				_mode = Mode.NewItem;
				_upgradeMagic = null;
			}
		}

		private static void Finalizer()
		{
			_mode = Mode.None;
			_upgradeMagic = null;
		}

		[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem),
			typeof(string), typeof(int), typeof(int), typeof(int), typeof(long), typeof(string), typeof(Vector2i), typeof(bool), typeof(bool), typeof(bool))]
		private static class Patch_AddCraftedItem
		{
			private static void Postfix(ItemDrop.ItemData __result, string name)
			{
				if (_mode == Mode.None || __result == null) return;

				if (_mode == Mode.Upgrade)
				{
					if (_upgradeMagic != null) __result.SetMagic(_upgradeMagic);
					return;
				}

				if (!Plugin.CraftedItemsRoll.Value || __result.IsMagic()) return;
				if (!ItemClassifier.CanBeMagic(__result.m_shared)) return;
				GameObject prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(name) : null;
				if (prefab == null) return;
				MagicItem magic = LootRoller.RollForCrafted(__result, prefab);
				if (magic != null) __result.SetMagic(magic);
			}
		}
	}
}
