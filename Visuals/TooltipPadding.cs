using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LegendaryLoot
{
	/// <summary>
	/// Breathing room inside item tooltips. Margins go on the text elements themselves: TextMeshPro includes
	/// its margins in the size it asks for, so the tooltip box grows to fit instead of the text running into
	/// the frame. Other tooltips (buttons, skills) reuse the same box, so their original margins are put back.
	/// </summary>
	public static class TooltipPadding
	{
		/// <summary>Clear space between the text and the inside of the frame, in pixels.</summary>
		public const float Breathing = 20f;
		/// <summary>The frame's bronze band and inner shadow, which text shouldn't overlap.</summary>
		public const float FrameBand = 10f;
		public const float Edge = Breathing + FrameBand;
		private const float BetweenTitleAndBody = 4f;

		private class Original : MonoBehaviour
		{
			public readonly Dictionary<TMP_Text, Vector4> Margins = new Dictionary<TMP_Text, Vector4>();
			public bool Applied;
		}

		public static void Apply(GameObject root, GameObject box, bool isItem)
		{
			if (root == null) return;
			bool want = isItem && Plugin.TooltipFrame.Value;
			var orig = root.GetComponent<Original>();
			if (orig == null)
			{
				if (!want) return;
				orig = root.AddComponent<Original>();
			}

			TMP_Text topic = Find(root, "Topic");
			TMP_Text text = Find(root, "Text");
			foreach (TMP_Text t in new[] { topic, text })
			{
				if (t == null) continue;
				if (!orig.Margins.ContainsKey(t)) orig.Margins[t] = t.margin;
				Vector4 m = orig.Margins[t]; // x = left, y = top, z = right, w = bottom
				if (want)
				{
					bool hasTopic = topic != null && topic.gameObject.activeInHierarchy && !string.IsNullOrEmpty(topic.text);
					bool isTop = t == topic || !hasTopic;
					bool isBottom = t == text || text == null;
					m = new Vector4(
						m.x + Edge,
						isTop ? m.y + Edge : m.y + BetweenTitleAndBody,
						m.z + Edge,
						isBottom ? m.w + Edge : m.w);
				}
				if (t.margin != m) t.margin = m;
			}
			orig.Applied = want;
			if (box != null) LayoutRebuilder.MarkLayoutForRebuild((RectTransform)box.transform);
		}

		private static TMP_Text Find(GameObject root, string name)
		{
			Transform t = Utils.FindChild(root.transform, name);
			return t != null ? t.GetComponent<TMP_Text>() : null;
		}
	}
}
