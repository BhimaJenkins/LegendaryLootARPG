using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using LegendaryLoot.Core;
using LegendaryLoot.Data;
using LegendaryLoot.Loot;

namespace LegendaryLoot
{
	/// <summary>
	/// Tooltips and names.
	///  - Players only see each affix's short tooltip line (Sheet column B), never internal names.
	///  - Our lines go right after vanilla's stats and before Keen Combat's skill section, which starts at its
	///    &lt;link="kc_skill"&gt; marker. If Keen Combat isn't installed, they go at the end.
	///  - Names are drawn in the tier color, and Uniques use their own name (e.g. Eaglehorn).
	/// </summary>
	public static class TooltipPatches
	{
		private const string OurMarker = "<link=\"lla_affixes\"></link>";
		private const string KeenCombatMarker = "<link=\"kc_skill\">";
		private static readonly Regex XToken = new Regex(@"(?<![A-Za-z])X(?![A-Za-z])", RegexOptions.Compiled);

		/// <summary>The name players see: the Unique's name, or the item's own name, in the tier color.</summary>
		public static string GetDisplayName(ItemDrop.ItemData item, string vanillaName)
		{
			MagicItem magic = item.GetMagic();
			if (magic == null) return vanillaName;
			string name = vanillaName;
			if (magic.IsUnique && DataStore.UniqueById.TryGetValue(magic.UniqueId, out UniqueDef u) && !string.IsNullOrEmpty(u.Name))
				name = u.Name;
			// Uniques shine gold in the tooltip (the link tag is invisible everywhere else).
			if (magic.IsUnique) name = GoldShine.Wrap(name);
			return Tiers.Colorize(name, magic.Tier);
		}

		public static string BuildAffixBlock(ItemDrop.ItemData item)
		{
			MagicItem magic = item.GetMagic();
			if (magic == null) return null;
			string hex = Tiers.GetHex(magic.Tier);
			var sb = new StringBuilder();
			sb.Append(OurMarker).Append('\n');
			string label = TooltipFonts.Active ? Tiers.GetLabel(magic.Tier) : "<b>" + Tiers.GetLabel(magic.Tier) + "</b>";
			if (magic.IsUnique) label = GoldShine.Wrap(label);
			sb.Append($"<color={hex}>{TooltipFonts.Wrap(FontRole.Heading, label)}</color>");
			if (magic.Crafted) sb.Append(" <color=#9a9a9a>(crafted)</color>");
			sb.Append('\n');

			if (magic.IsUnique && DataStore.UniqueById.TryGetValue(magic.UniqueId, out UniqueDef u))
				sb.Append($"<color={hex}>{TooltipFonts.Wrap(FontRole.Description, u.Description)}</color>\n");

			foreach (RolledAffix a in magic.Affixes)
				sb.Append($"<color={hex}>{TooltipFonts.Wrap(FontRole.Affix, FormatAffix(a))}</color>\n");
			return sb.ToString();
		}

		public static string FormatAffix(RolledAffix a)
		{
			if (!DataStore.AffixById.TryGetValue(a.Id, out AffixDef def) || string.IsNullOrEmpty(def.Tooltip))
				return a.Id; // data file is missing this affix: show something rather than nothing
			if (def.Toggle) return def.Tooltip;
			string value = a.Value.ToString(a.Value % 1f == 0f ? "0" : "0.#", CultureInfo.InvariantCulture);
			return XToken.Replace(def.Tooltip, value);
		}

		[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
			typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool))]
		[HarmonyPriority(Priority.Low)]
		private static class Patch_GetTooltip
		{
			private static void Postfix(ItemDrop.ItemData item, ref string __result)
			{
				if (item == null || __result == null || __result.Contains(OurMarker)) return;
				string block = BuildAffixBlock(item);
				if (block == null) return;

				int kc = __result.IndexOf(KeenCombatMarker, System.StringComparison.Ordinal);
				if (kc >= 0) __result = __result.Substring(0, kc) + block + __result.Substring(kc);
				else __result = __result.TrimEnd() + "\n\n" + block;
			}
		}

		/// <summary>Inventory tooltip header: tier-colored name.</summary>
		[HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.CreateItemTooltip))]
		private static class Patch_CreateItemTooltip
		{
			// Mark first: vanilla's Set() inside CreateItemTooltip redraws the tooltip, and the style needs to know it's an item.
			private static void Prefix(ItemDrop.ItemData item, UITooltip tooltip) => TooltipStyle.MarkItemTooltip(tooltip, item);

			private static void Postfix(ItemDrop.ItemData item, UITooltip tooltip)
			{
				if (item == null || tooltip == null || !item.IsMagic()) return;
				string name = GetDisplayName(item, Localization.instance.Localize(item.m_shared.m_name));
				tooltip.Set(name, tooltip.m_text, tooltip.m_anchor, tooltip.m_fixedPosition);
			}
		}

		/// <summary>Dropped item hover text: tier-colored name, plus who it's reserved for.</summary>
		[HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.GetHoverText))]
		private static class Patch_GetHoverText
		{
			private static void Postfix(ItemDrop __instance, ref string __result)
			{
				ItemDrop.ItemData item = __instance.m_itemData;
				if (item == null || string.IsNullOrEmpty(__result)) return;
				if (item.IsMagic())
				{
					string vanilla = Localization.instance.Localize(item.m_shared.m_name);
					int i = __result.IndexOf(vanilla, System.StringComparison.Ordinal);
					if (i >= 0) __result = __result.Substring(0, i) + GetDisplayName(item, vanilla) + __result.Substring(i + vanilla.Length);
				}
				long me = Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerID() : 0;
				if (PersonalLoot.IsReservedForOther(item, me, out string owner))
					__result += $"\n<color=#9a9a9a>Reserved for {(string.IsNullOrEmpty(owner) ? "another player" : owner)}</color>";
			}
		}

		[HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.GetHoverName))]
		private static class Patch_GetHoverName
		{
			private static void Postfix(ItemDrop __instance, ref string __result)
			{
				if (__instance.m_itemData != null && __instance.m_itemData.IsMagic())
					__result = GetDisplayName(__instance.m_itemData, Localization.instance.Localize(__instance.m_itemData.m_shared.m_name));
			}
		}
	}
}
