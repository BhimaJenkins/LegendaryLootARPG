using HarmonyLib;
using UnityEngine;

namespace LegendaryLoot.Powers
{
	/// <summary>
	/// Extra attacks (Thrasher, Windfury): when one procs, the same hit lands again right away, once or twice,
	/// a few hundredths of a second apart, so you see the damage numbers pop up side by side. Each extra hit is
	/// a full hit of its own (it can crit, heal you, and so on), but extra hits never start more extra hits.
	/// The proc is shown with effects and a sound: Thrasher's blue slash, Windfury's whirlwind.
	/// </summary>
	public static class ExtraAttacks
	{
		private const float Gap = 0.07f; // seconds between the extra hits

		/// <summary>True while an extra hit is being dealt (so it can't start more extra hits).</summary>
		public static bool InExtraHit { get; private set; }

		public static void Strike(Player self, Character target, HitData hit, int count, string fx)
		{
			if (InExtraHit || self == null || target == null || hit == null || count <= 0) return;
			HitData copy = hit.Clone(); // as it was before crits and buffs: each extra hit gets its own
			Plugin.Debug($"Extra attack '{fx}': {count} more hit(s) on {target.m_name}");
			Vector3 fwd = self.transform.forward;
			SlashFx.Broadcast(fx, self.transform.position + Vector3.up * 1.1f + fwd * 0.5f, fwd);
			Plugin.Instance.StartCoroutine(Hits(target, copy, count));
		}

		private static System.Collections.IEnumerator Hits(Character target, HitData template, int count)
		{
			for (int i = 0; i < count; i++)
			{
				yield return new WaitForSeconds(Gap);
				if (target == null || target.IsDead()) yield break;
				HitData h = template.Clone();
				// Nudge where it lands so the numbers sit side by side instead of on top of each other.
				h.m_point += Random.insideUnitSphere * 0.35f + Vector3.up * (0.2f * (i + 1));
				InExtraHit = true;
				try { target.Damage(h); }
				finally { InExtraHit = false; }
			}
		}
	}

	/// <summary>
	/// A crescent slash: a bright leading edge sweeping across in front of the player, trailed by a fading tail,
	/// with a burst of sparks. Colors and sound depend on the kind ("thrasher" blue, "windfury" pale wind).
	/// Built like the light shafts (which render reliably): the head-to-tail fade and the bright outer edge are
	/// baked into the texture, and the color and fade-out go through the material.
	/// </summary>
	public class SlashFx : MonoBehaviour
	{
		private const string Rpc = "LLA_Slash";
		private const int Segments = 32;
		private const float Radius = 2.0f, Thickness = 0.7f;
		private const float SweepTime = 0.2f, FadeTime = 0.2f;
		private const float StartAngle = -80f, EndAngle = 80f, Tail = 120f;

		private static Texture2D _texture;
		private static bool _flip;
		private Mesh _mesh;
		private Material _mat;
		private float _born;
		private Color _color;

		public static void Broadcast(string kind, Vector3 pos, Vector3 forward)
		{
			if (string.IsNullOrEmpty(kind)) return;
			if (ZRoutedRpc.instance == null) { Play(kind, pos, forward); return; }
			ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, Rpc, kind, pos, forward);
		}

		[HarmonyPatch(typeof(Game), nameof(Game.Start))]
		private static class Patch_Register
		{
			private static void Postfix()
			{
				ZRoutedRpc.instance.Register<string, Vector3, Vector3>(Rpc, (sender, kind, pos, fwd) => Play(kind, pos, fwd));
			}
		}

