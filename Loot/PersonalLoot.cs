using System.Globalization;
using HarmonyLib;

namespace LegendaryLoot.Loot
{
	/// <summary>
	/// Personal loot: a drop can be reserved for one player. The reservation lives in the item's custom data
	/// (so every client sees it), and ends when the owner picks it up or the timer runs out.
	/// Once an item is in any inventory the reservation is gone, so dropping it again makes it public.
	/// Tombstones and chests are untouched.
	/// </summary>
	public static class PersonalLoot
	{
		public const string OwnerKey = "LLA.owner";
		public const string OwnerNameKey = "LLA.ownerName";
		public const string UntilKey = "LLA.ownerUntil";

		public static void Reserve(ItemDrop.ItemData item, long playerId, string playerName)
		{
			if (playerId == 0 || Plugin.PersonalLootMinutes.Value <= 0f || ZNet.instance == null) return;
			double until = ZNet.instance.GetTimeSeconds() + Plugin.PersonalLootMinutes.Value * 60.0;
			item.m_customData[OwnerKey] = playerId.ToString(CultureInfo.InvariantCulture);
			item.m_customData[OwnerNameKey] = playerName ?? "";
			item.m_customData[UntilKey] = until.ToString("0", CultureInfo.InvariantCulture);
		}

		public static void Clear(ItemDrop.ItemData item)
		{
			if (item?.m_customData == null) return;
			item.m_customData.Remove(OwnerKey);
			item.m_customData.Remove(OwnerNameKey);
			item.m_customData.Remove(UntilKey);
			item.m_customData.Remove(DropSound.DropTimeKey);
		}

		/// <summary>True if the item is reserved for someone other than this player right now.</summary>
		public static bool IsReservedForOther(ItemDrop.ItemData item, long playerId, out string ownerName)
		{
			ownerName = null;
			if (item?.m_customData == null || !item.m_customData.TryGetValue(OwnerKey, out string ownerText)) return false;
			if (!long.TryParse(ownerText, out long owner) || owner == 0 || owner == playerId) return false;
			if (item.m_customData.TryGetValue(UntilKey, out string untilText) &&
			    double.TryParse(untilText, NumberStyles.Float, CultureInfo.InvariantCulture, out double until) &&
			    ZNet.instance != null && ZNet.instance.GetTimeSeconds() > until)
				return false; // expired: anyone can take it
			item.m_customData.TryGetValue(OwnerNameKey, out ownerName);
			return true;
		}

		private static long LocalId => Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0;

		[HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.CanPickup))]
		private static class Patch_CanPickup
		{
			private static void Postfix(ItemDrop __instance, ref bool __result)
			{
				if (__result && IsReservedForOther(__instance.m_itemData, LocalId, out _)) __result = false;
			}
		}

		[HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Interact))]
		private static class Patch_Interact
		{
			private static bool Prefix(ItemDrop __instance, ref bool __result)
			{
				if (!IsReservedForOther(__instance.m_itemData, LocalId, out string owner)) return true;
				Player.m_localPlayer?.Message(MessageHud.MessageType.Center,
					string.IsNullOrEmpty(owner) ? "This loot belongs to another player" : $"This loot belongs to {owner}");
				__result = false;
				return false;
			}
		}

		/// <summary>Any item entering an inventory loses its reservation.</summary>
		[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData))]
		private static class Patch_AddItem
		{
			private static void Prefix(ItemDrop.ItemData item) => Clear(item);
		}

		[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int), typeof(bool))]
		private static class Patch_AddItemAt
		{
			private static void Prefix(ItemDrop.ItemData item) => Clear(item);
		}

		[HarmonyPatch(typeof(Inventory), nameof(Inventory.AddItem), typeof(ItemDrop.ItemData), typeof(Vector2i))]
		private static class Patch_AddItemPos
		{
			private static void Prefix(ItemDrop.ItemData item) => Clear(item);
		}
	}
}
