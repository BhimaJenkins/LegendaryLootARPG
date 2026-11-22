using HarmonyLib;
using LegendaryLoot.Core;
using UnityEngine;

namespace LegendaryLoot
{
	/// <summary>
	/// A pillar of "holy fire" in the tier's color above dropped gear: a soft flame-shaped beam (wide and faint at
	/// the base, brightest higher up, tapering to a point) with a whiter core, both flickering gently, plus a
	/// flickering glow on the ground. Uniques add a taller flame and gold sparkles drifting up through it.
	/// Built at runtime (no asset bundle), and rebuilt whenever a dropped item loads, so every player sees it.
	/// </summary>
	public class LightShaft : MonoBehaviour
	{
		private const float Height = 5f;
		private static Material _material, _sparkleMaterial;
		private static Texture2D _flameTexture;
		private static Mesh _quad;

		private Transform _outer, _core;
		private Renderer _outerRenderer, _coreRenderer;
		private Light _light;
		private ParticleSystem _sparkles;
		private Tier _tier;
		private Color _color;
		private float _coreWhite = 0.55f; // how far the bright core leans toward white
		private float _seed;
		private float _baseWidth, _baseHeight, _lightIntensity;
		private MaterialPropertyBlock _block;

		public static void Attach(ItemDrop drop)
		{
			if (drop == null || !Plugin.ShowLightShafts.Value) return;
			MagicItem magic = drop.m_itemData.GetMagic();
			LightShaft existing = drop.GetComponent<LightShaft>();
			if (magic == null)
			{
				if (existing != null) Destroy(existing);
				return;
			}
			if (existing == null) existing = drop.gameObject.AddComponent<LightShaft>();
			existing.Setup(magic.Tier);
		}

		/// <summary>A beam of a tier's color on any object (quest chests).</summary>
		public static LightShaft Mark(GameObject go, Tier tier)
		{
			if (go == null) return null;
			LightShaft shaft = go.GetComponent<LightShaft>() ?? go.AddComponent<LightShaft>();
			shaft.Setup(tier);
			return shaft;
		}

		private void Setup(Tier tier)
		{
			if (_outer != null && _tier == tier) return;
			_tier = tier;
			// Uniques burn a copper-gold on the ground, so they stand apart from Rare's yellow and Legendary's orange.
			_color = tier == Tier.Unique ? new Color32(0xD8, 0x86, 0x3A, 0xFF) : Tiers.GetColor(tier);
			_coreWhite = tier == Tier.Unique ? 0.35f : 0.55f;
			_seed = Random.Range(0f, 100f);
			_block = _block ?? new MaterialPropertyBlock();

			// Higher tiers burn a little wider and taller; Uniques the most.
			_baseHeight = Height * (tier == Tier.Unique ? 1.25f : 0.85f + 0.05f * (int)tier);
			_baseWidth = _baseHeight * 0.29f; // about a third as wide as it is tall, like the reference art

			if (_outer == null)
			{
				_outer = MakeFlame("LLA_FlameOuter", out _outerRenderer);
				_core = MakeFlame("LLA_FlameCore", out _coreRenderer);

				var glow = new GameObject("LLA_Glow");
				glow.transform.SetParent(transform, false);
				_light = glow.AddComponent<Light>();
				_light.type = LightType.Point;
				_light.shadows = LightShadows.None;
			}

			_light.color = _color;
			_light.range = 2f + 0.3f * (int)tier;
			_lightIntensity = tier == Tier.Unique ? 1.6f : 1.2f;

			if (tier == Tier.Unique) AddSparkles(_color);
			LateUpdate();
		}

		private Transform MakeFlame(string name, out Renderer renderer)
		{
			var go = new GameObject(name);
			go.transform.SetParent(transform, false);
			go.AddComponent<MeshFilter>().sharedMesh = GetQuad();
			var mr = go.AddComponent<MeshRenderer>();
			mr.sharedMaterial = GetMaterial();
			mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			mr.receiveShadows = false;
			renderer = mr;
			return go.transform;
		}

