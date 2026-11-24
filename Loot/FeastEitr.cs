using System.Collections.Generic;
using HarmonyLib;
using LegendaryLoot.Data;
using UnityEngine;

namespace LegendaryLoot.Loot
{
	/// <summary>
	/// loot.yml feastEitr: gives feasts Eitr (for gear mods that bring magic before the Mistlands). The game's own
	/// value is kept, so a feast taken out of the list (or a reload) goes back to it.
	/// </summary>
	public static class FeastEitr
	{
		private static readonly Dictionary<string, float> Original = new Dictionary<string, float>();

		static FeastEitr()
		{
			DataStore.Reloaded += Apply;
		}

		[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.Awake))]
		private static class Patch_Awake
		{
			[HarmonyPriority(Priority.Low)]
			private static void Postfix() => Apply();
		}

		[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
		private static class Patch_Copy
		{
			[HarmonyPriority(Priority.Low)]
			private static void Postfix() => Apply();
		}

		public static void Apply()
		{
			if (ObjectDB.instance == null || DataStore.Loot?.FeastEitr == null) return;
			// Put back anything no longer listed.
			foreach (var kv in Original)
				if (!DataStore.Loot.FeastEitr.ContainsKey(kv.Key) && Shared(kv.Key) is ItemDrop.ItemData.SharedData s) s.m_foodEitr = kv.Value;
			foreach (var kv in DataStore.Loot.FeastEitr)
			{
				ItemDrop.ItemData.SharedData s = Shared(kv.Key);
				if (s == null) { Plugin.Debug($"feastEitr: no item called {kv.Key}."); continue; }
				if (!Original.ContainsKey(kv.Key)) Original[kv.Key] = s.m_foodEitr;
				s.m_foodEitr = Mathf.Max(0f, kv.Value);
			}
		}

		private static ItemDrop.ItemData.SharedData Shared(string name)
		{
			GameObject go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(name) : null;
			ItemDrop drop = go != null ? go.GetComponent<ItemDrop>() : null;
			return drop?.m_itemData?.m_shared;
		}
	}
}
