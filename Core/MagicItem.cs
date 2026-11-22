using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LegendaryLoot.Core
{
	/// <summary>One rolled affix: which affix, and the value it rolled.</summary>
	public struct RolledAffix
	{
		public string Id;
		public float Value;

		public RolledAffix(string id, float value)
		{
			Id = id;
			Value = value;
		}
	}

	/// <summary>
	/// The mod's data on one item. It lives only in ItemData.m_customData, which vanilla saves with the item
	/// everywhere it goes: inventories, chests, tombstones, item and armor stands, trades, dropped items.
	/// We never touch m_shared, so other mods' tooltips and stats stay correct.
	/// </summary>
	public class MagicItem
	{
		public const string Key = "LLA.magic";
		private const int FormatVersion = 1;

		public Tier Tier;
		public string UniqueId;      // null unless Tier == Unique
		public bool Crafted;         // crafted items sell for less
		public readonly List<RolledAffix> Affixes = new List<RolledAffix>();

		public bool IsUnique => Tier == Tier.Unique && !string.IsNullOrEmpty(UniqueId);

		// Format: version|tier|uniqueId|crafted|id=value;id=value
		public string Serialize()
		{
			var sb = new StringBuilder();
			sb.Append(FormatVersion).Append('|');
			sb.Append((int)Tier).Append('|');
			sb.Append(UniqueId ?? "").Append('|');
			sb.Append(Crafted ? '1' : '0').Append('|');
			for (int i = 0; i < Affixes.Count; i++)
			{
				if (i > 0) sb.Append(';');
				sb.Append(Affixes[i].Id).Append('=').Append(Affixes[i].Value.ToString("0.###", CultureInfo.InvariantCulture));
			}
			return sb.ToString();
		}

		public static MagicItem Deserialize(string text)
		{
			if (string.IsNullOrEmpty(text)) return null;
			string[] parts = text.Split('|');
			if (parts.Length < 5) return null;
			if (!int.TryParse(parts[1], out int tier)) return null;
			var item = new MagicItem
			{
				Tier = (Tier)tier,
				UniqueId = string.IsNullOrEmpty(parts[2]) ? null : parts[2],
				Crafted = parts[3] == "1",
			};
			if (parts[4].Length > 0)
			{
				foreach (string pair in parts[4].Split(';'))
				{
					int eq = pair.IndexOf('=');
					if (eq <= 0) continue;
					float.TryParse(pair.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float value);
					item.Affixes.Add(new RolledAffix(pair.Substring(0, eq), value));
				}
			}
			return item.Tier == Tier.None ? null : item;
		}
	}

	/// <summary>Reads and writes MagicItem data on vanilla items, with a small cache so tooltips and stat checks stay cheap.</summary>
	public static class MagicItemExtensions
	{
		private static readonly Dictionary<string, MagicItem> Cache = new Dictionary<string, MagicItem>();

		public static MagicItem GetMagic(this ItemDrop.ItemData item)
		{
			if (item?.m_customData == null) return null;
			if (!item.m_customData.TryGetValue(MagicItem.Key, out string text)) return null;
			if (Cache.TryGetValue(text, out MagicItem cached)) return cached;
			MagicItem parsed = MagicItem.Deserialize(text);
			if (Cache.Count > 4096) Cache.Clear();
			Cache[text] = parsed;
			return parsed;
		}

		public static bool IsMagic(this ItemDrop.ItemData item) => item.GetMagic() != null;

		public static void SetMagic(this ItemDrop.ItemData item, MagicItem magic)
		{
			if (item == null) return;
			if (item.m_customData == null) item.m_customData = new Dictionary<string, string>();
			if (magic == null) item.m_customData.Remove(MagicItem.Key);
			else item.m_customData[MagicItem.Key] = magic.Serialize();
		}

		/// <summary>Sum of every rolled value of one affix on this item (an affix only rolls once per item, but this stays safe).</summary>
		public static float GetAffix(this ItemDrop.ItemData item, string affixId)
		{
			MagicItem magic = item.GetMagic();
			if (magic == null) return 0f;
			float total = 0f;
			foreach (RolledAffix a in magic.Affixes)
				if (a.Id == affixId) total += a.Value;
			return total;
		}
	}
}