		public static void Play(string kind, Vector3 pos, Vector3 forward)
		{
			Color color, spark;
			string sound;
			switch (kind)
			{
				case "windfury":
					color = new Color(0.6f, 1f, 0.9f); spark = new Color(0.9f, 1f, 0.97f); sound = "windfurysound.ogg"; break;
				default: // thrasher
					color = new Color(0.3f, 0.6f, 1f); spark = new Color(0.75f, 0.9f, 1f); sound = "thrashersound.ogg"; break;
			}
			SoundLibrary.PlayAt(sound, pos);
			Plugin.Debug($"Proc effect '{kind}', sound {sound}");
			if (kind == "windfury") TornadoFx.Play(pos);

			forward.y = 0f;
			if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
			_flip = !_flip; // alternate the diagonal so repeated slashes cross
			var go = new GameObject("LLA_Slash");
			go.transform.position = pos;
			go.transform.rotation = Quaternion.LookRotation(forward.normalized) * Quaternion.Euler(0f, 0f, _flip ? 28f : -28f);
			var mf = go.AddComponent<MeshFilter>();
			var mr = go.AddComponent<MeshRenderer>();
			var fx = go.AddComponent<SlashFx>();
			fx._mat = new Material(LightShaft.GetMaterial()) { mainTexture = Texture(), renderQueue = 3100 };
			mr.sharedMaterial = fx._mat;
			mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			mr.receiveShadows = false;
			fx._mesh = new Mesh();
			fx._mesh.MarkDynamic();
			mf.sharedMesh = fx._mesh;
			fx._color = color;
			fx._born = Time.time;
			Plugin.Debug($"Slash '{kind}' at {pos} using shader {fx._mat.shader.name}");
			fx.Build(0f);
			Sparks(go.transform, color, spark);
		}

		private void Update()
		{
			float age = Time.time - _born;
			if (age > SweepTime + FadeTime) { Destroy(_mat); Destroy(_mesh); Destroy(gameObject); return; }
			Build(age);
		}

		private void Build(float age)
		{
			float t = Mathf.Clamp01(age / SweepTime);
			t = 1f - (1f - t) * (1f - t);                         // fast start, easing out
			float fade = age <= SweepTime ? 1f : 1f - (age - SweepTime) / FadeTime;
			float head = Mathf.Lerp(StartAngle, EndAngle, t);
			float tail = Mathf.Max(StartAngle - 1f, head - Tail);
			int n = Segments + 1;
			var verts = new Vector3[n * 2];
			var uv = new Vector2[n * 2];
			for (int i = 0; i < n; i++)
			{
				float k = i / (float)Segments;                    // 0 = tail, 1 = head
				float a = Mathf.Lerp(tail, head, k) * Mathf.Deg2Rad;
				float thick = Mathf.Lerp(0.08f, Thickness, k * k); // thin tail, thick head
				Vector3 dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
				verts[i * 2] = dir * (Radius - thick);
				verts[i * 2 + 1] = dir * Radius;
				uv[i * 2] = new Vector2(k, 0f);
				uv[i * 2 + 1] = new Vector2(k, 1f);
			}
			var tris = new int[Segments * 6];
			for (int i = 0; i < Segments; i++)
			{
				int v = i * 2;
				tris[i * 6] = v; tris[i * 6 + 1] = v + 1; tris[i * 6 + 2] = v + 2;
				tris[i * 6 + 3] = v + 1; tris[i * 6 + 4] = v + 3; tris[i * 6 + 5] = v + 2;
			}
			_mesh.Clear();
			_mesh.vertices = verts;
			_mesh.uv = uv;
			_mesh.triangles = tris;
			_mesh.RecalculateBounds();
			// Color and fade through the material (same properties the light shafts use).
			Color c = _color;
			c.a = fade;
			_mat.SetColor("_Color", c);
			_mat.SetColor("_TintColor", c);
		}

