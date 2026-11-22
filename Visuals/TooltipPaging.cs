using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryLoot
{
	/// <summary>
	/// Very long item tooltips can be taller than the screen, which makes vanilla shake the box up and down.
	/// Item tooltips are capped at part of the screen height; anything longer scrolls smoothly inside a
	/// window under the item name, with the text fading out at the edge where there's more to read.
	/// Mouse wheel, or the right stick (down or right = further down).
	/// </summary>
	public static class TooltipPaging
	{
		private const string ViewportName = "LLA_Viewport";
		private const string HintName = "LLA_ScrollHint";
		private const string GlowName = "LLA_ScrollHintGlow";
		private const int Fade = 32;              // height of the fade at a scrollable edge, in pixels
		private const float WheelStep = 90f;      // pixels per mouse wheel notch
		private const float StickSpeed = 700f;    // pixels per second at full stick
		private const float Smoothing = 14f;      // how quickly the text glides to where you scrolled
		private const float HintHeight = 24f;
		private const float HintBottom = 12f;      // the note's distance from the box's bottom edge
		/// <summary>
		/// Clear band at the bottom of the tooltip box, measured from the box's real bottom edge: the note's
		/// offset, its height and a little air. While there's more to read, text is cut off above this band and
		/// fades out over the 32 px above the cut.
		/// </summary>
		private const float BottomClear = HintBottom + HintHeight + 10f;
		private static Texture2D s_glowTex;
		private static Sprite s_glowSprite;

		/// <summary>Kept on the tooltip text: where it came from (to put it back) and the scroll position.</summary>
		private class State : MonoBehaviour
		{
			public Transform OriginalParent;
			public int OriginalIndex;
			public Vector2 AnchorMin, AnchorMax, Pivot, OffsetMin, OffsetMax;
			public bool Scrolling;
			public float Scroll, Target, Max;
			public string LastText;
			public RectMask2D Mask;
			public TMP_Text Hint;
			public Image Glow;
			public RectTransform Box;
			public float Cut;                 // bottom mask padding, in the window's own units
		}

		public static void Apply(GameObject root, GameObject box, bool isItem)
		{
			Transform textT = Utils.FindChild(root.transform, "Text");
			TMP_Text text = textT != null ? textT.GetComponent<TMP_Text>() : null;
			if (text == null) return;
			var state = text.GetComponent<State>() ?? text.gameObject.AddComponent<State>();
			if (box != null) state.Box = (RectTransform)box.transform;

			// Vanilla re-sets the text every frame while the inventory is open; only new text starts at the top.
			if (text.text != state.LastText)
			{
				state.LastText = text.text;
				state.Scroll = state.Target = 0f;
			}

			float cap = isItem ? TextHeightCap(root) : float.MaxValue;
			float full = text.preferredHeight;
			// A dead zone around the limit: inside the window the text wraps at a hair different width, so its height
			// shifts a little. Without this, a tooltip right at the limit flips in and out of scrolling every frame.
			const float DeadZone = 40f;
			bool need = isItem && (state.Scrolling ? full > cap - DeadZone : full > cap);
			if (need)
			{
				EnsureViewport(text, state, cap);
				var rt = (RectTransform)text.transform;
				rt.sizeDelta = new Vector2(rt.sizeDelta.x, full);
				// Where the text must stop: the clear band above the box's bottom edge, converted into the window's
				// own space (the window can reach lower than the box, so it can't be measured from the window).
				var vpRt = (RectTransform)rt.parent;
				Rect vr = vpRt.rect;
				float cutLocal = vr.yMin + TooltipPadding.Edge;
				if (state.Box != null)
				{
					var corners = new Vector3[4];
					state.Box.GetWorldCorners(corners);
					float boxBottom = vpRt.InverseTransformPoint(corners[0]).y;
					float scale = state.Box.lossyScale.y > 0f && vpRt.lossyScale.y > 0f ? state.Box.lossyScale.y / vpRt.lossyScale.y : 1f;
					cutLocal = boxBottom + BottomClear * scale;
				}
				state.Cut = Mathf.Clamp(cutLocal - vr.yMin, 0f, vr.height * 0.8f);
				float visible = vr.height - state.Cut;
				// Scroll far enough that the last line sits fully above the fade (the text ends with its own bottom
				// margin, which doesn't need to be shown).
				state.Max = Mathf.Max(0f, full - TooltipPadding.Edge - visible + Fade + 4f);
				state.Target = Mathf.Clamp(state.Target, 0f, state.Max);
				state.Scroll = Mathf.Clamp(state.Scroll, 0f, state.Max);
				ApplyScroll(text, state);
			}
			else if (state.Scrolling)
			{
				Restore(text, state);
			}
			KeepTitleOnTop(state.OriginalParent != null ? state.OriginalParent : (box != null ? box.transform : null));
			UpdateHint(box, text, state);
		}

		/// <summary>Safety net: the title always sits above the text (or its scrolling window) in the tooltip's stack.</summary>
		private static void KeepTitleOnTop(Transform parent)
		{
			if (parent == null) return;
			Transform topic = parent.Find("Topic");
			if (topic == null) return;
			int first = int.MaxValue;
			for (int i = 0; i < parent.childCount; i++)
			{
				Transform c = parent.GetChild(i);
				if (c.name == ViewportName || c.name == "Text") { first = Mathf.Min(first, i); }
			}
			if (first != int.MaxValue && topic.GetSiblingIndex() > first)
			{
				topic.SetSiblingIndex(first);
				LayoutRebuilder.MarkLayoutForRebuild((RectTransform)parent);
			}
		}

		/// <summary>The screen-height share from the config, minus the title and the box's padding.</summary>
		private static float TextHeightCap(GameObject root)
		{
			Canvas canvas = root.GetComponentInParent<Canvas>();
			canvas = canvas != null ? canvas.rootCanvas : null;
			float screen = canvas != null ? ((RectTransform)canvas.transform).rect.height : Screen.height;
			float maxBox = screen * Mathf.Clamp(Plugin.TooltipMaxHeight.Value, 0.3f, 0.95f);
			Transform topicT = Utils.FindChild(root.transform, "Topic");
			TMP_Text topic = topicT != null ? topicT.GetComponent<TMP_Text>() : null;
			float topicHeight = topic != null && topic.gameObject.activeInHierarchy ? topic.preferredHeight : 0f;
			return Mathf.Max(120f, maxBox - topicHeight - 24f);
		}

		/// <summary>Moves the text into a clipping window that takes its place in the tooltip's layout.</summary>
		private static void EnsureViewport(TMP_Text text, State state, float cap)
		{
			var rt = (RectTransform)text.transform;
			Transform parent = rt.parent;
			if (parent == null) return;
			if (parent.name != ViewportName)
			{
				state.OriginalParent = parent;
				state.OriginalIndex = rt.GetSiblingIndex();
				state.AnchorMin = rt.anchorMin;
				state.AnchorMax = rt.anchorMax;
				state.Pivot = rt.pivot;
				state.OffsetMin = rt.offsetMin;
				state.OffsetMax = rt.offsetMax;

				// The window takes the text's exact place and width in the tooltip. (The tooltip's layout doesn't size
				// its children's width, so a new object would otherwise keep Unity's default 100 px.)
				float width = rt.rect.width;
				var vp = new GameObject(ViewportName, typeof(RectTransform));
				vp.transform.SetParent(parent, false);
				vp.transform.SetSiblingIndex(state.OriginalIndex);
				var vpRt = (RectTransform)vp.transform;
				vpRt.anchorMin = rt.anchorMin;
				vpRt.anchorMax = rt.anchorMax;
				vpRt.pivot = rt.pivot;
				vpRt.anchoredPosition = rt.anchoredPosition;
				vpRt.sizeDelta = new Vector2(rt.sizeDelta.x, cap);
				var vpLayout = vp.AddComponent<LayoutElement>();
				vpLayout.preferredWidth = width;
				vpLayout.minWidth = width;
				state.Mask = vp.AddComponent<RectMask2D>();
				state.Mask.softness = new Vector2Int(0, Fade);

				rt.SetParent(vp.transform, false);
				rt.anchorMin = new Vector2(0f, 1f);
				rt.anchorMax = new Vector2(1f, 1f);
				rt.pivot = new Vector2(0.5f, 1f);
				rt.offsetMin = new Vector2(0f, rt.offsetMin.y);
				rt.offsetMax = new Vector2(0f, rt.offsetMax.y);
				LayoutElement own = text.GetComponent<LayoutElement>();
				if (own != null) own.enabled = false;
			}
			var le = rt.parent.GetComponent<LayoutElement>();
			le.preferredHeight = cap;
			le.minHeight = cap;
			state.Scrolling = true;
			LayoutRebuilder.MarkLayoutForRebuild((RectTransform)rt.parent.parent);
		}

		/// <summary>Puts the text back where vanilla had it (short tooltips, non-item tooltips).</summary>
		private static void Restore(TMP_Text text, State state)
		{
			var rt = (RectTransform)text.transform;
			Transform vp = rt.parent;
			if (vp != null && vp.name == ViewportName && state.OriginalParent != null)
			{
				rt.SetParent(state.OriginalParent, false);
				rt.SetSiblingIndex(state.OriginalIndex);
				rt.anchorMin = state.AnchorMin;
				rt.anchorMax = state.AnchorMax;
				rt.pivot = state.Pivot;
				rt.offsetMin = state.OffsetMin;
				rt.offsetMax = state.OffsetMax;
				Object.Destroy(vp.gameObject);
				LayoutRebuilder.MarkLayoutForRebuild((RectTransform)state.OriginalParent);
			}
			state.Scrolling = false;
			state.Scroll = state.Target = state.Max = 0f;
		}

		private static void ApplyScroll(TMP_Text text, State state)
		{
			var rt = (RectTransform)text.transform;
			rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, state.Scroll);
			if (state.Mask != null)
			{
				// Only fade an edge when there's more text past it: at the very top (or bottom) the mask reaches
				// past the window by the fade height, so the first (or last) line is fully readable.
				// Top: only fade when there's text above; at the very top the mask reaches past the window so the
				// first line is fully readable. Bottom: always stop short of the frame and the scroll note, with
				// the text fading out above that band.
				float top = Mathf.Clamp01(state.Scroll / Fade);
				// At the very end the note disappears, so the cut drops back to the frame edge.
				float end = state.Max > 0f ? Mathf.Clamp01((state.Max - state.Scroll) / Fade) : 0f;
				float cut = Mathf.Lerp(Mathf.Min(state.Cut, TooltipPadding.Edge), state.Cut, end);
				state.Mask.padding = new Vector4(0f, cut, 0f, -Fade * (1f - top));
			}
		}

		private static void UpdateHint(GameObject box, TMP_Text text, State state)
		{
			if (box == null) return;
			if (state.Hint == null)
			{
				Transform existing = box.transform.Find(HintName);
				if (existing != null)
				{
					state.Hint = existing.GetComponent<TMP_Text>();
					Transform g = box.transform.Find(GlowName);
					if (g != null) state.Glow = g.GetComponent<Image>();
				}
				else if (state.Scrolling)
				{
					// Soft black glow behind the note, so scrolling text never clashes with it.
					var glowGo = new GameObject(GlowName, typeof(RectTransform));
					glowGo.transform.SetParent(box.transform, false);
					glowGo.AddComponent<LayoutElement>().ignoreLayout = true;
					var grt = (RectTransform)glowGo.transform;
					grt.anchorMin = grt.anchorMax = new Vector2(1f, 0f);
					grt.pivot = new Vector2(0.5f, 0.5f);
					state.Glow = glowGo.AddComponent<Image>();
					state.Glow.sprite = GlowSprite();
					state.Glow.color = new Color(0f, 0f, 0f, 0.92f);
					state.Glow.raycastTarget = false;

					// Created switched off, so TextMeshPro doesn't go looking for its default font (which Valheim
					// doesn't ship) before ours is set.
					var go = new GameObject(HintName, typeof(RectTransform));
					go.SetActive(false);
					go.transform.SetParent(box.transform, false);
					go.AddComponent<LayoutElement>().ignoreLayout = true;
					var rt = (RectTransform)go.transform;
					rt.anchorMin = new Vector2(1f, 0f);
					rt.anchorMax = new Vector2(1f, 0f);
					rt.pivot = new Vector2(1f, 0f);
					rt.anchoredPosition = new Vector2(-TooltipPadding.Breathing - 10f, HintBottom);
					rt.sizeDelta = new Vector2(240f, HintHeight);
					var hint = go.AddComponent<TextMeshProUGUI>();
					hint.font = text.font;
					hint.fontSize = text.fontSize * 0.7f;
					hint.alignment = TextAlignmentOptions.BottomRight;
					hint.color = new Color(0.75f, 0.72f, 0.65f, 0.9f);
					hint.raycastTarget = false;
					state.Hint = hint;
				}
			}
			if (state.Hint == null) return;
			bool more = state.Scrolling && state.Scroll < state.Max - 2f;
			state.Hint.gameObject.SetActive(more);
			if (state.Glow != null) state.Glow.gameObject.SetActive(more);
			if (!more) return;
			state.Hint.font = text.font;
			state.Hint.text = TooltipFonts.SmallCaps("Scroll for more");
			if (state.Glow != null)
			{
				// Centered on the words themselves (the note is right-aligned in a wider box).
				var hrt = (RectTransform)state.Hint.transform;
				float w = Mathf.Min(state.Hint.preferredWidth, hrt.sizeDelta.x);
				var grt = (RectTransform)state.Glow.transform;
				grt.anchoredPosition = new Vector2(hrt.anchoredPosition.x - w * 0.5f, hrt.anchoredPosition.y + HintHeight * 0.5f);
				grt.sizeDelta = new Vector2(w + 110f, HintHeight + 46f);
				state.Glow.transform.SetAsLastSibling();
			}
			state.Hint.transform.SetAsLastSibling();
		}

		/// <summary>A soft black oval that fades to nothing at its edges.</summary>
		private static Sprite GlowSprite()
		{
			if (s_glowSprite != null) return s_glowSprite;
			const int W = 128, H = 64;
			s_glowTex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
			var px = new Color32[W * H];
			for (int y = 0; y < H; y++)
				for (int x = 0; x < W; x++)
				{
					float u = (x + 0.5f) / W * 2f - 1f, v = (y + 0.5f) / H * 2f - 1f;
					float d = Mathf.Sqrt(u * u + v * v);
					float a = Mathf.Clamp01(1f - d);
					a = a * a * (3f - 2f * a);      // smooth falloff
					px[y * W + x] = new Color32(0, 0, 0, (byte)(a * 255f));
				}
			s_glowTex.SetPixels32(px);
			s_glowTex.Apply();
			s_glowSprite = Sprite.Create(s_glowTex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f));
			return s_glowSprite;
		}

		/// <summary>Scrolls the tooltip that's showing: mouse wheel, or the right stick on a controller.</summary>
		[HarmonyPatch(typeof(UITooltip), nameof(UITooltip.LateUpdate))]
		private static class Patch_LateUpdate
		{
			private static void Postfix(UITooltip __instance)
			{
				if (UITooltip.m_current != __instance || UITooltip.m_tooltip == null) return;
				Transform textT = Utils.FindChild(UITooltip.m_tooltip.transform, "Text");
				State state = textT != null ? textT.GetComponent<State>() : null;
				if (state == null || !state.Scrolling) return;
				TMP_Text text = textT.GetComponent<TMP_Text>();
				float dt = Time.unscaledDeltaTime;

				float wheel = ZInput.GetMouseScrollWheel();
				if (wheel < -0.01f) state.Target += WheelStep;
				else if (wheel > 0.01f) state.Target -= WheelStep;

				// Right stick: down or right scrolls further down. (Valheim reports "down" as positive.)
				float y = ZInput.GetJoyRightStickY();
				float x = ZInput.GetJoyRightStickX();
				float stick = Mathf.Abs(x) > Mathf.Abs(y) ? x : y;
				if (Mathf.Abs(stick) > 0.2f) state.Target += stick * StickSpeed * dt;

				state.Target = Mathf.Clamp(state.Target, 0f, state.Max);
				float before = state.Scroll;
				state.Scroll = Mathf.Lerp(state.Scroll, state.Target, 1f - Mathf.Exp(-Smoothing * dt));
				if (Mathf.Abs(state.Scroll - state.Target) < 0.5f) state.Scroll = state.Target;
				if (Mathf.Approximately(before, state.Scroll)) return;
				ApplyScroll(text, state);
				GameObject box = state.OriginalParent != null ? state.OriginalParent.gameObject : null;
				UpdateHint(box, text, state);
			}
		}
	}
}
