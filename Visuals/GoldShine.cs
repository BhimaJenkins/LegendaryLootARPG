using TMPro;
using UnityEngine;

namespace LegendaryLoot
{
	/// <summary>
	/// "Shiny card" gold for Unique items: any text wrapped in &lt;link="lla_gold"&gt; is drawn in a gold gradient
	/// (bright at the top, deep amber at the bottom) with a soft highlight that sweeps across it every few
	/// seconds. Used on a Unique's name and its "Unique" label in the tooltip.
	/// Works by recoloring the letters' vertices just before TextMeshPro draws them, so it overrides the
	/// tier color tag and follows whatever font is in use.
	/// </summary>
	public class GoldShine : MonoBehaviour
	{
		public const string LinkId = "lla_gold";
		/// <summary>A color no other text uses: letters drawn in it are the ones to gild (sturdier than link tags).</summary>
		private const string MarkerHex = "#FFD701";
		private static readonly Color32 Marker = new Color32(255, 215, 1, 255);
		public static string Wrap(string text) => $"<color={MarkerHex}><link=\"{LinkId}\">{text}</link></color>";

		private static readonly Color Top = new Color(1f, 0.98f, 0.86f);       // almost white gold
		private static readonly Color Mid = new Color(1f, 0.76f, 0.16f);       // rich gold
		private static readonly Color Bottom = new Color(0.52f, 0.26f, 0.03f); // dark bronze
		private const float Period = 6f;        // seconds between sweeps
		private const float SweepTime = 0.9f;   // how long one sweep takes to cross
		private const float BandWidth = 0.30f;  // width of the highlight, as a share of the text's width
		private const float Highlight = 1f;     // how close to white the highlight gets

		private TMP_Text _text;
		private bool _logged;
		/// <summary>Gild every letter (used on a Unique's title, which is just its name).</summary>
		public bool Whole;

		public static void SetWhole(TMP_Text text, bool whole)
		{
			if (text == null) return;
			GoldShine shine = text.GetComponent<GoldShine>();
			if (shine == null)
			{
				if (!whole) return;
				shine = text.gameObject.AddComponent<GoldShine>();
			}
			shine.Whole = whole;
		}

		/// <summary>Adds the effect to a text once (cheap to call every frame).</summary>
		public static void Attach(TMP_Text text)
		{
			if (text != null && text.GetComponent<GoldShine>() == null) text.gameObject.AddComponent<GoldShine>();
		}

		private void Awake()
		{
			_text = GetComponent<TMP_Text>();
			_text.OnPreRenderText += Recolor;
		}

		private void OnDestroy()
		{
			if (_text != null) _text.OnPreRenderText -= Recolor;
		}

		private void LateUpdate()
		{
			// Redraw every frame while there's gold to animate.
			string t = _text.text;
			if (!Whole && (t == null || t.IndexOf(MarkerHex, System.StringComparison.OrdinalIgnoreCase) < 0)) return;
			_text.havePropertiesChanged = true;
			_text.SetVerticesDirty();
		}

		private void Recolor(TMP_TextInfo info)
		{
			if (info == null) return;
			float now = Time.unscaledTime;
			float phase = (now % Period) / SweepTime;           // 0..1 while sweeping, >1 while resting
			bool sweeping = phase <= 1f;
			// The band starts left of the text and ends right of it.
			float bandCenter = Mathf.Lerp(-BandWidth, 1f + BandWidth, phase);

			if (Whole)
			{
				Gild(info, 0, info.characterCount - 1, now, sweeping, bandCenter);
				return;
			}
			// Find runs of letters drawn in the marker color and gild each run.
			int runStart = -1;
			for (int c = 0; c <= info.characterCount; c++)
			{
				bool marked = false;
				if (c < info.characterCount)
				{
					TMP_CharacterInfo ch = info.characterInfo[c];
					if (ch.isVisible)
					{
						Color32[] colors = info.meshInfo[ch.materialReferenceIndex].colors32;
						if (colors != null && ch.vertexIndex < colors.Length)
						{
							Color32 k = colors[ch.vertexIndex];
							marked = k.r == Marker.r && k.g == Marker.g && k.b == Marker.b;
						}
					}
					else if (runStart >= 0) continue; // spaces inside a run don't break it
				}
				if (marked && runStart < 0) runStart = c;
				else if (!marked && runStart >= 0)
				{
					Gild(info, runStart, c - 1, now, sweeping, bandCenter);
					runStart = -1;
				}
			}
			if (!_logged && Plugin.DebugLogging != null && Plugin.DebugLogging.Value)
			{
				_logged = true;
				Plugin.Debug($"GoldShine active on {name}");
			}
		}

		private static void Gild(TMP_TextInfo info, int first, int last, float now, bool sweeping, float bandCenter)
		{
			last = Mathf.Min(last, info.characterCount - 1);
			if (last < first) return;
			{

				// Horizontal extent of the gold text (across lines, so a wrapped name still sweeps as one).
				float minX = float.MaxValue, maxX = float.MinValue;
				for (int c = first; c <= last; c++)
				{
					TMP_CharacterInfo ch = info.characterInfo[c];
					if (!ch.isVisible) continue;
					minX = Mathf.Min(minX, ch.bottomLeft.x);
					maxX = Mathf.Max(maxX, ch.topRight.x);
				}
				float width = Mathf.Max(1f, maxX - minX);

				for (int c = first; c <= last; c++)
				{
					TMP_CharacterInfo ch = info.characterInfo[c];
					if (!ch.isVisible) continue;
					Color32[] colors = info.meshInfo[ch.materialReferenceIndex].colors32;
					Vector3[] verts = info.meshInfo[ch.materialReferenceIndex].vertices;
					int vi = ch.vertexIndex;
					if (colors == null || verts == null || vi + 3 >= colors.Length) continue;
					// Gradient by line: top of the letters to the baseline/descender.
					float yTop = ch.ascender, yBottom = ch.descender;
					for (int k = 0; k < 4; k++)
					{
						Vector3 v = verts[vi + k];
						float tY = Mathf.InverseLerp(yBottom, yTop, v.y);
						Color col = tY > 0.5f ? Color.Lerp(Mid, Top, (tY - 0.5f) * 2f) : Color.Lerp(Bottom, Mid, tY * 2f);
						// Gentle ripple of light that always drifts across the gold, so it never looks flat.
						float wave = Mathf.Sin(((v.x - minX) / width) * 9f - now * 3.5f) * 0.5f + 0.5f;
						col = Color.Lerp(col, Top, wave * 0.35f);
						if (sweeping)
						{
							// Slanted band, like light catching a foil card.
							float x = (v.x - minX) / width + (tY - 0.5f) * 0.12f;
							float d = Mathf.Abs(x - bandCenter) / BandWidth;
							float glint = Mathf.Clamp01(1f - d);
							glint = glint * glint * (3f - 2f * glint);
							col = Color.Lerp(col, Color.white, glint * Highlight);
						}
						byte a = colors[vi + k].a;
						Color32 c32 = col;
						c32.a = a;
						colors[vi + k] = c32;
					}
				}
			}
		}
	}
}