		private void LateUpdate()
		{
			if (_outer == null) return;
			Vector3 p = transform.position;
			float t = Time.time;

			// Subtle flicker: small, slow, uneven changes in width, height and brightness, different for each item.
			float n1 = Mathf.PerlinNoise(_seed, t * 1.6f);
			float n2 = Mathf.PerlinNoise(_seed + 17f, t * 2.2f);
			float n3 = Mathf.PerlinNoise(_seed + 41f, t * 3.4f);

			// Face the camera, turning only around the vertical axis, so the flame always looks full.
			Quaternion facing = Quaternion.identity;
			Camera cam = Utils.GetMainCamera();
			if (cam != null)
			{
				Vector3 toCam = cam.transform.position - p;
				toCam.y = 0f;
				if (toCam.sqrMagnitude > 0.001f) facing = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
			}

			float sway = (n3 - 0.5f) * 0.015f; // the tip leans only slightly
			Place(_outer, _outerRenderer, p, facing, sway,
				_baseWidth * (0.97f + 0.06f * n1), _baseHeight * (0.98f + 0.04f * n2),
				_color, 0.6f + 0.1f * n2);
			Place(_core, _coreRenderer, p, facing, sway * 0.6f,
				_baseWidth * 0.35f * (0.96f + 0.08f * n2), _baseHeight * 0.9f * (0.98f + 0.04f * n1),
				Color.Lerp(_color, Color.white, _coreWhite), 0.75f + 0.12f * n3);

			if (_light != null)
			{
				_light.transform.position = p + Vector3.up * 0.4f;
				_light.intensity = _lightIntensity * (0.94f + 0.12f * n3);
			}
			if (_sparkles != null) _sparkles.transform.position = p;
		}

		private void Place(Transform flame, Renderer r, Vector3 p, Quaternion facing, float sway, float width, float height, Color color, float alpha)
		{
			flame.position = p - Vector3.up * 0.2f;
			flame.rotation = facing * Quaternion.Euler(0f, 0f, sway * Mathf.Rad2Deg);
			// The flame is a child of the item (so it's cleaned up with it); undo the item's own scale.
			float parentScale = Mathf.Max(0.01f, transform.lossyScale.x);
			flame.localScale = new Vector3(width, height, 1f) / parentScale;
			color.a = alpha;
			_block.SetColor("_Color", color);      // alpha-blended shaders
			_block.SetColor("_TintColor", color);  // particle (additive) shaders
			r.SetPropertyBlock(_block);
		}

		// Shared assets, built once ------------------------------------------------------------------------

		/// <summary>A 1 x 1 quad with its pivot at the bottom center, so it grows upward from the item.</summary>
		private static Mesh GetQuad()
		{
			if (_quad != null) return _quad;
			_quad = new Mesh { name = "LLA_FlameQuad" };
			_quad.vertices = new[] { new Vector3(-0.5f, 0f, 0f), new Vector3(0.5f, 0f, 0f), new Vector3(-0.5f, 1f, 0f), new Vector3(0.5f, 1f, 0f) };
			_quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
			_quad.triangles = new[] { 0, 2, 1, 2, 3, 1, 0, 1, 2, 2, 1, 3 }; // both sides
			_quad.RecalculateBounds();
			return _quad;
		}

