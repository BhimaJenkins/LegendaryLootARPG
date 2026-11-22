using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryLoot
{
	/// <summary>
	/// The mod's own on-screen keyboard for controllers, like most console keyboards: a grid of keys moved with the
	/// D-pad or left stick (key to key, never a free cursor), with hold-to-repeat.
	///  A types the highlighted key, X deletes, Y adds a space, B closes (the text stays).
	/// While it's open the inventory's own controller navigation, button hints and close keys are paused.
	/// </summary>
	public class OnScreenKeyboard : MonoBehaviour
	{
		private const int Cols = 10;
		private const float KeyW = 58f, KeyH = 50f, Gap = 6f;
		private static readonly string[] Rows = { "1234567890", "QWERTYUIOP", "ASDFGHJKL'", "ZXCVBNM-.,", };

		private class Key
		{
			public string Label, Insert;   // Insert: what it types (null for actions)
			public string Action;          // "space", "delete", "clear", "done"
			public int Row, Col, Span = 1;
			public Image Back;
			public TMP_Text Text;
		}

		private static OnScreenKeyboard _instance;
		public static bool IsOpen => _instance != null && _instance.gameObject.activeSelf;
		private static int _closedFrame = -10;

		/// <summary>Open, or closed this frame or the last (so the B that closed it can't also close the inventory).</summary>
		public static bool Blocking => IsOpen || Time.frameCount - _closedFrame <= 1;

		private readonly List<Key> _keys = new List<Key>();
		private Key[,] _grid;
		private int _row = 1, _col;
		private TMP_InputField _target;
		private TMP_Text _preview;
		private float _repeatAt;
		private Vector2Int _heldDir;
		private int _openedFrame;

		private static readonly Color KeyColor = new Color(0.16f, 0.12f, 0.08f, 0.95f);
		private static readonly Color KeyText = new Color(0.93f, 0.88f, 0.76f);
		private static readonly Color SelColor = new Color(0.83f, 0.69f, 0.22f, 1f);
		private static readonly Color SelText = new Color(0.12f, 0.08f, 0.03f);

		/// <summary>Opens the keyboard for a text field (building it the first time, under the field's canvas).</summary>
		public static void Open(TMP_InputField target)
		{
			if (target == null) return;
			if (_instance == null) Build(target);
			if (_instance == null) return;
			_instance._target = target;
			_instance._openedFrame = Time.frameCount;
			_instance.gameObject.SetActive(true);
			_instance.Refresh();
		}

		public static void Close()
		{
			if (_instance == null || !_instance.gameObject.activeSelf) return;
			_instance.gameObject.SetActive(false);
			_closedFrame = Time.frameCount;
		}

		private static void Build(TMP_InputField target)
		{
			Canvas canvas = target.GetComponentInParent<Canvas>();
			canvas = canvas != null ? canvas.rootCanvas : null;
			TMP_Text fontSource = target.textComponent;
			if (canvas == null || fontSource == null) return;

			var root = new GameObject("LLA_OnScreenKeyboard", typeof(RectTransform));
			root.transform.SetParent(canvas.transform, false);
			var rt = (RectTransform)root.transform;
			float width = Cols * KeyW + (Cols - 1) * Gap + 40f;
			float height = 5 * KeyH + 4 * Gap + 130f;
			rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
			rt.pivot = new Vector2(0.5f, 0f);
			rt.sizeDelta = new Vector2(width, height);
			rt.anchoredPosition = new Vector2(0f, 40f);
			rt.SetAsLastSibling();

			var bg = root.AddComponent<Image>();
			bg.sprite = Weathering.Frame();
			bg.type = Image.Type.Sliced;
			bg.color = Color.white;
			var fill = NewImage(root.transform, "Back", new Color(0.07f, 0.05f, 0.03f, 0.94f));
			Stretch(fill.rectTransform, 8f);
			fill.transform.SetAsFirstSibling();

			var kb = root.AddComponent<OnScreenKeyboard>();
			kb._preview = NewText(root.transform, fontSource, "Preview", 28f, KeyText);
			Place(kb._preview.rectTransform, new Vector2(20f, -18f), new Vector2(width - 40f, 40f));
			kb._preview.alignment = TextAlignmentOptions.Left;
			TMP_Text legend = NewText(root.transform, fontSource, "Legend", 18f, new Color(0.8f, 0.74f, 0.6f));
			Place(legend.rectTransform, new Vector2(20f, -60f), new Vector2(width - 40f, 30f));
			legend.alignment = TextAlignmentOptions.Left;
			legend.richText = true;
			legend.text = $"{Glyph("JoyButtonA")} Type    {Glyph("JoyButtonX")} Delete    {Glyph("JoyButtonY")} Space    {Glyph("JoyButtonB")} Done";

			// Letter rows, then the action row: Space (5 wide), Delete (2), Clear (1), Done (2).
			for (int r = 0; r < Rows.Length; r++)
				for (int c = 0; c < Cols; c++)
					kb._keys.Add(new Key { Label = Rows[r][c].ToString(), Insert = Rows[r][c].ToString().ToLowerInvariant(), Row = r, Col = c });
			kb._keys.Add(new Key { Label = "Space", Action = "space", Row = 4, Col = 0, Span = 5 });
			kb._keys.Add(new Key { Label = "Delete", Action = "delete", Row = 4, Col = 5, Span = 2 });
			kb._keys.Add(new Key { Label = "Clear", Action = "clear", Row = 4, Col = 7, Span = 1 });
			kb._keys.Add(new Key { Label = "Done", Action = "done", Row = 4, Col = 8, Span = 2 });
			kb._grid = new Key[5, Cols];
			foreach (Key k in kb._keys)
			{
				for (int i = 0; i < k.Span; i++) kb._grid[k.Row, k.Col + i] = k;
				k.Back = NewImage(root.transform, "Key_" + k.Label, KeyColor);
				float x = 20f + k.Col * (KeyW + Gap), y = -100f - k.Row * (KeyH + Gap);
				Place(k.Back.rectTransform, new Vector2(x, y), new Vector2(k.Span * KeyW + (k.Span - 1) * Gap, KeyH));
				k.Text = NewText(k.Back.transform, fontSource, "Label", k.Span > 1 ? 20f : 24f, KeyText);
				Stretch(k.Text.rectTransform, 0f);
				k.Text.alignment = TextAlignmentOptions.Center;
				k.Text.text = k.Label;
			}
			_instance = kb;
			root.SetActive(false);
		}

		private static string Glyph(string button) => Localization.instance.GetBoundKeyString(button, true);

		private static Image NewImage(Transform parent, string name, Color color)
		{
			var go = new GameObject(name, typeof(RectTransform));
			go.transform.SetParent(parent, false);
			var img = go.AddComponent<Image>();
			img.color = color;
			img.raycastTarget = false;
			return img;
		}

		private static TMP_Text NewText(Transform parent, TMP_Text source, string name, float size, Color color)
		{
			var go = new GameObject(name, typeof(RectTransform));
			go.transform.SetParent(parent, false);
			var t = go.AddComponent<TextMeshProUGUI>();
			t.font = source.font;
			t.fontSharedMaterial = source.fontSharedMaterial;
			t.spriteAsset = source.spriteAsset;
			t.fontSize = size;
			t.color = color;
			t.enableWordWrapping = false;
			t.overflowMode = TextOverflowModes.Ellipsis;
			t.raycastTarget = false;
			return t;
		}

		private static void Place(RectTransform rt, Vector2 topLeft, Vector2 size)
		{
			rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
			rt.pivot = new Vector2(0f, 1f);
			rt.anchoredPosition = topLeft;
			rt.sizeDelta = size;
		}

		private static void Stretch(RectTransform rt, float inset)
		{
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = new Vector2(inset, inset);
			rt.offsetMax = new Vector2(-inset, -inset);
		}

		private Key Selected => _grid[_row, Mathf.Clamp(_col, 0, Cols - 1)];

		private void Refresh()
		{
			Key sel = Selected;
			foreach (Key k in _keys)
			{
				bool on = k == sel;
				k.Back.color = on ? SelColor : KeyColor;
				k.Text.color = on ? SelText : KeyText;
			}
			string text = _target != null ? _target.text : "";
			_preview.text = $"Search: <color=#FFE9A0>{text}</color><color=#FFE9A0>_</color>";
		}

		private void Update()
		{
			if (_target == null || !_target.gameObject.activeInHierarchy || !InventoryGui.IsVisible() || !ZInput.IsGamepadActive())
			{
				Close();
				return;
			}
			if (Time.frameCount == _openedFrame) return;   // the R3 press that opened it

			Vector2Int dir = ReadDirection();
			if (dir != Vector2Int.zero)
			{
				if (dir != _heldDir) { Move(dir); _repeatAt = Time.unscaledTime + 0.35f; }
				else if (Time.unscaledTime >= _repeatAt) { Move(dir); _repeatAt = Time.unscaledTime + 0.09f; }
			}
			_heldDir = dir;

			if (ZInput.GetButtonDown("JoyButtonA")) Press(Selected);
			else if (ZInput.GetButtonDown("JoyButtonX")) Press(new Key { Action = "delete" });
			else if (ZInput.GetButtonDown("JoyButtonY")) Press(new Key { Action = "space" });
			else if (ZInput.GetButtonDown("JoyButtonB")) Press(new Key { Action = "done" });
		}

		private static Vector2Int ReadDirection()
		{
			if (ZInput.GetButton("JoyDPadUp") || ZInput.GetButton("JoyLStickUp")) return new Vector2Int(0, -1);
			if (ZInput.GetButton("JoyDPadDown") || ZInput.GetButton("JoyLStickDown")) return new Vector2Int(0, 1);
			if (ZInput.GetButton("JoyDPadLeft") || ZInput.GetButton("JoyLStickLeft")) return new Vector2Int(-1, 0);
			if (ZInput.GetButton("JoyDPadRight") || ZInput.GetButton("JoyLStickRight")) return new Vector2Int(1, 0);
			return Vector2Int.zero;
		}

		/// <summary>Key to key: across skips past wide keys, up/down keeps the column; both wrap around.</summary>
		private void Move(Vector2Int dir)
		{
			Key from = Selected;
			if (dir.x != 0)
			{
				int c = dir.x > 0 ? from.Col + from.Span : from.Col - 1;
				c = (c + Cols) % Cols;
				_col = _grid[_row, c].Col;
			}
			else
			{
				_row = (_row + dir.y + 5) % 5;
				_col = Mathf.Clamp(_col, 0, Cols - 1);
			}
			Refresh();
		}

		private void Press(Key k)
		{
			if (_target == null) return;
			string text = _target.text ?? "";
			switch (k.Action)
			{
				case null:
					if (text.Length < _target.characterLimit || _target.characterLimit <= 0) _target.text = text + k.Insert;
					break;
				case "space":
					if (text.Length > 0 && !text.EndsWith(" ")) _target.text = text + " ";
					break;
				case "delete":
					if (text.Length > 0) _target.text = text.Substring(0, text.Length - 1);
					break;
				case "clear":
					_target.text = "";
					break;
				case "done":
					Close();
					return;
			}
			Refresh();
		}

		// ---------- while it's open, the inventory's own controller input waits ----------

		[HarmonyPatch(typeof(InventoryGrid), nameof(InventoryGrid.UpdateGamepad))]
		private static class Patch_Grid
		{
			private static bool Prefix() => !Blocking;
		}

		[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.UpdateRecipeGamepadInput))]
		private static class Patch_Recipes
		{
			private static bool Prefix() => !Blocking;
		}

		[HarmonyPatch(typeof(UIGamePad), nameof(UIGamePad.ButtonPressed))]
		private static class Patch_Hints
		{
			private static void Postfix(ref bool __result)
			{
				if (Blocking) __result = false;
			}
		}

		[HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
		private static class Patch_Hide
		{
			private static void Postfix() => Close();
		}
	}
}
