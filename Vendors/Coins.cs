using System.Globalization;
using HarmonyLib;
using LegendaryLoot.Core;
using LegendaryLoot.Data;
using LegendaryLoot.Loot;
using UnityEngine;

namespace LegendaryLoot.Vendors
{
	/// <summary>
	/// Coins weigh nothing and stack to 999,999 (vendors.yml). Slots show a short amount (12.5k, 1.25M);
	/// the tooltip shows the exact number.
	/// </summary>
	public static class Coins
	{
		public const string PrefabName = "Coins";
		public const string SharedName = "$item_coins";

		static Coins()
		{
			DataStore.Reloaded += Apply;
		}

		public static bool IsCoin(ItemDrop.ItemData item) =>
			item != null && (item.m_shared.m_name == SharedName || (item.m_dropPrefab != null && item.m_dropPrefab.name == PrefabName));

		public static GameObject Prefab => ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(PrefabName) : null;

		public static void Apply()
		{
			GameObject prefab = Prefab;
			ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
			if (drop == null) return;
			VendorTables v = DataStore.Vendors;
			ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
			if (v.CoinStackSize > 0) shared.m_maxStackSize = v.CoinStackSize;
			if (v.CoinsWeightless) shared.m_weight = 0f;
		}

		/// <summary>12,345 → "12.3k"; 1,250,000 → "1.25M". Under 10,000 shows the plain number.</summary>
		public static string Short(int amount)
		{
			CultureInfo c = CultureInfo.InvariantCulture;
			if (amount < 10000) return amount.ToString(c);
			if (amount < 100000) return (Mathf.Floor(amount / 100f) / 10f).ToString("0.#", c) + "k";
			if (amount < 1000000) return (amount / 1000).ToString(c) + "k";
			if (amount < 10000000) return (Mathf.Floor(amount / 10000f) / 100f).ToString("0.##", c) + "M";
			return (Mathf.Floor(amount / 100000f) / 10f).ToString("0.#", c) + "M";
		}

		public static string Exact(int amount) => amount.ToString("N0", CultureInfo.InvariantCulture);

		/// <summary>Adds coins to the player's inventory, dropping whatever doesn't fit at their feet.</summary>
		public static void Give(Player player, int amount)
		{
			if (player == null || amount <= 0) return;
			GameObject prefab = Prefab;
			if (prefab == null) return;
			Inventory inv = player.GetInventory();
			if (inv.CanAddItem(prefab, amount) && inv.AddItem(prefab, amount)) return;

			ItemDrop.ItemData data = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
			data.m_dropPrefab = prefab;
			data.m_stack = amount;
			Transform t = player.transform;
			ItemDrop.DropItem(data, amount, t.position + t.forward * 0.6f + Vector3.up, t.rotation);
			player.Message(MessageHud.MessageType.Center, "Your bags are full. The coins fell at your feet.");
		}

		[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
		private static class Patch_Awake
		{
			private static void Postfix() => Apply();
		}

		[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
		private static class Patch_Copy
		{
			private static void Postfix() => Apply();
		}

		/// <summary>Short amount in the slot (vanilla writes "12500/999999").</summary>
		[HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGui))]
		private static class Patch_GridAmount
		{
			private static void Postfix(InventoryGrid __instance)
			{
				Inventory inv = __instance.m_inventory;
				if (inv == null) return;
				foreach (ItemDrop.ItemData item in inv.GetAllItems())
				{
					if (!IsCoin(item)) continue;
					InventoryElement el = __instance.GetElement(item.m_gridPos.x, item.m_gridPos.y, __instance.m_width);
					if (el != null && el.m_amount != null) el.m_amount.text = Short(item.m_stack);
				}
			}
		}

		/// <summary>Exact coin count on coins, and the sell value on mod gear.</summary>
		[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
			typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool))]
		[HarmonyPriority(Priority.VeryLow)]
		private static class Patch_Tooltip
		{
			private static void Postfix(ItemDrop.ItemData item, int stackOverride, ref string __result)
			{
				if (item == null || __result == null) return;
				if (IsCoin(item))
				{
					int n = stackOverride > 0 ? stackOverride : item.m_stack;
					__result = __result.TrimEnd() + $"\n\n<color={GoldHex}>{Exact(n)} coins</color>";
					return;
				}
				int price = SellPrice.Of(item);
				if (price > 0)
					__result = __result.TrimEnd() + $"\n\n<color=#A8A8A8>Sells for</color> <color={GoldHex}>{Exact(price)} coins</color>";
			}
		}

		public const string GoldHex = "#E8C547";
	}

	/// <summary>Sell value of mod gear: tier price × the item's biome sell multiplier (crafted items sell for less).</summary>
	public static class SellPrice
	{
		public static int Of(ItemDrop.ItemData item)
		{
			MagicItem magic = item?.GetMagic();
			if (magic == null) return 0;
			VendorTables v = DataStore.Vendors;
			float price = v.TierPrice(magic.Tier);
			if (price <= 0f) return 0;
			int step = ItemPool.GetItemBiomeStep(item.m_dropPrefab);
			price *= VendorTables.AtStep(v.BiomeSellMultiplier, Mathf.Max(0, step));
			if (magic.Crafted) price *= v.CraftedSellShare;
			return Mathf.Max(1, Mathf.RoundToInt(price)) * Mathf.Max(1, item.m_stack);
		}

		public static bool CanSell(ItemDrop.ItemData item) => item != null && !item.m_equipped && Of(item) > 0;
	}
}
