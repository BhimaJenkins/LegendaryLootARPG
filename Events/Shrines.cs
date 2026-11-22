using System;
using System.Collections.Generic;
using LegendaryLoot.Data;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LegendaryLoot.Events
{
	/// <summary>
	/// Shrine events: a glowing runestone in its buff's color (red Fury, blue Swiftness, gold Fortune) with a pulsing
	/// light and drifting effects. "Touch the shrine" gives that buff for a few minutes; then the glow fades and the
	/// shrine goes with the event. Each game draws its own copy (nothing is saved), so every player sees the same
	/// shrine and whoever touches it first uses it up.
	/// </summary>
	public static class Shrines
	{
		private static readonly Dictionary<int, ShrineStone> Stones = new Dictionary<int, ShrineStone>();
		private static readonly HashSet<int> Greeted = new HashSet<int>();
		private static GameObject _holder;

		private static EventFile Data => DataStore.Events;

		internal static EventType Type => Data.Types.TryGetValue("Shrine", out EventType t) ? t : null;

		/// <summary>The same buff on every game: picked from the event's id.</summary>
		internal static ShrineBuff BuffFor(int id)
		{
			EventType t = Type;
			if (t == null || t.Buffs == null || t.Buffs.Count == 0) return null;
			return t.Buffs[(int)((uint)(id * 2654435761u) % (uint)t.Buffs.Count)];
		}

		public static void Tick(Player p, IEnumerable<WorldEvent> events)
		{
			var live = new HashSet<int>();
			foreach (WorldEvent ev in events)
			{
				if (ev.Type != "Shrine") continue;
				live.Add(ev.Id);
				float d = WorldEvents.Flat(p.transform.position, ev.Pos);
				if (d < 160f && !Stones.ContainsKey(ev.Id))
				{
					ShrineStone stone = Build(ev);
					if (stone != null) Stones[ev.Id] = stone;
				}
				if (d < Data.ActivateRange && Greeted.Add(ev.Id))
				{
					string start = Type != null && !string.IsNullOrEmpty(Type.Start) ? Type.Start : "A shrine hums with power.";
					p.Message(MessageHud.MessageType.Center, start);
				}
			}
			foreach (int id in new List<int>(Stones.Keys))
			{
				if (live.Contains(id)) continue;
				if (Stones[id] != null) Stones[id].FadeAway();
				Stones.Remove(id);
			}
		}

		private static ShrineStone Build(WorldEvent ev)
		{
			ShrineBuff buff = BuffFor(ev.Id);
			if (buff == null || ZNetScene.instance == null) return null;
			GameObject prefab = null;
			foreach (string n in new[] { "stone_pillar", "piece_groundtorch_mist", "piece_groundtorch" })
				if ((prefab = ZNetScene.instance.GetPrefab(n)) != null) break;
			if (prefab == null) return null;
			if (_holder == null) { _holder = new GameObject("LLA_ShrineHolder"); _holder.SetActive(false); UnityEngine.Object.DontDestroyOnLoad(_holder); }

			// A local copy only: no network object, no scripts, no sounds; colliders stay so it can be hovered.
			bool was = ZNetView.m_forceDisableInit;
			ZNetView.m_forceDisableInit = true;
			GameObject go;
			try { go = UnityEngine.Object.Instantiate(prefab, _holder.transform); }
			finally { ZNetView.m_forceDisableInit = was; }
			foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(mb);
			foreach (AudioSource au in go.GetComponentsInChildren<AudioSource>(true)) UnityEngine.Object.DestroyImmediate(au);
			go.name = "LLA_Shrine_" + ev.Id;
			Vector3 pos = ev.Pos;
			if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(pos, out float h, 1000)) pos.y = h;
			go.transform.position = pos;
			go.transform.rotation = Quaternion.Euler(0f, (ev.Id * 47) % 360, 0f);
			go.transform.localScale = new Vector3(0.9f, 0.75f, 0.6f);   // squat, like a runestone
			go.transform.SetParent(null, true);
			ShrineStone stone = go.AddComponent<ShrineStone>();
			stone.Setup(ev.Id, buff);
			return stone;
		}

		/// <summary>Applies the buff on the local player: the stat for the mod, and an icon with a timer on the HUD.</summary>
		internal static void Grant(Player p, ShrineBuff buff)
		{
			EventType t = Type;
			float minutes = t != null && t.Minutes > 0f ? t.Minutes : 5f;
			Powers.Buffs.Add(buff.Stat, buff.Value, minutes * 60f, 1);

			var se = ScriptableObject.CreateInstance<StatusEffect>();
			se.name = "LLA_" + buff.Name.Replace(" ", "");
			se.m_name = buff.Name;
			se.m_tooltip = buff.Text;
			se.m_ttl = minutes * 60f;
			GameObject icon = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(buff.Icon) : null;
			ItemDrop drop = icon != null ? icon.GetComponent<ItemDrop>() : null;
			if (drop != null && drop.m_itemData.m_shared.m_icons.Length > 0) se.m_icon = drop.m_itemData.m_shared.m_icons[0];
			p.GetSEMan().AddStatusEffect(se, true, 0, 0f, 0);
			p.Message(MessageHud.MessageType.Center, $"{buff.Name}: {buff.Text} for {minutes:0} minutes.");
		}
	}

	/// <summary>One shrine: glow, pulsing light, drifting effects; hover and touch.</summary>
	public class ShrineStone : MonoBehaviour, Hoverable, Interactable
	{
		private int _id;
		private ShrineBuff _buff;
		private Light _light;
		private Color _color;
		private float _nextFx, _fadeStart = -1f;
		private bool _used;

		public void Setup(int id, ShrineBuff buff)
		{
			_id = id;
			_buff = buff;
			if (!ColorUtility.TryParseHtmlString(buff.Color ?? "#FFFFFF", out _color)) _color = Color.white;
			var glow = new GameObject("Glow");
			glow.transform.SetParent(transform, false);
			glow.transform.localPosition = Vector3.up * 1.6f;
			_light = glow.AddComponent<Light>();
			_light.type = LightType.Point;
			_light.color = _color;
			_light.range = 9f;
			_light.intensity = 2.5f;
			_light.shadows = LightShadows.None;
			// The stone's own surfaces pick up a hint of the color, like glowing runes.
			foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
				foreach (Material m in r.materials)
				{
					if (!m.HasProperty("_EmissionColor")) continue;
					m.EnableKeyword("_EMISSION");
					m.SetColor("_EmissionColor", _color * 0.6f);
				}
			_nextFx = Time.time + Random.value;
		}

		private void Update()
		{
			if (_fadeStart >= 0f)
			{
				float k = 1f - (Time.time - _fadeStart) / 1.5f;
				if (_light != null) _light.intensity = 2.5f * Mathf.Max(0f, k);
				if (k <= 0f) Destroy(gameObject);
				return;
			}
			if (_light != null) _light.intensity = 2.1f + 0.7f * Mathf.Sin(Time.time * 2.2f);
			if (Time.time >= _nextFx && !string.IsNullOrEmpty(_buff.Fx))
			{
				_nextFx = Time.time + 2.5f;
				PlayFx(_buff.Fx, transform.position + Vector3.up * 0.4f);
			}
		}

		/// <summary>A silent, script-free copy of a game effect, gone after 4 s.</summary>
		private static void PlayFx(string name, Vector3 at)
		{
			GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
			if (prefab == null) return;
			var holder = new GameObject("LLA_FxHolder");
			holder.SetActive(false);
			bool was = ZNetView.m_forceDisableInit;
			ZNetView.m_forceDisableInit = true;
			GameObject fx;
			try { fx = Instantiate(prefab, holder.transform); }
			finally { ZNetView.m_forceDisableInit = was; }
			foreach (MonoBehaviour mb in fx.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
			foreach (Collider c in fx.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
			foreach (AudioSource au in fx.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(au);
			fx.transform.position = at;
			fx.transform.SetParent(null, true);
			Destroy(holder);
			Destroy(fx, 4f);
		}

		public void FadeAway()
		{
			if (_fadeStart < 0f) _fadeStart = Time.time;
		}

		public string GetHoverName() => _buff?.Name ?? "Shrine";

		public float GetHoverOffset() => 1.2f;

		public string GetHoverText()
		{
			if (_used || _fadeStart >= 0f) return $"<color={_buff?.Color ?? "#FFFFFF"}>{GetHoverName()}</color>\n(its power is spent)";
			return Localization.instance.Localize($"<color={_buff.Color}>{GetHoverName()}</color>\n{_buff.Text}\n[<color=yellow><b>$KEY_Use</b></color>] Touch the shrine");
		}

		public bool Interact(Humanoid user, bool hold, bool alt)
		{
			if (hold || _used || _fadeStart >= 0f || !(user is Player p) || p != Player.m_localPlayer) return false;
			_used = true;
			Shrines.Grant(p, _buff);
			WorldEvents.End(_id, 1);
			FadeAway();
			return true;
		}

		public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
	}
}
