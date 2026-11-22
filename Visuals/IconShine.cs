using UnityEngine;
using UnityEngine.UI;

namespace LegendaryLoot
{
	/// <summary>
	/// A slanted glint that sweeps across a Unique item's icon background every 20 seconds, like light
	/// catching a foil card. Every Unique on screen glints together.
	/// </summary>
	public class IconShine : MonoBehaviour
	{
		private const string ChildName = "LLA_Shine";
		private const float Period = 20f;     // seconds between glints
		private const float SweepTime = 0.45f; // how long one glint takes to cross
		private const float Strength = 0.75f; // brightest point of the glint

		private static Sprite _sprite;
		private Image _image;
		private RectTransform _rt, _parent;

		/// <summary>Turns the glint on or off for one slot's background.</summary>
		public static void Set(Image background, bool on)
		{
			if (background == null) return;
			Transform existing = background.transform.Find(ChildName);
			if (!on)
			{
				if (existing != null && existing.gameObject.activeSelf) existing.gameObject.SetActive(false);
				return;
			}
			if (existing == null)
			{
				// The background clips the glint to the slot.
				if (background.GetComponent<RectMask2D>() == null) background.gameObject.AddComponent<RectMask2D>();
				var go = new GameObject(ChildName, typeof(RectTransform));
				go.transform.SetParent(background.transform, false);
				var rt = (RectTransform)go.transform;
				rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
				rt.pivot = new Vector2(0.5f, 0.5f);
				rt.localRotation = Quaternion.Euler(0f, 0f, -25f);
				var img = go.AddComponent<Image>();
				img.sprite = Sprite();
				img.raycastTarget = false;
				go.AddComponent<IconShine>();
				existing = go.transform;
			}
			if (!existing.gameObject.activeSelf) existing.gameObject.SetActive(true);
		}

		private void Awake()
		{
			_image = GetComponent<Image>();
			_rt = (RectTransform)transform;
			_parent = (RectTransform)transform.parent;
		}

		private void Update()
		{
			float phase = (Time.unscaledTime % Period) / SweepTime;
			if (phase > 1f)
			{
				if (_image.enabled) _image.enabled = false;
				return;
			}
			_image.enabled = true;
			Vector2 size = _parent.rect.size;
			_rt.sizeDelta = new Vector2(size.x * 0.14f, size.y * 2f);
			_rt.anchoredPosition = new Vector2(Mathf.Lerp(-0.9f, 0.9f, phase) * size.x, 0f);
			_image.color = new Color(1f, 0.97f, 0.85f, Strength * Mathf.Sin(phase * Mathf.PI));
		}

		/// <summary>A soft white band, brightest down its middle.</summary>
		private static Sprite Sprite()
		{
			if (_sprite != null) return _sprite;
			const int W = 32, H = 4;
			var tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
			var px = new Color32[W * H];
			for (int x = 0; x < W; x++)
			{
				float u = (x + 0.5f) / W * 2f - 1f;
				float a = Mathf.Clamp01(1f - Mathf.Abs(u));
				a = a * a * (3f - 2f * a);
				for (int y = 0; y < H; y++) px[y * W + x] = new Color32(255, 255, 255, (byte)(a * 255f));
			}
			tex.SetPixels32(px);
			tex.Apply();
			_sprite = UnityEngine.Sprite.Create(tex, new Rect(0, 0, W, H), new Vector2(0.5f, 0.5f));
			return _sprite;
		}
	}
}