		/// <summary>
		/// The flame shape: wide and faint at the base, narrowing to a point at the top, brightest in the upper
		/// middle, with soft sides and a faint wisp texture so it reads as fire rather than a smooth cone.
		/// </summary>
		private static Texture2D GetFlameTexture()
		{
			if (_flameTexture != null) return _flameTexture;
			const int w = 64, h = 256;
			var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
			for (int y = 0; y < h; y++)
			for (int x = 0; x < w; x++)
			{
				float v = y / (float)(h - 1);                 // 0 = base, 1 = tip
				float u = (x / (float)(w - 1)) * 2f - 1f;     // -1 .. 1 across
				float halfWidth = Mathf.Lerp(0.95f, 0.03f, Mathf.Pow(v, 1.6f)); // stays wide, then tapers to a point
				float across = Mathf.Exp(-(u / halfWidth) * (u / halfWidth) * 2.2f);
				float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(v / 0.8f));
				float along = Mathf.Pow(rise, 1.3f) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((v - 0.9f) / 0.1f)));
				float wisps = Mathf.Lerp(0.8f, 1f, Mathf.PerlinNoise(u * 3f + 5f, v * 9f));
				tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(across * along * wisps)));
			}
			tex.Apply();
			_flameTexture = tex;
			return tex;
		}

		/// <summary>Additive blending if the game has such a shader (glows like light), otherwise soft alpha blending.</summary>
		internal static Material GetMaterial()
		{
			if (_material != null) return _material;
			Shader shader = Shader.Find("Legacy Shaders/Particles/Additive") ?? Shader.Find("Particles/Additive")
				?? Shader.Find("Mobile/Particles/Additive") ?? Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default");
			_material = new Material(shader) { renderQueue = 3100, mainTexture = GetFlameTexture() };
			return _material;
		}

		// Unique sparkles ---------------------------------------------------------------------------------

		private void AddSparkles(Color color)
		{
			if (_sparkles != null) return;
			var go = new GameObject("LLA_UniqueSparkles");
			go.transform.SetParent(transform, false);
			go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // cone points up
			_sparkles = go.AddComponent<ParticleSystem>();
			_sparkles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

			ParticleSystem.MainModule main = _sparkles.main;
			main.loop = true;
			main.duration = 2f;
			main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
			main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
			main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
			main.startColor = new ParticleSystem.MinMaxGradient(color, Color.white);
			main.maxParticles = 80;
			main.simulationSpace = ParticleSystemSimulationSpace.World;
			main.gravityModifier = -0.05f;

			ParticleSystem.EmissionModule emission = _sparkles.emission;
			emission.rateOverTime = 28f;

			ParticleSystem.ShapeModule shape = _sparkles.shape;
			shape.shapeType = ParticleSystemShapeType.Cone;
			shape.angle = 6f;
			shape.radius = 0.18f;

			// Twinkle: grow and shrink a couple of times over each sparkle's life, then fade out.
			ParticleSystem.SizeOverLifetimeModule size = _sparkles.sizeOverLifetime;
			size.enabled = true;
			var curve = new AnimationCurve(
				new Keyframe(0f, 0f), new Keyframe(0.15f, 1f), new Keyframe(0.35f, 0.4f),
				new Keyframe(0.55f, 1f), new Keyframe(0.8f, 0.5f), new Keyframe(1f, 0f));
			size.size = new ParticleSystem.MinMaxCurve(1f, curve);

			ParticleSystem.ColorOverLifetimeModule col = _sparkles.colorOverLifetime;
			col.enabled = true;
			var fade = new Gradient();
			fade.SetKeys(
				new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
				new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
			col.color = fade;

			var renderer = go.GetComponent<ParticleSystemRenderer>();
			renderer.material = GetSparkleMaterial();
			renderer.renderMode = ParticleSystemRenderMode.Billboard;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			_sparkles.Play();
		}

		internal static Material GetSparkleMaterial()
		{
			if (_sparkleMaterial != null) return _sparkleMaterial;
			// A small four-pointed star with a soft glow, drawn once in code.
			const int n = 32;
			var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
			for (int y = 0; y < n; y++)
			for (int x = 0; x < n; x++)
			{
				float dx = (x - (n - 1) / 2f) / (n / 2f), dy = (y - (n - 1) / 2f) / (n / 2f);
				float r = Mathf.Sqrt(dx * dx + dy * dy);
				float glow = Mathf.Clamp01(1f - r) * 0.6f;
				float star = Mathf.Clamp01(1f - Mathf.Abs(dx) * 9f) * Mathf.Clamp01(1f - Mathf.Abs(dy)) +
				             Mathf.Clamp01(1f - Mathf.Abs(dy) * 9f) * Mathf.Clamp01(1f - Mathf.Abs(dx));
				tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(glow * glow + star)));
			}
			tex.Apply();
			_sparkleMaterial = new Material(GetMaterial()) { mainTexture = tex };
			return _sparkleMaterial;
		}

		/// <summary>Rebuild the flame whenever a dropped item loads its data (world load, other players' drops, item stands releasing items).</summary>
		[HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Start))]
		private static class Patch_Start
		{
			private static void Postfix(ItemDrop __instance) => Attach(__instance);
		}

		[HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.Load))]
		private static class Patch_Load
		{
			private static void Postfix(ItemDrop __instance)
			{
				if (__instance.isActiveAndEnabled) Attach(__instance);
			}
		}
	}
}