		private static void Sparks(Transform parent, Color body, Color core)
		{
			var go = new GameObject("LLA_SlashSparks");
			go.transform.SetParent(parent, false);
			var ps = go.AddComponent<ParticleSystem>();
			ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
			ParticleSystem.MainModule main = ps.main;
			main.duration = 0.3f;
			main.loop = false;
			main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
			main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4.5f);
			main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.12f);
			main.startColor = new ParticleSystem.MinMaxGradient(body, core);
			main.simulationSpace = ParticleSystemSimulationSpace.World;
			main.gravityModifier = 0.3f;
			ParticleSystem.EmissionModule em = ps.emission;
			em.rateOverTime = 0f;
			em.SetBursts(new[] { new ParticleSystem.Burst(0.05f, 24) });
			ParticleSystem.ShapeModule shape = ps.shape;
			shape.shapeType = ParticleSystemShapeType.Circle;
			shape.radius = Radius;
			shape.arc = EndAngle - StartAngle;
			shape.rotation = new Vector3(90f, 0f, 90f + StartAngle);
			shape.radiusThickness = 0f;
			var r = go.GetComponent<ParticleSystemRenderer>();
			r.material = LightShaft.GetSparkleMaterial();
			r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			ps.Play();
		}

		public static Texture2D StreakTexture() => Texture();

		private static readonly System.Collections.Generic.Dictionary<string, Mesh> ArcCache = new System.Collections.Generic.Dictionary<string, Mesh>();

		/// <summary>A flat arc (tail to head along u), for the whirlwind's spinning streaks. Cached by shape.</summary>
		public static Mesh ArcMesh(float radius, float thickness, float degrees)
		{
			string key = $"{radius:0.00}|{thickness:0.00}|{degrees:0}";
			if (ArcCache.TryGetValue(key, out Mesh cached) && cached != null) return cached;
			const int seg = 24;
			var verts = new Vector3[(seg + 1) * 2];
			var uv = new Vector2[(seg + 1) * 2];
			for (int i = 0; i <= seg; i++)
			{
				float k = i / (float)seg;
				float a = Mathf.Lerp(-degrees / 2f, degrees / 2f, k) * Mathf.Deg2Rad;
				float th = Mathf.Lerp(thickness * 0.2f, thickness, k);
				var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
				// Tilted slightly up so the ribbon shows from the side as well as from above.
				verts[i * 2] = dir * (radius - th) - Vector3.up * th * 0.6f;
				verts[i * 2 + 1] = dir * radius + Vector3.up * th * 0.6f;
				uv[i * 2] = new Vector2(k, 0f);
				uv[i * 2 + 1] = new Vector2(k, 1f);
			}
			var tris = new int[seg * 6];
			for (int i = 0; i < seg; i++)
			{
				int v = i * 2;
				tris[i * 6] = v; tris[i * 6 + 1] = v + 1; tris[i * 6 + 2] = v + 2;
				tris[i * 6 + 3] = v + 1; tris[i * 6 + 4] = v + 3; tris[i * 6 + 5] = v + 2;
			}
			var mesh = new Mesh { vertices = verts, uv = uv, triangles = tris };
			mesh.RecalculateBounds();
			ArcCache[key] = mesh;
			return mesh;
		}

		/// <summary>
		/// u runs tail (0) to head (1), v runs inner edge (0) to outer edge (1): bright and white-hot along the
		/// outer edge near the head, fading to nothing toward the tail and the inner edge.
		/// </summary>
		private static Texture2D Texture()
		{
			if (_texture != null) return _texture;
			const int W = 64, H = 32;
			_texture = new Texture2D(W, H, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
			var px = new Color32[W * H];
			for (int x = 0; x < W; x++)
			{
				float u = (x + 0.5f) / W;
				float along = u * u;                                  // fades out toward the tail
				for (int y = 0; y < H; y++)
				{
					float v = (y + 0.5f) / H;
					float across = Mathf.Clamp01(v / 0.85f);
					across = across * across;
					if (v > 0.9f) across *= 0.5f + 0.5f * Mathf.Clamp01((1f - v) / 0.1f); // soft outer rim
					float hot = Mathf.Clamp01((v - 0.6f) / 0.3f) * u;   // white-hot edge near the head
					byte w = (byte)(Mathf.Lerp(170f, 255f, hot));
					px[y * W + x] = new Color32(w, w, 255, (byte)(Mathf.Clamp01(along * across * 1.3f) * 255f));
				}
			}
			_texture.SetPixels32(px);
			_texture.Apply();
			return _texture;
		}
	}

	/// <summary>
	/// Windfury's whirlwind: a stack of glowing arcs spinning around the player at different heights, speeds
	/// and tilts, wider toward the top, so together they read as a swirling column of wind streaks. Built from the
	/// same kind of mesh and material as the slash (which renders reliably). Follows the player for about a second.
	/// One per proc.
	/// </summary>
	public class TornadoFx : MonoBehaviour
	{
		private const float Life = 1.1f;
		private const int Rings = 9;
		private static float _lastTime;
		private static Vector3 _lastPos;

		private Transform[] _arcs;
		private float[] _speeds;
		private Material _mat;
		private float _born;
		private Color _color;

		public static void Play(Vector3 chestPos)
		{
			if (Time.time - _lastTime < 0.8f && (chestPos - _lastPos).sqrMagnitude < 9f) return;
			_lastTime = Time.time;
			_lastPos = chestPos;

			Player owner = Player.GetClosestPlayer(chestPos, 3f);
			Plugin.Debug($"Whirlwind at {chestPos}, around {(owner != null ? owner.GetPlayerName() : "nobody (left in place)")}");
			var root = new GameObject("LLA_Tornado");
			if (owner != null)
			{
				root.transform.SetParent(owner.transform, false);
				root.transform.localPosition = Vector3.zero;
				root.transform.localRotation = Quaternion.identity;
			}
			else root.transform.position = chestPos - Vector3.up * 1.1f;

			var fx = root.AddComponent<TornadoFx>();
			fx._color = new Color(0.35f, 1f, 0.9f);
			fx._mat = new Material(LightShaft.GetMaterial()) { mainTexture = SlashFx.StreakTexture(), renderQueue = 3100 };
			fx._born = Time.time;
			fx._arcs = new Transform[Rings];
			fx._speeds = new float[Rings];
			for (int i = 0; i < Rings; i++)
			{
				float h = Mathf.Lerp(0.15f, 2.5f, i / (float)(Rings - 1));
				float radius = Mathf.Lerp(0.75f, 1.35f, i / (float)(Rings - 1)) + Random.Range(-0.08f, 0.08f);
				var arc = new GameObject("Arc" + i);
				arc.transform.SetParent(root.transform, false);
				arc.transform.localPosition = new Vector3(0f, h, 0f);
				arc.transform.localRotation = Quaternion.Euler(Random.Range(-8f, 8f), Random.Range(0f, 360f), Random.Range(-8f, 8f));
				var mf = arc.AddComponent<MeshFilter>();
				mf.sharedMesh = SlashFx.ArcMesh(radius, Random.Range(0.06f, 0.14f), Random.Range(140f, 220f));
				var mr = arc.AddComponent<MeshRenderer>();
				mr.sharedMaterial = fx._mat;
				mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				mr.receiveShadows = false;
				fx._arcs[i] = arc.transform;
				fx._speeds[i] = Random.Range(620f, 980f) * (Random.value < 0.85f ? 1f : -1f); // a few counter-spin
			}
			Glints(root.transform);
		}

		private void Update()
		{
			float age = Time.time - _born;
			if (age > Life) { Destroy(_mat); Destroy(gameObject); return; }
			for (int i = 0; i < _arcs.Length; i++)
				_arcs[i].Rotate(0f, _speeds[i] * Time.deltaTime, 0f, Space.Self);
			// Quick swell in, hold, fade out.
			float a = Mathf.Clamp01(age / 0.12f) * Mathf.Clamp01((Life - age) / 0.35f);
			Color c = _color;
			c.a = a;
			_mat.SetColor("_Color", c);
			_mat.SetColor("_TintColor", c);
		}

		/// <summary>Glints kicked up around the feet (the same sparkle material as the light shafts).</summary>
		private static void Glints(Transform parent)
		{
			var go = new GameObject("LLA_TornadoGlints");
			go.transform.SetParent(parent, false);
			var ps = go.AddComponent<ParticleSystem>();
			ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
			var main = ps.main;
			main.duration = 0.6f;
			main.loop = false;
			main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.0f);
			main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, 2.5f);
			main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.1f);
			main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.35f, 1f, 0.9f), new Color(0.85f, 1f, 0.98f));
			main.gravityModifier = 0.35f;
			main.simulationSpace = ParticleSystemSimulationSpace.World;
			var em = ps.emission;
			em.rateOverTime = 0f;
			em.SetBursts(new[] { new ParticleSystem.Burst(0f, 40), new ParticleSystem.Burst(0.3f, 25) });
			var shape = ps.shape;
			shape.shapeType = ParticleSystemShapeType.Circle;
			shape.radius = 0.9f;
			shape.rotation = new Vector3(-90f, 0f, 0f);
			var r = go.GetComponent<ParticleSystemRenderer>();
			r.material = LightShaft.GetSparkleMaterial();
			r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			ps.Play();
		}
	}
}
