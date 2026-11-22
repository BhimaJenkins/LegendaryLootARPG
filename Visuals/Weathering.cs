using System.IO;
using System.Reflection;
using UnityEngine;

namespace LegendaryLoot
{
	/// <summary>
	/// Procedural textures for the Diablo-style look: weathered gradients and a worn bronze frame.
	/// Generated once at runtime, so the mod needs no image files. All textures are white/grey so they can be
	/// tinted with any tier color.
	/// </summary>
	public static class Weathering
	{
		private static Sprite _slotGradient, _tooltipWash, _frame, _glow;

		/// <summary>
		/// The inner glow just inside the frame: bright at the edge, fading inward over a short distance,
		/// with a ragged, distressed falloff. White, so it takes the tier color. 9-sliced, center empty.
		/// </summary>
		public static Sprite InnerGlow()
		{
			if (_glow != null) return _glow;
			const int size = 128, border = 44;
			const float edge = 6f;    // where the glow starts (inside the frame's bronze band)
			const float reach = 30f;  // how far in it fades
			var tex = NewTexture(size, size);
			for (int y = 0; y < size; y++)
			for (int x = 0; x < size; x++)
			{
				float d = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y));
				float a = 0f;
				if (d >= edge)
				{
					float t = (d - edge) / reach;
					// Ragged edge: noise pushes the falloff in and out, plus fine grain.
					float ragged = Mathf.Lerp(0.6f, 1.25f, Noise(x / 7f, y / 7f, 21.3f));
					float grain = Mathf.Lerp(0.7f, 1f, Noise(x / 2f, y / 2f, 8.8f));
					a = Mathf.Clamp01(Mathf.Exp(-t * 3.2f / ragged) * grain);
					a *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - edge) / 2f)); // soft start, no hard line
				}
				tex.SetPixel(x, y, new Color(1f, 1f, 1f, a * 0.9f));
			}
			tex.Apply();
			_glow = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
				SpriteMeshType.FullRect, new Vector4(border, border, border, border));
			return _glow;
		}

		/// <summary>Layered noise, 0..1.</summary>
		private static float Noise(float x, float y, float seed)
		{
			float v = 0f, amp = 0.5f, freq = 1f;
			for (int o = 0; o < 4; o++)
			{
				v += amp * Mathf.PerlinNoise(x * freq + seed, y * freq + seed * 1.7f);
				amp *= 0.5f;
				freq *= 2.1f;
			}
			return Mathf.Clamp01(v / 0.9375f);
		}

		/// <summary>Thin pale streaks, like scratches in old paint.</summary>
		private static float Scratches(float x, float y)
		{
			float s = Mathf.PerlinNoise(x * 0.6f + 31f, y * 18f + 7f);
			return s > 0.82f ? (s - 0.82f) * 3f : 0f;
		}

		/// <summary>Inventory and hotbar slots: strong at the bottom, fading up, with blotchy wear.</summary>
		public static Sprite SlotGradient()
		{
			if (_slotGradient != null) return _slotGradient;
			const int w = 64, h = 64;
			var tex = NewTexture(w, h);
			for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				float t = y / (float)(h - 1);
				float baseA = Mathf.Lerp(0.8f, 0.04f, Mathf.Pow(t, 0.75f));
				float wear = Mathf.Lerp(0.55f, 1f, Noise(x / 14f, y / 14f, 3.1f));
				float a = Mathf.Clamp01(baseA * wear - Scratches(x / 8f, y / 8f) * 0.25f);
				float shade = Mathf.Lerp(0.8f, 1f, Noise(x / 6f, y / 6f, 9.4f));
				tex.SetPixel(x, y, new Color(shade, shade, shade, a));
			}
			tex.Apply();
			_slotGradient = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
			return _slotGradient;
		}

		/// <summary>
		/// Tooltip background: a pattern (close-up jagged rock by default) blended in at the configured strength,
		/// fully opaque at the bottom and fading to transparent at the top along a slightly tilted line. The box
		/// goes from near-black at the top to the tier color at the bottom. Grey, so the tier color tints it.
		///
		/// Customizing:
		///  - "Tooltip texture strength" in the config: how much of the pattern shows (0 = flat color).
		///  - textures/tooltip_texture.png: replaces only the pattern. Colors, fade, glow and frame stay.
		///  - textures/tooltip_background.png: replaces the whole background layer (advanced).
		/// </summary>
		public static Sprite TooltipWash()
		{
			if (_tooltipWash != null) return _tooltipWash;
			_tooltipWash = LoadOverride("tooltip_background.png", 0);
			if (_tooltipWash != null) return _tooltipWash;

			const int w = 256, h = 512;
			const float tiltDegrees = 8f;
			float[,] pattern = LoadPattern("tooltip_texture.png", w, h) ?? RockPattern(w, h);

			// Blend toward the pattern's own average, so changing the strength never changes the overall color.
			float mean = 0f;
			foreach (float v in pattern) mean += v;
			mean /= w * h;
			float strength = Mathf.Clamp01(Plugin.TooltipTextureStrength.Value);

			var tex = NewTexture(w, h);
			float tilt = tiltDegrees * Mathf.Deg2Rad;
			for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				float shade = Mathf.Lerp(mean, pattern[x, y], strength);
				// Opacity: 0 at the top, 1 at the bottom, along a line tilted a few degrees.
				float fromTop = (h - 1 - y) / (float)(h - 1);
				float u = Mathf.Clamp01(fromTop * Mathf.Cos(tilt) + (x - w * 0.5f) / h * Mathf.Sin(tilt));
				float a = u * u * (3f - 2f * u);
				tex.SetPixel(x, y, new Color(shade, shade, shade, a));
			}
			tex.Apply();
			_tooltipWash = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f));
			return _tooltipWash;
		}

		/// <summary>Throws away the cached background so it's rebuilt (e.g. after the strength setting changes).</summary>
		public static void ResetTooltipWash() => _tooltipWash = null;

		/// <summary>The built-in pattern: warped ridged noise, stepped ledges, light from the top left, dark crevices.</summary>
		private static float[,] RockPattern(int w, int h)
		{
			var height = new float[w, h];
			var cavity = new float[w, h];
			for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				int top = h - 1 - y; // texture rows run bottom-up; design in top-down terms
				float wx = x / 60f + 1.8f * Noise(x / 70f, top / 70f, 4.1f);
				float wy = top / 60f + 1.8f * Noise(x / 70f + 9f, top / 70f + 3f, 6.3f);
				float big = Ridged(wx, wy, 2.2f, 5);
				float mid = Ridged(wx * 2.7f, wy * 2.7f, 7.7f, 4);
				float grit = Noise(x / 2.2f, top / 2.2f, 11.1f);
				float v = 0.6f * big + 0.3f * mid + 0.1f * grit;
				const float steps = 6f;
				float q = Mathf.Floor(v * steps);
				float ledge = (q + Mathf.Clamp01((v * steps - q) * 3f)) / steps;
				height[x, y] = 0.55f * v + 0.45f * ledge;
				cavity[x, y] = Mathf.Clamp(1f - (Noise(wx * 1.3f, wy * 1.3f, 3.3f) - 0.35f) * 1.2f, 0.55f, 1f);
			}
			var result = new float[w, h];
			for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				float gx = height[Mathf.Min(x + 1, w - 1), y] - height[Mathf.Max(x - 1, 0), y];
				float gy = height[x, Mathf.Max(y - 1, 0)] - height[x, Mathf.Min(y + 1, h - 1)];
				float emboss = Mathf.Clamp((-gx - gy) * 7f, -0.4f, 0.4f);
				result[x, y] = Mathf.Clamp((0.35f + 0.6f * height[x, y] + emboss) * cavity[x, y], 0.15f, 1f);
			}
			return result;
		}

		/// <summary>Reads a custom pattern image as greys (light = raised), stretched to w x h. Null if there's no file.</summary>
		private static float[,] LoadPattern(string file, int w, int h)
		{
			Sprite sprite = LoadOverride(file, 0);
			if (sprite == null) return null;
			Texture2D src = sprite.texture;
			var result = new float[w, h];
			try
			{
				for (int y = 0; y < h; y++)
				for (int x = 0; x < w; x++)
				{
					Color c = src.GetPixelBilinear((x + 0.5f) / w, (y + 0.5f) / h);
					result[x, y] = c.grayscale;
				}
			}
			catch (UnityException)
			{
				Plugin.Log.LogWarning($"Couldn't read {file}; using the built-in rock pattern.");
				return null;
			}
			return result;
		}

		/// <summary>Ridged noise: sharp crests where smooth noise crosses its middle, like rock ridges.</summary>
		private static float Ridged(float x, float y, float seed, int octaves)
		{
			float v = 0f, amp = 0.5f, freq = 1f, total = 0f, weight = 1f;
			for (int o = 0; o < octaves; o++)
			{
				float n = Mathf.PerlinNoise(x * freq + seed, y * freq + seed * 1.3f);
				float r = 1f - Mathf.Abs(2f * n - 1f);
				r = r * r * weight;
				weight = Mathf.Clamp01(r * 1.6f);
				v += amp * r;
				total += amp;
				amp *= 0.5f;
				freq *= 2.1f;
			}
			return v / total;
		}

		/// <summary>
		/// A worn bronze frame for 9-slicing: dark outer edge, bronze band with a bright inner line,
		/// small studs in the corners, and a soft shadow on the inside.
		/// </summary>
		public static Sprite Frame()
		{
			if (_frame != null) return _frame;
			// A custom frame in textures/tooltip_frame.png replaces this one. Its corners (FrameBorder pixels)
			// stay sharp and the edges stretch.
			_frame = LoadOverride("tooltip_frame.png", FrameBorder);
			if (_frame != null) return _frame;
			const int size = 64, border = 14;
			var tex = NewTexture(size, size);
			var dark = new Color(0.06f, 0.045f, 0.03f, 1f);
			var bronze = new Color(0.47f, 0.35f, 0.19f, 1f);
			var bright = new Color(0.82f, 0.66f, 0.38f, 1f);

			for (int y = 0; y < size; y++)
			for (int x = 0; x < size; x++)
			{
				int d = Mathf.Min(Mathf.Min(x, y), Mathf.Min(size - 1 - x, size - 1 - y)); // distance to edge
				float grit = Mathf.Lerp(0.7f, 1.1f, Noise(x / 5f, y / 5f, 4.4f));
				Color c;
				if (d == 0) c = dark;
				else if (d <= 3) c = bronze * grit;
				else if (d == 4) c = bright * grit;
				else if (d == 5) c = dark;
				else if (d <= 9) c = new Color(0f, 0f, 0f, Mathf.Lerp(0.55f, 0f, (d - 6) / 4f)); // inner shadow
				else c = Color.clear;

				// Corner studs: a small bronze diamond with a bright center.
				int cx = x < size / 2 ? 7 : size - 8;
				int cy = y < size / 2 ? 7 : size - 8;
				int dm = Mathf.Abs(x - cx) + Mathf.Abs(y - cy);
				if (dm <= 5) c = dm <= 1 ? bright : dm <= 3 ? bronze * grit : dark;

				if (c.a > 0f) c.a = Mathf.Min(1f, c.a);
				tex.SetPixel(x, y, c);
			}
			tex.Apply();
			_frame = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0,
				SpriteMeshType.FullRect, new Vector4(border, border, border, border));
			return _frame;
		}

		/// <summary>Corner size in pixels for a custom tooltip_frame.png.</summary>
		public const int FrameBorder = 24;

		/// <summary>Loads a PNG from the mod's textures folder, or null if there isn't one.</summary>
		internal static Sprite LoadOverride(string file, int border)
		{
			string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "";
			string[] candidates =
			{
				Path.Combine(dir, "textures", file),
				Path.Combine(dir, file),
				Path.Combine(dir, "LegendaryLootARPG", "textures", file),
			};
			foreach (string path in candidates)
			{
				if (!File.Exists(path)) continue;
				var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
				if (!LoadImage(tex, File.ReadAllBytes(path))) continue;
				Plugin.Log.LogInfo($"Using custom {file} ({tex.width}x{tex.height}).");
				return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f, 0,
					SpriteMeshType.FullRect, new Vector4(border, border, border, border));
			}
			return null;
		}

		/// <summary>ImageConversion.LoadImage, called by reflection (like Keen Combat) so the build needs no extra Unity reference.</summary>
		private static bool LoadImage(Texture2D tex, byte[] data)
		{
			System.Type t = System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule");
			MethodInfo m = t?.GetMethod("LoadImage", new[] { typeof(Texture2D), typeof(byte[]) });
			if (m == null)
			{
				Plugin.Log.LogWarning("UnityEngine.ImageConversion.LoadImage not found; custom textures are off.");
				return false;
			}
			return (bool)m.Invoke(null, new object[] { tex, data });
		}

		private static Texture2D NewTexture(int w, int h)
		{
			return new Texture2D(w, h, TextureFormat.RGBA32, false)
			{
				wrapMode = TextureWrapMode.Clamp,
				filterMode = FilterMode.Bilinear,
			};
		}
	}
}
