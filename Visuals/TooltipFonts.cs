using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace LegendaryLoot
{
	/// <summary>The jobs a font does in an item tooltip (Paul's tooltip mock-up).</summary>
	public enum FontRole
	{
		Title,        // item name                         Spectral SC ExtraBold
		Description,  // flavor text, skill descriptions    Spectral SC Italic
		Heading,      // "Two-handed", tier label           Spectral SC Bold
		Label,        // "Weight:", "Attack Speed:"         Spectral SC Medium (the text box's base font)
		Value,        // the colored numbers                Faculty Glyphic Regular
		Affix,        // affix lines                        Spectral SC Regular
		SkillName,    // Keen Combat skill name             Spectral SC SemiBold
	}

	/// <summary>
	/// Loads the tooltip fonts from the mod's fonts folder (free Google Fonts, SIL Open Font License) and
	/// registers them with TextMeshPro so tooltip text can switch fonts mid-line with &lt;font="..."&gt; tags.
	/// </summary>
	public static class TooltipFonts
	{
		private static readonly Dictionary<FontRole, string> Files = new Dictionary<FontRole, string>
		{
			{ FontRole.Title, "SpectralSC-ExtraBold.ttf" },
			{ FontRole.Description, "SpectralSC-Italic.ttf" },
			{ FontRole.Heading, "SpectralSC-Bold.ttf" },
			{ FontRole.Label, "SpectralSC-Medium.ttf" },
			{ FontRole.Value, "FacultyGlyphic-Regular.ttf" },
			{ FontRole.Affix, "SpectralSC-Regular.ttf" },
			{ FontRole.SkillName, "SpectralSC-SemiBold.ttf" },
		};

		private static readonly Dictionary<FontRole, TMP_FontAsset> Fonts = new Dictionary<FontRole, TMP_FontAsset>();
		private static bool _loaded;
		private static TMP_FontAsset _fallback;

		/// <summary>True when the designed font set is on and loaded.</summary>
		public static bool Active => Plugin.TooltipFontChoice.Value == TooltipFont.Designed && EnsureLoaded();

		public static TMP_FontAsset Get(FontRole role) => EnsureLoaded() && Fonts.TryGetValue(role, out TMP_FontAsset f) ? f : null;

		/// <summary>Wraps text in a font tag for the role, or returns it unchanged if the fonts are off.</summary>
		public static string Wrap(FontRole role, string text)
		{
			if (!Active || string.IsNullOrEmpty(text) || role == FontRole.Label) return text;
			string font = $"<font=\"{AssetName(role)}\">";
			switch (role)
			{
				// Flavor text stays in normal case, so it steps out of the small caps around it.
				case FontRole.Description: return $"</smallcaps>{font}{text}</font><smallcaps>";
				// Faculty Glyphic runs large next to Spectral SC.
				case FontRole.Value: return $"{font}<size={ValueSize}>{text}</size></font>";
				default: return font + text + "</font>";
			}
		}

		/// <summary>Faculty Glyphic is drawn 15% smaller so the numbers sit level with Spectral SC.</summary>
		private const string ValueSize = "85%";

		/// <summary>Everything in Spectral SC is shown in small caps, except the italic flavor text.</summary>
		public static string SmallCaps(string text)
		{
			if (!Active || string.IsNullOrEmpty(text) || text.StartsWith("<smallcaps>", System.StringComparison.Ordinal)) return text;
			return "<smallcaps>" + text + "</smallcaps>";
		}

		/// <summary>Adds the game's own font as a fallback, so letters our fonts lack (other languages) still show.</summary>
		public static void SetFallback(TMP_FontAsset gameFont)
		{
			if (gameFont == null || _fallback == gameFont) return;
			_fallback = gameFont;
			foreach (TMP_FontAsset f in Fonts.Values)
				if (f.fallbackFontAssetTable != null && !f.fallbackFontAssetTable.Contains(gameFont))
					f.fallbackFontAssetTable.Add(gameFont);
		}

		private static string AssetName(FontRole role) => "LLA-" + role;

		/// <summary>The designed set is usable as long as the base (Label) font loaded; any missing role falls back to it.</summary>
		private static bool EnsureLoaded()
		{
			if (_loaded) return Fonts.ContainsKey(FontRole.Label);
			_loaded = true;
			foreach (KeyValuePair<FontRole, string> pair in Files)
			{
				try
				{
					TMP_FontAsset asset = Load(pair.Key, pair.Value);
					if (asset != null) Fonts[pair.Key] = asset;
				}
				catch (System.Exception ex)
				{
					Plugin.Log.LogWarning($"Couldn't load font {pair.Value}: {ex.Message}");
				}
			}
			Plugin.Log.LogInfo($"Tooltip fonts loaded: {Fonts.Count} of {Files.Count}.");
			if (Fonts.TryGetValue(FontRole.Label, out TMP_FontAsset label))
				foreach (FontRole role in Files.Keys)
					if (!Fonts.ContainsKey(role)) Fonts[role] = label;
			return Fonts.ContainsKey(FontRole.Label);
		}

		private static TMP_FontAsset Load(FontRole role, string file)
		{
			string path = FindFile(file);
			if (path == null)
			{
				Plugin.Log.LogWarning($"Font not found: {file} (put it in the mod's fonts folder).");
				return null;
			}
			TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(path, 0, 64, 6, GlyphRenderMode.SDFAA, 1024, 1024);
			if (asset == null)
			{
				Plugin.Log.LogWarning($"Couldn't load {file}.");
				return null;
			}
			// Spectral SC Italic's kerning data pulls some letters over the following space ("Whilestamina").
			// Skip the font's kerning for it; the spacing built into each letter is fine on its own.
			if (role == FontRole.Description) asset.getFontFeatures = false;
			// TextMeshPro finds fonts and their materials by name. Fonts from one family (all the Spectral SC
			// weights) come out with identical material names, which makes registering the second one fail,
			// so every font and material gets its own name first.
			asset.name = AssetName(role);
			asset.hashCode = TMP_TextUtilities.GetHashCode(asset.name); // what <font="LLA-..."> looks up
			if (asset.material != null)
			{
				AddOutline(asset.material);
				asset.material.name = asset.name + " Material";
				asset.materialHashCode = TMP_TextUtilities.GetSimpleHashCode(asset.material.name);
			}
			if (asset.fallbackFontAssetTable == null) asset.fallbackFontAssetTable = new List<TMP_FontAsset>();
			MaterialReferenceManager.AddFontAsset(asset);
			return asset;
		}

		/// <summary>
		/// Thin black outline, like Valheim's own font, so text reads over any background. Kept thin and with no
		/// letter thickening, so Bold stays Bold (not Black) and the italic keeps its delicate strokes.
		/// </summary>
		private const float OutlineWidth = 0.1f;

		private static void AddOutline(Material m)
		{
			if (m.HasProperty("_OutlineWidth")) m.SetFloat("_OutlineWidth", OutlineWidth);
			if (m.HasProperty("_OutlineColor")) m.SetColor("_OutlineColor", new Color(0f, 0f, 0f, 1f));
			if (m.HasProperty("_FaceDilate")) m.SetFloat("_FaceDilate", 0f);
			m.EnableKeyword("OUTLINE_ON");
		}

		private static string FindFile(string filename)
		{
			string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
			string[] candidates =
			{
				Path.Combine(dir, "fonts", filename),
				Path.Combine(dir, filename),
				Path.Combine(dir, "LegendaryLootARPG", "fonts", filename),
				Path.Combine(dir, "LegendaryLootARPG", filename),
			};
			foreach (string c in candidates)
				if (File.Exists(c)) return c;
			return null;
		}

		// Styling the tooltip text ------------------------------------------------------------------------

		private const string OurMarker = "<link=\"lla_affixes\">";
		private const string StyledMarker = "<link=\"lla_styled\"></link>"; // invisible; stops styling twice
		private const string KeenCombatMarker = "<link=\"kc_skill\">";
		private static readonly Regex ColorValue = new Regex("(<color=[^>]+>)(.*?)(</color>)", RegexOptions.Compiled);
		private static readonly Regex Bold = new Regex("</?b>", RegexOptions.Compiled);
		private static readonly Regex Italic = new Regex("</?i>", RegexOptions.Compiled);

		/// <summary>
		/// Applies the designed fonts to a whole item tooltip:
		///  - vanilla part: first paragraph = description (italic); lines with "Label:" keep the base font with
		///    their colored values in Faculty Glyphic; other lines (e.g. "Two-handed") in bold.
		///  - our affix section is already tagged when it's built.
		///  - Keen Combat's section: skill name in semibold, description italic, stats like vanilla.
		/// </summary>
		public static string Style(string text)
		{
			if (string.IsNullOrEmpty(text) || text.StartsWith(StyledMarker, System.StringComparison.Ordinal)) return text;

			int ours = text.IndexOf(OurMarker, System.StringComparison.Ordinal);
			int kc = text.IndexOf(KeenCombatMarker, System.StringComparison.Ordinal);
			int vanillaEnd = ours >= 0 ? ours : kc >= 0 ? kc : text.Length;
			if (kc >= 0 && ours >= 0 && kc < ours) vanillaEnd = kc;

			var sb = new StringBuilder(StyledMarker + "<smallcaps>");
			sb.Append(StyleVanilla(text.Substring(0, vanillaEnd)));
			if (kc > vanillaEnd)
			{
				sb.Append(text, vanillaEnd, kc - vanillaEnd); // our section, untouched
				sb.Append(StyleKeenCombat(text.Substring(kc)));
			}
			else if (kc == vanillaEnd)
			{
				int end = ours > kc ? ours : text.Length;
				sb.Append(StyleKeenCombat(text.Substring(kc, end - kc)));
				if (end < text.Length) sb.Append(text, end, text.Length - end);
			}
			else
			{
				sb.Append(text, vanillaEnd, text.Length - vanillaEnd);
			}
			sb.Append("</smallcaps>");
			return sb.ToString();
		}

		private static string StyleVanilla(string part)
		{
			string[] lines = part.Split('\n');
			bool inDescription = lines.Length > 1 && !lines[0].Contains(":");
			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i];
				if (line.Trim().Length == 0)
				{
					inDescription = false;
					continue;
				}
				if (inDescription) lines[i] = Wrap(FontRole.Description, Italic.Replace(line, ""));
				else lines[i] = StyleStatLine(line);
			}
			return string.Join("\n", lines);
		}

		private static string StyleKeenCombat(string part)
		{
			string[] lines = part.Split('\n');
			bool nameDone = false;
			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i];
				if (line.Trim().Length == 0) continue;
				if (!nameDone)
				{
					nameDone = true;
					lines[i] = Wrap(FontRole.SkillName, Bold.Replace(line, ""));
				}
				else if (line.Contains("<i>")) lines[i] = Wrap(FontRole.Description, Italic.Replace(line, ""));
				else lines[i] = StyleStatLine(line);
			}
			return string.Join("\n", lines);
		}

		/// <summary>"Label: value" keeps the base font with values in the value font; other lines become headings.</summary>
		private static string StyleStatLine(string line)
		{
			if (line.Contains(":"))
				return ColorValue.Replace(line, m => m.Groups[1].Value + Wrap(FontRole.Value, m.Groups[2].Value) + m.Groups[3].Value);
			return Wrap(FontRole.Heading, Bold.Replace(line, ""));
		}
	}
}
