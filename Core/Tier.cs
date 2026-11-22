using UnityEngine;

namespace LegendaryLoot.Core
{
	/// <summary>Rarity tiers, lowest to highest. The numbers are saved on items, so never reorder them.</summary>
	public enum Tier
	{
		None = 0,
		Uncommon = 1,  // Green
		Magic = 2,     // Blue
		Rare = 3,      // Yellow
		Epic = 4,      // Purple
		Legendary = 5, // Orange
		Unique = 6,    // Gold
	}

	public static class Tiers
	{
		/// <summary>Number of rolled affixes per tier. Uniques get their named affix plus 4 rolled at Legendary values.</summary>
		public static int AffixCount(Tier tier)
		{
			switch (tier)
			{
				case Tier.Uncommon: return 1;
				case Tier.Magic: return 2;
				case Tier.Rare: return 3;
				case Tier.Epic: return 4;
				case Tier.Legendary: return 4;
				case Tier.Unique: return 4;
				default: return 0;
			}
		}

		/// <summary>The value column used when rolling affixes. Uniques roll at Legendary values.</summary>
		public static Tier ValueTier(Tier tier) => tier == Tier.Unique ? Tier.Legendary : tier;

		public static Color GetColor(Tier tier)
		{
			switch (tier)
			{
				case Tier.Uncommon: return new Color32(0x3D, 0xD6, 0x4B, 0xFF);
				case Tier.Magic: return new Color32(0x4A, 0x8C, 0xFF, 0xFF);
				case Tier.Rare: return new Color32(0xFF, 0xFF, 0x5C, 0xFF);      // lemon yellow, clearly apart from Unique gold
				case Tier.Epic: return new Color32(0xB0, 0x5C, 0xFF, 0xFF);
				case Tier.Legendary: return new Color32(0xFF, 0x8C, 0x1A, 0xFF);
				case Tier.Unique: return new Color32(0xD4, 0xAF, 0x37, 0xFF);
				default: return Color.white;
			}
		}

		public static string GetHex(Tier tier) => "#" + ColorUtility.ToHtmlStringRGB(GetColor(tier));

		public static string GetLabel(Tier tier)
		{
			switch (tier)
			{
				case Tier.Uncommon: return "Uncommon";
				case Tier.Magic: return "Magic";
				case Tier.Rare: return "Rare";
				case Tier.Epic: return "Epic";
				case Tier.Legendary: return "Legendary";
				case Tier.Unique: return "Unique";
				default: return "";
			}
		}

		public static string Colorize(string text, Tier tier) => $"<color={GetHex(tier)}>{text}</color>";

		/// <summary>Moves a tier up by the given number of steps, never past Legendary (Unique is only reached by the Unique roll).</summary>
		public static Tier Bump(Tier tier, int steps = 1)
		{
			int t = Mathf.Clamp((int)tier + steps, (int)Tier.Uncommon, (int)Tier.Legendary);
			return (Tier)t;
		}
	}
}
