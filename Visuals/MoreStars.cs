using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryLoot.Visuals
{
	/// <summary>
	/// Vanilla's enemy health bar only has art for 1 and 2 stars, and shows none at all above that. Creatures
	/// with 3 or more stars (hard bounties now, enemy scaling later) get the 2-star row plus one more star per level.
	/// </summary>
	public static class MoreStars
	{
		private const string ExtraName = "LLA_Star";

		[HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.UpdateHuds))]
		private static class Patch_UpdateHuds
		{
			private static void Postfix(EnemyHud __instance)
			{
				foreach (KeyValuePair<Character, EnemyHud.HudData> kv in __instance.m_huds)
				{
					EnemyHud.HudData hud = kv.Value;
					if (hud?.m_character == null || hud.m_level3 == null) continue;
					int stars = hud.m_character.GetLevel() - 1;
					if (stars < 3)
					{
						SetExtra(hud.m_level3, 0);
						continue;
					}
					hud.m_level3.gameObject.SetActive(true);
					SetExtra(hud.m_level3, stars - 2);
				}
			}
		}

		/// <summary>Shows 'count' extra stars after the two in the row, copying the row's own star picture and spacing.</summary>
		private static void SetExtra(RectTransform row, int count)
		{
			var vanilla = new List<RectTransform>();
			var extra = new List<RectTransform>();
			foreach (Transform t in row)
			{
				if (!(t is RectTransform rt) || rt.GetComponent<Image>() == null) continue;
				(t.name.StartsWith(ExtraName) ? extra : vanilla).Add(rt);
			}
			if (count <= 0 && extra.Count == 0) return;
			if (vanilla.Count == 0) return;
			vanilla.Sort((a, b) => a.anchoredPosition.x.CompareTo(b.anchoredPosition.x));
			RectTransform last = vanilla[vanilla.Count - 1];
			Vector2 step = vanilla.Count > 1 ? last.anchoredPosition - vanilla[vanilla.Count - 2].anchoredPosition : new Vector2(last.rect.width, 0f);
			while (extra.Count < count)
			{
				var star = (RectTransform)Object.Instantiate(last.gameObject, row, false).transform;
				star.name = ExtraName + extra.Count;
				extra.Add(star);
			}
			for (int i = 0; i < extra.Count; i++)
			{
				extra[i].anchoredPosition = last.anchoredPosition + step * (i + 1);
				extra[i].gameObject.SetActive(i < count);
			}
		}
	}
}
