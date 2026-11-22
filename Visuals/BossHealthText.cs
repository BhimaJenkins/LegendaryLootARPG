using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace LegendaryLoot
{
	/// <summary>Bosses: "current / max" health written on the boss health bar.</summary>
	public static class BossHealthText
	{
		private const string LabelName = "LLA_BossHealth";

		[HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.UpdateHuds))]
		private static class Patch_Update
		{
			private static void Postfix(EnemyHud __instance)
			{
				if (!Plugin.BossHealthNumbers.Value) return;
				foreach (KeyValuePair<Character, EnemyHud.HudData> kv in __instance.m_huds)
				{
					Character c = kv.Key;
					EnemyHud.HudData hud = kv.Value;
					if (c == null || hud == null || hud.m_gui == null || !c.IsBoss() || hud.m_healthSlow == null) continue;
					TextMeshProUGUI label = Label(hud);
					if (label == null) continue;
					float hp = Mathf.Max(0f, c.GetHealth());
					float max = Mathf.Max(1f, c.GetMaxHealth());
					label.text = $"{Mathf.CeilToInt(hp):N0} / {Mathf.CeilToInt(max):N0}";
				}
			}
		}

		private static TextMeshProUGUI Label(EnemyHud.HudData hud)
		{
			Transform bar = hud.m_healthSlow.transform.parent != null ? hud.m_healthSlow.transform.parent : hud.m_healthSlow.transform;
			Transform found = bar.Find(LabelName);
			if (found != null) return found.GetComponent<TextMeshProUGUI>();
			if (hud.m_name == null) return null;

			GameObject go = Object.Instantiate(hud.m_name.gameObject, bar, false);
			go.name = LabelName;
			foreach (Component comp in go.GetComponents<Component>())
				if (comp is MonoBehaviour mb && !(mb is TextMeshProUGUI)) Object.Destroy(mb); // no Localize etc.
			var rt = (RectTransform)go.transform;
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.pivot = new Vector2(0.5f, 0.5f);
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;
			rt.SetAsLastSibling();
			var tmp = go.GetComponent<TextMeshProUGUI>();
			tmp.alignment = TextAlignmentOptions.Center;
			tmp.enableAutoSizing = false;
			tmp.fontSize = Mathf.Max(12f, hud.m_name.fontSize * 0.6f);
			tmp.color = Color.white;
			tmp.outlineWidth = 0.25f;
			tmp.outlineColor = new Color32(0, 0, 0, 255);
			tmp.enableWordWrapping = false;
			tmp.overflowMode = TextOverflowModes.Overflow;
			tmp.raycastTarget = false;
			return tmp;
		}
	}
}
