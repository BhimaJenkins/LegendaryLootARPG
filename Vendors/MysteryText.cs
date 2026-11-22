using System.Text;
using HarmonyLib;
using LegendaryLoot.Core;
using TMPro;
using UnityEngine;

namespace LegendaryLoot.Vendors
{
	/// <summary>
	/// The Mystery item's name in the shop list: a gradient through every tier color, Uncommon green to Unique
	/// gold and back, sliding slowly along the letters.
	/// </summary>
	public class MysteryText : MonoBehaviour
	{
		private static readonly Tier[] Stops = { Tier.Uncommon, Tier.Magic, Tier.Rare, Tier.Epic, Tier.Legendary, Tier.Unique };
		private const float Speed = 0.35f;   // gradient lengths per second
		private const float Spread = 0.06f;  // gradient length per letter

		public string Plain;
		private TMP_Text _text;
		private readonly StringBuilder _sb = new StringBuilder();

		private void Awake() => _text = GetComponent<TMP_Text>();

		private void Update()
		{
			if (_text == null || string.IsNullOrEmpty(Plain)) return;
			_sb.Clear();
			float t0 = Time.unscaledTime * Speed;
			for (int i = 0; i < Plain.Length; i++)
			{
				char c = Plain[i];
				if (c == ' ') { _sb.Append(c); continue; }
				Color col = Sample(t0 - i * Spread);
				_sb.Append("<color=#").Append(ColorUtility.ToHtmlStringRGB(col)).Append('>').Append(c).Append("</color>");
			}
			_text.text = _sb.ToString();
		}

		/// <summary>Ping-pong through the tier colors: t in [0,1) is green to gold, [1,2) gold back to green.</summary>
		private static Color Sample(float t)
		{
			float p = Mathf.PingPong(t, 1f) * (Stops.Length - 1);
			int i = Mathf.Min(Mathf.FloorToInt(p), Stops.Length - 2);
			return Color.Lerp(Tiers.GetColor(Stops[i]), Tiers.GetColor(Stops[i + 1]), p - i);
		}

		/// <summary>After the shop list is built, put the gradient on the Mystery row.</summary>
		[HarmonyPatch(typeof(StoreGui), nameof(StoreGui.FillList))]
		private static class Patch_FillList
		{
			private static void Postfix(StoreGui __instance)
			{
				Trader.TradeItem mystery = Shop.MysteryEntry;
				if (mystery == null || __instance.m_trader == null) return;
				int index = __instance.m_trader.GetAvailableItems().IndexOf(mystery);
				if (index < 0 || index >= __instance.m_itemList.Count) return;
				Transform name = __instance.m_itemList[index].transform.Find("name");
				TMP_Text text = name != null ? name.GetComponent<TMP_Text>() : null;
				if (text == null) return;
				MysteryText anim = text.GetComponent<MysteryText>() ?? text.gameObject.AddComponent<MysteryText>();
				anim.Plain = mystery.m_name;
				if (Shop.IsSoldOut(mystery))
				{
					// Sold out: plain grey name and a dimmed icon until it's back.
					anim.enabled = false;
					text.text = mystery.m_name;
					text.color = Color.grey;
					Transform icon = __instance.m_itemList[index].transform.Find("icon");
					UnityEngine.UI.Image img = icon != null ? icon.GetComponent<UnityEngine.UI.Image>() : null;
					if (img != null) img.color = new Color(0.45f, 0.45f, 0.45f, 0.8f);
				}
				else anim.enabled = true;
			}
		}
	}
}
