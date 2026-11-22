using UnityEngine;

namespace LegendaryLoot.Powers
{
	/// <summary>
	/// Gives a summon a different body on every player's game, without making a new creature: the summon keeps
	/// its own AI and attacks, its own body is hidden, and a copy of another creature's body rides along.
	///  - raven: a Bat (flies and bites) wearing a Seagull's body, tinted near-black.
	/// Purely visual, so nothing extra has to be sent between players.
	/// </summary>
	public static class SummonLooks
	{
		private class Applied : MonoBehaviour { public string Look; }

		public static void Apply(Character c)
		{
			if (c == null) return;
			string look = Summons.LookOf(c);
			if (string.IsNullOrEmpty(look)) return;
			Applied done = c.GetComponent<Applied>();
			if (done != null && done.Look == look) return;
			bool ok = false;
			switch (look)
			{
				case "raven": ok = Wear(c, "Seagal", new Color(0.06f, 0.06f, 0.08f)); break;
			}
			if (ok) (done ?? c.gameObject.AddComponent<Applied>()).Look = look;
		}

		/// <summary>Hides the creature's own body and attaches a tinted copy of another creature's body.</summary>
		private static bool Wear(Character c, string donorName, Color tint)
		{
			GameObject donor = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(donorName) : null;
			if (donor == null)
			{
				Plugin.Debug($"Summon look: no '{donorName}' to borrow a body from.");
				return false;
			}
			// The donor's body: the part with its animator (never the whole prefab, which would be a new network object).
			Animator donorAnim = donor.GetComponentInChildren<Animator>(true);
			Transform body = donorAnim != null ? donorAnim.transform : null;
			if (body == null || body == donor.transform)
			{
				Renderer r = donor.GetComponentInChildren<Renderer>(true);
				body = r != null ? r.transform : null;
			}
			if (body == null || body == donor.transform) return false;

			// A raven shouldn't screech like a bat: mute its spotted-you and idle sounds (per creature, safe).
			BaseAI ai = c.GetComponent<BaseAI>();
			if (ai != null)
			{
				ai.m_alertedEffects = new EffectList();
				ai.m_idleSound = new EffectList();
			}

			// Hide our own body.
			foreach (Renderer r in c.GetComponentsInChildren<Renderer>(true)) r.enabled = false;

			GameObject copy = Object.Instantiate(body.gameObject, c.transform, false);
			copy.name = "LLA_Look_" + donorName;
			copy.transform.localPosition = Vector3.zero;
			copy.transform.localRotation = Quaternion.identity;
			copy.AddComponent<FaceTravel>();
			// Only the visuals: no colliders, no scripts that might act on their own.
			foreach (Collider col in copy.GetComponentsInChildren<Collider>(true)) Object.Destroy(col);
			foreach (MonoBehaviour mb in copy.GetComponentsInChildren<MonoBehaviour>(true))
				if (!(mb is FaceTravel)) Object.Destroy(mb);

			var block = new MaterialPropertyBlock();
			foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
			{
				r.enabled = true;
				r.GetPropertyBlock(block);
				block.SetColor("_Color", tint);
				block.SetColor("_EmissionColor", Color.black);
				r.SetPropertyBlock(block);
			}

			// Keep the wings flapping: switch on any "fly" flag the bird's animator has.
			Animator anim = copy.GetComponentInChildren<Animator>(true);
			if (anim != null)
			{
				anim.enabled = true;
				foreach (AnimatorControllerParameter p in anim.parameters)
				{
					if (p.type != AnimatorControllerParameterType.Bool) continue;
					string n = p.name.ToLowerInvariant();
					if (n.Contains("fly")) anim.SetBool(p.name, true);
					else if (n.Contains("land") || n.Contains("idle") || n.Contains("sit")) anim.SetBool(p.name, false);
				}
			}
			return true;
		}
	}

	/// <summary>
	/// Turns the borrowed body to face where it's actually going. Bats often fly backwards to back off after a
	/// bite, and a bird flying tail-first looks wrong. Uses how the creature moves on screen, so it works on
	/// every player's game; when it hangs in place it faces the way the creature faces.
	/// </summary>
	public class FaceTravel : MonoBehaviour
	{
		private Vector3 _last;
		private Vector3 _smoothed;
		private bool _started;

		private void LateUpdate()
		{
			Transform owner = transform.parent;
			if (owner == null) return;
			Vector3 pos = owner.position;
			if (!_started) { _last = pos; _started = true; }
			float dt = Mathf.Max(Time.deltaTime, 0.0001f);
			Vector3 v = (pos - _last) / dt;
			_last = pos;
			v.y = 0f;
			_smoothed = Vector3.Lerp(_smoothed, v, 1f - Mathf.Exp(-6f * dt));
			Vector3 dir = _smoothed.sqrMagnitude > 0.6f ? _smoothed : owner.forward;
			dir.y = 0f;
			if (dir.sqrMagnitude < 0.0001f) return;
			// The seagull model is built facing backwards relative to the bat, hence the half turn.
			Quaternion want = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0f, 180f, 0f);
			transform.rotation = Quaternion.Slerp(transform.rotation, want, 1f - Mathf.Exp(-10f * dt));
		}
	}

	/// <summary>
	/// Raven summons caw instead of screeching: their attack's own start and trigger sounds are dropped, and
	/// ravenattack.ogg (from the audio folder) plays for every player nearby.
	/// </summary>
	public static class RavenSound
	{
		public const string File = "ravenattack.ogg";
		private const string Rpc = "LLA_Sound";

		[HarmonyLib.HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
		private static class Patch_Attack
		{
			private static void Prefix(Attack __instance, Humanoid character)
			{
				if (character == null || Summons.LookOf(character) != "raven") return;
				// This attack is a per-swing copy, so emptying its sound lists doesn't touch other bats.
				__instance.m_startEffect = new EffectList();
				__instance.m_triggerEffect = new EffectList();
				Broadcast(File, character.transform.position);
			}
		}

		/// <summary>Plays a sound from the audio folder at a spot, on every player's game.</summary>
		public static void Broadcast(string file, Vector3 pos)
		{
			if (ZRoutedRpc.instance == null) { SoundLibrary.PlayAt(file, pos); return; }
			ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, Rpc, file, pos);
		}

		[HarmonyLib.HarmonyPatch(typeof(Game), nameof(Game.Start))]
		private static class Patch_Register
		{
			private static void Postfix()
			{
				ZRoutedRpc.instance.Register<string, Vector3>(Rpc, (sender, file, pos) => SoundLibrary.PlayAt(file, pos));
			}
		}
	}
}
