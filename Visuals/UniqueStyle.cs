using UnityEngine;
using UnityEngine.UI;

namespace LegendaryLoot
{
	/// <summary>
	/// The premium look for Unique items, all drawn in code:
	///  - Slot: dark burnished gold (brown middle, pale-gold edge glow lit from the top-left, faint brushed streaks),
	///    with small sparkles that twinkle in turn. The 20-second glint (IconShine) stays on top.
	///  - Tooltip: an ornate double gold frame with diamond corner pieces and a crest at the top and bottom.
	/// </summary>
	public static class UniqueStyle
	{
		private static Sprite _foil, _frame, _sparkle, _crest;

		private static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out Color c); return c; }

		private static Texture2D NewTex(int w, int h) =>
			new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };

		private static Color Ramp(float t, float[] at, Color[] cols)
		{
			if (t <= at[0]) return cols[0];
			for (int i = 1; i < at.Length; i++)
				if (t <= at[i]) return Color.Lerp(cols[i - 1], cols[i], (t - at[i - 1]) / (at[i] - at[i - 1]));
			return cols[cols.Length - 1];
		}

		// ---------- slot ----------

		/// <summary>
		/// Dark burnished gold: a deep brown middle, warming to bronze toward the edges, with a soft pale-gold edge glow
		/// lit from the top-left (brightest there, darkest bottom-right) and faint diagonal brushed streaks.
		/// </summary>
		public static Sprite SlotFoil()
		{
			if (_foil != null) return _foil;
			const int S = 64;
			Texture2D tex = NewTex(S, S);
			Color center = Hex("#2E1C0D"), mid = Hex("#5E3E1D");
			Color topLeft = Hex("#E2D2A6"), bottomRight = Hex("#3A2508");
			for (int y = 0; y < S; y++)
			for (int x = 0; x < S; x++)
			{
				float u = (x + 0.5f) / S, v = (y + 0.5f) / S;   // v = 0 at the bottom
				// Rounded-square distance from the middle (0 centre, 1 edge).
				float ax = Mathf.Abs(u * 2f - 1f), ay = Mathf.Abs(v * 2f - 1f);
				float r = Mathf.Pow(Mathf.Pow(ax, 4f) + Mathf.Pow(ay, 4f), 0.25f);
				Color c = Color.Lerp(center, mid, Mathf.SmoothStep(0f, 1f, r));
				// Edge glow, coloured by which corner it's nearest (light from the top-left).
				// A narrow band at the edges, strongest in the corners; pale gold at the top-left fading to dark bronze
				// at the bottom-right.
				float light = Mathf.Clamp01((1f - u + v) * 0.5f);
				Color edge = Color.Lerp(bottomRight, topLeft, light);
				float glow = Mathf.Clamp01((r - 0.78f) / 0.4f);
				glow = glow * glow * (3f - 2f * glow);
				c = Color.Lerp(c, edge, glow);
				// Faint diagonal brushed streaks.
				float streak = Mathf.PerlinNoise((x + y) * 0.45f, (x - y) * 0.04f + 7.3f);
				c *= Mathf.Lerp(0.93f, 1.05f, streak);
				c.a = 0.97f;
				tex.SetPixel(x, y, c);
			}
			tex.Apply();
			_foil = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
			return _foil;
		}

		/// <summary>A four-point star with a soft glow.</summary>
		public static Sprite Sparkle()
		{
			if (_sparkle != null) return _sparkle;
			const int S = 32;
			Texture2D tex = NewTex(S, S);
			float r = S / 2f;
			for (int y = 0; y < S; y++)
			for (int x = 0; x < S; x++)
			{
				float dx = Mathf.Abs(x + 0.5f - r) / r, dy = Mathf.Abs(y + 0.5f - r) / r;
				float star = Mathf.Clamp01(1f - (Mathf.Sqrt(dx) + Mathf.Sqrt(dy)) * 0.95f);   // pinched 4-point shape
				float glow = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy)) * 0.35f;
				float a = Mathf.Clamp01(star * 2.2f + glow * glow);
				tex.SetPixel(x, y, new Color(1f, 0.98f, 0.88f, a));
			}
			tex.Apply();
			_sparkle = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f));
			return _sparkle;
		}

		/// <summary>Turns the twinkling sparkles on or off in a Unique slot's background.</summary>
		public static void SetSparkles(Image background, bool on)
		{
			if (background == null) return;
			Transform existing = background.transform.Find("LLA_Sparkles");
			if (!on)
			{
				if (existing != null && existing.gameObject.activeSelf) existing.gameObject.SetActive(false);
				return;
			}
			if (existing == null)
			{
				if (background.GetComponent<RectMask2D>() == null) background.gameObject.AddComponent<RectMask2D>();
				var root = new GameObject("LLA_Sparkles", typeof(RectTransform));
				root.transform.SetParent(background.transform, false);
				var rt = (RectTransform)root.transform;
				rt.anchorMin = Vector2.zero;
				rt.anchorMax = Vector2.one;
				rt.offsetMin = rt.offsetMax = Vector2.zero;
				root.AddComponent<UniqueSparkles>();
				existing = root.transform;
			}
			if (!existing.gameObject.activeSelf) existing.gameObject.SetActive(true);
		}

		// ---------- tooltip ----------

		public const int FrameBorder = 30;

		/// <summary>The ornate frame (9-sliced): double gold border, dark keylines, diamond corners with a gem.</summary>
		public static Sprite Frame()
		{
			if (_frame != null) return _frame;
			const int S = 96, B = FrameBorder;
			Texture2D tex = NewTex(S, S);
			Color outline = Hex("#1E1204"), goldHi = Hex("#FFEBA6"), goldMid = Hex("#D9AE45"), goldLo = Hex("#7A5212"), thin = Hex("#E8C463");
			Color gemHi = Hex("#FFFBE6"), gemLo = Hex("#F2B630");
			for (int y = 0; y < S; y++)
			for (int x = 0; x < S; x++)
			{
				int e = Mathf.Min(Mathf.Min(x, y), Mathf.Min(S - 1 - x, S - 1 - y)); // distance to the nearest edge
				Color c = Color.clear;
				if (e <= 1) c = outline;
				else if (e <= 6)
				{
					// Polished band: bright on its outer side, darker inside, lit from the top-left.
					float k = (e - 2) / 4f;
					c = Color.Lerp(goldHi, goldLo, k);
					bool lit = x < S / 2 ? (y > S / 2 || x < y) : (y > S / 2 && S - 1 - x > S - 1 - y);
					c = Color.Lerp(c, lit ? goldHi : goldLo, 0.2f);
				}
				else if (e == 7) c = outline;
				else if (e == 9 || e == 10) c = thin;
				else if (e == 11) c = new Color(outline.r, outline.g, outline.b, 0.6f);

				// Corner pieces: a diamond with an outline and a bright gem, over the border.
				int cx = Mathf.Min(x, S - 1 - x), cy = Mathf.Min(y, S - 1 - y);
				int d = Mathf.Abs(cx - 11) + Mathf.Abs(cy - 11);
				if (d <= 11)
				{
					if (d >= 10) c = outline;
					else
					{
						float k = d / 10f;
						c = Color.Lerp(goldHi, goldMid, k);
						if (cx + cy > 22) c = Color.Lerp(c, goldLo, 0.35f);  // inner half in shade
						if (d <= 4) c = Color.Lerp(gemHi, gemLo, d / 4f);    // the gem
						if (d == 5) c = outline;
					}
				}
				// Little spurs running along each edge from the corner piece.
				else if ((cx >= 22 && cx <= B + 2 && (cy == 3 || cy == 4)) || (cy >= 22 && cy <= B + 2 && (cx == 3 || cx == 4)))
					c = goldHi;
				tex.SetPixel(x, y, c);
			}
			tex.Apply();
			_frame = Sprite.Create(tex, new Rect(0, 0, S, S), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect,
				new Vector4(B, B, B, B));
			return _frame;
		}

		/// <summary>The crest at the top and bottom middle of the frame: a wide diamond flanked by two small ones.</summary>
		public static Sprite Crest()
		{
			if (_crest != null) return _crest;
			const int W = 64, H = 20;
			Texture2D tex = NewTex(W, H);
			Color outline = Hex("#1E1204"), goldHi = Hex("#FFEBA6"), goldMid = Hex("#D9AE45"), gem = Hex("#FFF4C8");
			for (int y = 0; y < H; y++)
			for (int x = 0; x < W; x++)
			{
				Color c = Color.clear;
				foreach (var (ox, rx, ry) in new[] { (32, 14f, 9f), (12, 6f, 5f), (52, 6f, 5f) })
				{
					float d = Mathf.Abs(x + 0.5f - ox) / rx + Mathf.Abs(y + 0.5f - H / 2f) / ry;
					if (d > 1f) continue;
					c = d > 0.82f ? outline : Color.Lerp(goldHi, goldMid, d);
					if (ox == 32 && d < 0.35f) c = gem;
					break;
				}
				tex.SetPixel(x, y, c);
			}
			tex.Apply();
			_crest = Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f));
			return _crest;
		}

		/// <summary>Crests at the top and bottom middle of a frame layer; on for Uniques only.</summary>
		public static void SetCrests(Image frame, bool on)
		{
			if (frame == null) return;
			foreach (string name in new[] { "LLA_CrestTop", "LLA_CrestBottom" })
			{
				Transform t = frame.transform.Find(name);
				if (!on)
				{
					if (t != null && t.gameObject.activeSelf) t.gameObject.SetActive(false);
					continue;
				}
				if (t == null)
				{
					var go = new GameObject(name, typeof(RectTransform));
					go.transform.SetParent(frame.transform, false);
					var rt = (RectTransform)go.transform;
					bool top = name.EndsWith("Top");
					rt.anchorMin = rt.anchorMax = new Vector2(0.5f, top ? 1f : 0f);
					rt.pivot = new Vector2(0.5f, 0.5f);
					rt.sizeDelta = new Vector2(64f, 20f);
					rt.anchoredPosition = new Vector2(0f, top ? -3f : 3f);
					var img = go.AddComponent<Image>();
					img.sprite = Crest();
					img.raycastTarget = false;
					t = go.transform;
				}
				if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
			}
		}
	}

	/// <summary>Three sparkles that twinkle in turn around a Unique slot.</summary>
	public class UniqueSparkles : MonoBehaviour
	{
		private static readonly Vector2[] Spots = { new Vector2(0.2f, 0.78f), new Vector2(0.82f, 0.3f), new Vector2(0.7f, 0.86f) };
		private const float Cycle = 3.6f;   // each sparkle twinkles once per cycle...
		private const float Twinkle = 0.7f; // ...for this long
		private Image[] _stars;
		private RectTransform _rt;

		private void Awake()
		{
			_rt = (RectTransform)transform;
			_stars = new Image[Spots.Length];
			for (int i = 0; i < Spots.Length; i++)
			{
				var go = new GameObject("Star" + i, typeof(RectTransform));
				go.transform.SetParent(transform, false);
				var rt = (RectTransform)go.transform;
				rt.anchorMin = rt.anchorMax = Spots[i];
				rt.pivot = new Vector2(0.5f, 0.5f);
				var img = go.AddComponent<Image>();
				img.sprite = UniqueStyle.Sparkle();
				img.raycastTarget = false;
				_stars[i] = img;
			}
		}

		private void Update()
		{
			float size = Mathf.Min(_rt.rect.width, _rt.rect.height) * 0.42f;
			for (int i = 0; i < _stars.Length; i++)
			{
				float phase = ((Time.unscaledTime + i * Cycle / _stars.Length) % Cycle) / Twinkle;
				Image s = _stars[i];
				if (phase > 1f) { if (s.enabled) s.enabled = false; continue; }
				s.enabled = true;
				float k = Mathf.Sin(phase * Mathf.PI);
				s.rectTransform.sizeDelta = new Vector2(size, size) * k;
				s.rectTransform.localRotation = Quaternion.Euler(0f, 0f, phase * 45f);
				s.color = new Color(1f, 1f, 1f, k);
			}
		}
	}
}
