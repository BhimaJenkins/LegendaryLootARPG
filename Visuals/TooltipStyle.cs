using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using LegendaryLoot.Core;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace LegendaryLoot
{
	public enum TooltipFont
	{
		Designed, // Spectral SC + Faculty Glyphic, per Paul's mock-up (bundled free Google Fonts)
		Norse,    // Valheim's own runic display font for the whole tooltip
		Vanilla,  // leave the fonts alone
	}

	/// <summary>
	/// Diablo-style item tooltips: a worn bronze frame, a faint weathered wash in the item's tier color,
	/// and a carved-capitals font. Applies to every item tooltip (magic or not, so they all match);
	/// other tooltips (buttons, skills, settings) are left alone.
	/// </summary>
	public static class TooltipStyle
	{
		private const string WashName = "LLA_TooltipWash";
		private const string FrameName = "LLA_TooltipFrame";
		private const string GlowName = "LLA_TooltipGlow";

		// Which inventory slots are showing an item right now, and that item's tier (None = plain item).
		private static readonly Dictionary<UITooltip, Tier> ItemTooltips = new Dictionary<UITooltip, Tier>();

		public static void MarkItemTooltip(UITooltip tooltip, ItemDrop.ItemData item)
		{
			if (tooltip == null) return;
			if (ItemTooltips.Count > 512) ItemTooltips.Clear();
			ItemTooltips[tooltip] = item?.GetMagic()?.Tier ?? Tier.None;
		}

		[HarmonyPatch(typeof(UITooltip), nameof(UITooltip.UpdateTextElements))]
		private static class Patch_UpdateTextElements
		{
			private static void Postfix(UITooltip __instance)
			{
				GameObject root = UITooltip.m_tooltip;
				if (root == null) return;
				bool isItem = ItemTooltips.TryGetValue(__instance, out Tier tier);
				var memory = root.GetComponent<FontMemory>() ?? root.AddComponent<FontMemory>();

				ApplyFont(root, memory, isItem);
				// A Unique's title shines gold as a whole.
				Transform topic = Utils.FindChild(root.transform, "Topic");
				if (topic != null) GoldShine.SetWhole(topic.GetComponent<TMPro.TMP_Text>(), isItem && tier == Tier.Unique);
				GameObject box = FindBox(root);
				TooltipPadding.Apply(root, box, isItem);
				TooltipPaging.Apply(root, box, isItem);
				// Layer order, bottom to top: wash, glow, (text), frame.
				ApplyGlow(box, isItem && Plugin.TooltipFrame.Value && tier != Tier.None ? tier : Tier.None);
				ApplyWash(box, isItem && tier != Tier.None ? tier : Tier.None);
				ApplyFrame(box, isItem && Plugin.TooltipFrame.Value, tier == Tier.Unique);
			}
		}

		// Font ------------------------------------------------------------------------------------------

		/// <summary>Remembers each text's original font, so a reused tooltip can switch back for non-item tooltips.</summary>
		private class FontMemory : MonoBehaviour
		{
			public readonly Dictionary<TMP_Text, TMP_FontAsset> Original = new Dictionary<TMP_Text, TMP_FontAsset>();
		}

		private static void ApplyFont(GameObject root, FontMemory memory, bool isItem)
		{
			TooltipFont choice = Plugin.TooltipFontChoice.Value;
			bool designed = isItem && TooltipFonts.Active;
			TMP_FontAsset norse = isItem && choice == TooltipFont.Norse ? FindGameFont("Norse") : null;

			foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
			{
				if (!memory.Original.ContainsKey(text)) memory.Original[text] = text.font;
				TMP_FontAsset original = memory.Original[text];

				TMP_FontAsset want = original;
				if (designed)
				{
					TooltipFonts.SetFallback(original);
					want = TooltipFonts.Get(text.name == "Topic" ? FontRole.Title : FontRole.Label) ?? original;
				}
				else if (norse != null)
				{
					want = norse;
				}
				if (text.font != want) text.font = want;

				// Body text: switch fonts per section (description, labels, values, headings), all in small caps
				// except the flavor text. The title is small caps too.
				if (designed && text.name == "Text") text.text = TooltipFonts.Style(text.text);
				if (designed && text.name == "Topic") text.text = TooltipFonts.SmallCaps(text.text);
				if (isItem && (text.name == "Topic" || text.name == "Text")) GoldShine.Attach(text);
			}
		}

		private static readonly Dictionary<string, TMP_FontAsset> GameFonts = new Dictionary<string, TMP_FontAsset>();

		private static TMP_FontAsset FindGameFont(string contains)
		{
			if (GameFonts.TryGetValue(contains, out TMP_FontAsset cached) && cached != null) return cached;
			TMP_FontAsset best = null;
			foreach (TMP_FontAsset f in Resources.FindObjectsOfTypeAll<TMP_FontAsset>())
			{
				if (f == null || f.name.IndexOf(contains, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
				// Prefer the regular weight over bold.
				if (best == null || f.name.IndexOf("bold", System.StringComparison.OrdinalIgnoreCase) < 0) best = f;
			}
			GameFonts[contains] = best;
			return best;
		}

		// Frame and wash ------------------------------------------------------------------------------------

		/// <summary>
		/// The tooltip's root is only a small anchor at the cursor; the visible box is the background image that
		/// holds the text and resizes to fit it. Find it by walking up from the "Text" element to the first image.
		/// </summary>
		private static GameObject FindBox(GameObject root)
		{
			Transform text = Utils.FindChild(root.transform, "Text");
			for (Transform t = text != null ? text.parent : null; t != null; t = t.parent)
			{
				if (t.GetComponent<Image>() != null) return t.gameObject;
				if (t == root.transform) break;
			}
			// Fallback: the largest image in the tooltip.
			GameObject best = root;
			float bestArea = 0f;
			foreach (Image img in root.GetComponentsInChildren<Image>(true))
			{
				if (img.name.StartsWith("LLA_")) continue;
				Rect r = img.rectTransform.rect;
				if (r.width * r.height > bestArea) { bestArea = r.width * r.height; best = img.gameObject; }
			}
			return best;
		}

		private static void ApplyFrame(GameObject root, bool show, bool unique)
		{
			Image frame = GetOrCreateLayer(root, FrameName, show, last: true, inset: 0f);
			if (frame == null) return;
			frame.enabled = show;
			UniqueStyle.SetCrests(frame, show && unique);
			if (!show) return;
			// Uniques: the ornate gold frame (a little outside the box, so it frames rather than covers the text).
			frame.sprite = unique ? UniqueStyle.Frame() : Weathering.Frame();
			frame.type = Image.Type.Sliced;
			frame.color = Color.white;
			var rt = frame.rectTransform;
			float grow = unique ? 6f : 0f;
			rt.offsetMin = new Vector2(-grow, -grow);
			rt.offsetMax = new Vector2(grow, grow);
		}

		/// <summary>Inner glow in the tier color, just inside the frame (magic items only).</summary>
		private static void ApplyGlow(GameObject root, Tier tier)
		{
			bool show = tier != Tier.None;
			Image glow = GetOrCreateLayer(root, GlowName, show, last: false, inset: 0f);
			if (glow == null) return;
			glow.enabled = show;
			if (!show) return;
			glow.sprite = Weathering.InnerGlow();
			glow.type = Image.Type.Sliced;
			Color c = Tiers.GetColor(tier);
			c.a = 1f;
			glow.color = c;
		}

		private static void ApplyWash(GameObject root, Tier tier)
		{
			bool show = tier != Tier.None;
			Image wash = GetOrCreateLayer(root, WashName, show, last: false, inset: 2f);
			if (wash == null) return;
			wash.enabled = show;
			if (!show) return;
			wash.sprite = Weathering.TooltipWash();
			wash.type = Image.Type.Simple;
			// The rock texture is opaque at the bottom, so its brightness comes from this tint.
			Color c = Tiers.GetColor(tier) * 0.5f;
			c.a = 1f;
			wash.color = c;
		}

		/// <summary>
		/// A full-size image layer on the tooltip that layout groups ignore. The wash sits behind the text;
		/// the frame sits on top of everything, around the edge.
		/// </summary>
		private static Image GetOrCreateLayer(GameObject root, string name, bool create, bool last, float inset)
		{
			Transform t = root.transform.Find(name);
			if (t == null)
			{
				if (!create) return null;
				var go = new GameObject(name, typeof(RectTransform));
				go.transform.SetParent(root.transform, false);
				var rt = (RectTransform)go.transform;
				rt.anchorMin = Vector2.zero;
				rt.anchorMax = Vector2.one;
				rt.offsetMin = new Vector2(inset, inset);
				rt.offsetMax = new Vector2(-inset, -inset);
				go.AddComponent<LayoutElement>().ignoreLayout = true;
				var img = go.AddComponent<Image>();
				img.raycastTarget = false;
				t = go.transform;
			}
			if (last) t.SetAsLastSibling();
			else t.SetAsFirstSibling();
			return t.GetComponent<Image>();
		}
	}
}
