using System;
using System.Collections.Generic;
using HarmonyLib;
using LegendaryLoot.Data;
using LegendaryLoot.Loot;
using UnityEngine;
using Random = UnityEngine.Random;

namespace LegendaryLoot.Vendors
{
	/// <summary>
	/// A stall's vendor: turns to face you, greets you, talks now and then (with voice lines from the audio folder,
	/// e.g. boogie1.ogg, boogie2.ogg), says a welcome the first time you trade, and sells what you know how to make.
	/// Runs in place of the vanilla Trader's own Update, which expects Haldor's animations and head-turning.
	/// </summary>
	public class StallVendor : MonoBehaviour
	{
		public string Key;
		private Trader _trader;
		private ZNetView _nview;
		private bool _greeted;

		public StallDef Def => Key != null && DataStore.Vendors.Stalls.TryGetValue(Key, out StallDef d) ? d : null;

		private void Awake()
		{
			_trader = GetComponent<Trader>();
			_nview = GetComponentInParent<ZNetView>();
		}

		/// <summary>False for the building ghost and for copies that aren't real network objects.</summary>
		public bool Live => _nview != null && _nview.IsValid();

		private void Update()
		{
			if (!Live) return;
			Player p = Player.GetClosestPlayer(transform.position, 12f);
			if (p == null)
			{
				_greeted = false;
				return;
			}
			// Turn toward whoever is closest, a little at a time.
			Vector3 to = p.transform.position - transform.position;
			to.y = 0f;
			if (to.sqrMagnitude > 0.01f)
				transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to), 120f * Time.deltaTime);
			if (!_greeted && p == Player.m_localPlayer && to.magnitude < 5f)
			{
				_greeted = true;
				Voice();
			}
		}

		public void Voice()
		{
			StallDef d = Def;
			if (d == null || string.IsNullOrEmpty(d.Voice)) return;
			List<string> clips = SoundLibrary.FindPrefix(d.Voice);
			if (clips.Count > 0) SoundLibrary.PlayAt(clips[Random.Range(0, clips.Count)], transform.position + Vector3.up, 1f, 25f, dry: true);
		}

		public static StallVendor Of(Trader t) => t != null ? t.GetComponent<StallVendor>() : null;

		// ---------- the vanilla Trader, steered ----------

		[HarmonyPatch(typeof(Trader), nameof(Trader.Update))]
		private static class Patch_Update
		{
			private static bool Prefix(Trader __instance) => Of(__instance) == null;
		}

		[HarmonyPatch(typeof(Trader), nameof(Trader.RandomTalk))]
		private static class Patch_RandomTalk
		{
			private static bool Prefix(Trader __instance)
			{
				StallVendor v = Of(__instance);
				if (v == null) return true;
				if (!v.Live || StoreGui.IsVisible() || !Player.IsPlayerInRange(__instance.transform.position, __instance.m_greetRange)) return false;
				if (__instance.m_randomTalk.Count > 0)
					__instance.Say(__instance.m_randomTalk[Random.Range(0, __instance.m_randomTalk.Count)], "");
				return false;
			}
		}

		/// <summary>Speech bubble only (vanilla also fires a "Talk" animation these creatures don't have), plus a voice line.</summary>
		[HarmonyPatch(typeof(Trader), nameof(Trader.Say), typeof(string), typeof(string))]
		private static class Patch_Say
		{
			private static bool Prefix(Trader __instance, string text)
			{
				StallVendor v = Of(__instance);
				if (v == null) return true;
				if (!v.Live || Chat.instance == null) return false;
				Chat.instance.SetNpcText(__instance.gameObject, Vector3.up * __instance.m_dialogHeight, 20f, __instance.m_hideDialogDelay, "", text, false);
				v.Voice();
				return false;
			}
		}

		private const string MetKey = "LLA.stallsMet";

		/// <summary>The welcome, the first time each player trades at each kind of stall.</summary>
		[HarmonyPatch(typeof(Trader), nameof(Trader.Interact))]
		private static class Patch_Interact
		{
			private static bool Prefix(Trader __instance) => Of(__instance) == null || Of(__instance).Live;

			private static void Postfix(Trader __instance, Humanoid character)
			{
				StallVendor v = Of(__instance);
				if (v == null || character != Player.m_localPlayer) return;
				StallDef d = v.Def;
				Player p = (Player)character;
				p.m_customData.TryGetValue(MetKey, out string met);
				var set = new HashSet<string>((met ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
				if (d == null || string.IsNullOrEmpty(d.Greeting) || set.Contains(v.Key)) return;
				set.Add(v.Key);
				p.m_customData[MetKey] = string.Join(",", set);
				__instance.Say(d.Greeting, "");
			}
		}

		// ---------- what's for sale ----------

		[HarmonyPatch(typeof(Trader), nameof(Trader.GetAvailableItems))]
		private static class Patch_Items
		{
			private static void Postfix(Trader __instance, ref List<Trader.TradeItem> __result)
			{
				StallVendor v = Of(__instance);
				if (v == null) return;
				__result = StallStock.For(v.Def);
			}
		}
	}

	/// <summary>The goods a stall sells: every item of its kinds whose recipe you've discovered.</summary>
	public static class StallStock
	{
		private static Dictionary<string, List<GameObject>> _kinds;
		private static readonly Dictionary<GameObject, GameObject> MeadBase = new Dictionary<GameObject, GameObject>();
		private static ObjectDB _for;

		private static readonly Dictionary<string, List<Trader.TradeItem>> Cache = new Dictionary<string, List<Trader.TradeItem>>();
		private static string _cacheKey;

		public static List<Trader.TradeItem> For(StallDef def)
		{
			var list = new List<Trader.TradeItem>();
			Player p = Player.m_localPlayer;
			if (def == null || p == null) return list;
			Build();
			string key = def.Name + "|" + p.m_knownRecipes.Count + "|" + DataStore.Vendors.GetHashCode();
			if (_cacheKey == key && Cache.TryGetValue(def.Name, out List<Trader.TradeItem> cached)) return cached;
			foreach (KeyValuePair<string, StallSell> sell in def.Sells)
			{
				if (sell.Value == null || !_kinds.TryGetValue(sell.Key.ToLowerInvariant(), out List<GameObject> items)) continue;
				foreach (GameObject item in items)
				{
					if (!Known(p, item)) continue;
					ItemDrop drop = item.GetComponent<ItemDrop>();
					int step = Mathf.Max(0, BiomeStep(item));
					int price = Mathf.Max(1, Mathf.RoundToInt(sell.Value.Price * VendorTables.AtStep(DataStore.Vendors.BiomeBuyMultiplier, step)));
					list.Add(new Trader.TradeItem
					{
						m_prefab = drop,
						m_stack = Mathf.Clamp(sell.Value.Stack, 1, Mathf.Max(1, drop.m_itemData.m_shared.m_maxStackSize)),
						m_price = price,
						m_name = "",
						m_tooltip = "", // the shop list reads its length; vanilla trade items default to ""
						m_buyKey = "",
						m_incrementKey = "",
						m_requiredGlobalKey = "",
						m_buyPlayerEffects = new EffectList(),
					});
				}
			}
			_cacheKey = key;
			Cache[def.Name] = list;
			return list;
		}

		private static bool Known(Player p, GameObject item)
		{
			GameObject source = MeadBase.TryGetValue(item, out GameObject b) ? b : item; // a mead counts once you know its base
			return p.IsRecipeKnown(source.GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
		}

		private static int BiomeStep(GameObject item)
		{
			int step = ItemPool.GetItemBiomeStep(item);
			if (step < 0 && MeadBase.TryGetValue(item, out GameObject b)) step = ItemPool.GetItemBiomeStep(b);
			return step;
		}

		private static void Build()
		{
			ObjectDB db = ObjectDB.instance;
			if (db == null || (db == _for && _kinds != null)) return;
			_for = db;
			Cache.Clear();
			_cacheKey = null;
			MeadBase.Clear();
			_kinds = new Dictionary<string, List<GameObject>>();
			foreach (string k in new[] { "meads", "dishes", "arrows", "bolts", "bombs", "ballista" }) _kinds[k] = new List<GameObject>();

			// Meads: whatever the fermenter turns a base into.
			GameObject fermenter = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab("fermenter") : null;
			Fermenter f = fermenter != null ? fermenter.GetComponent<Fermenter>() : null;
			if (f != null)
				foreach (Fermenter.ItemConversion c in f.m_conversion)
				{
					if (c?.m_from == null || c.m_to == null) continue;
					MeadBase[c.m_to.gameObject] = c.m_from.gameObject;
					if (!_kinds["meads"].Contains(c.m_to.gameObject)) _kinds["meads"].Add(c.m_to.gameObject);
				}

			foreach (Recipe r in db.m_recipes)
			{
				if (r == null || r.m_item == null || !r.m_enabled) continue;
				GameObject go = r.m_item.gameObject;
				ItemDrop.ItemData.SharedData s = r.m_item.m_itemData.m_shared;
				string name = go.name;
				string station = r.m_craftingStation != null ? r.m_craftingStation.m_name : "";
				if (s.m_itemType == ItemDrop.ItemData.ItemType.Consumable && s.m_food > 0f && station == "$piece_cauldron" &&
				    name.IndexOf("Feast", StringComparison.OrdinalIgnoreCase) < 0 && !MeadBase.ContainsValue(go))
					Add("dishes", go);
				else if (s.m_itemType == ItemDrop.ItemData.ItemType.Ammo && s.m_ammoType == "$ammo_arrows") Add("arrows", go);
				else if (s.m_itemType == ItemDrop.ItemData.ItemType.Ammo && s.m_ammoType == "$ammo_bolts") Add("bolts", go);
				else if (name.StartsWith("Bomb", StringComparison.Ordinal)) Add("bombs", go);
				else if (name.StartsWith("TurretBolt", StringComparison.Ordinal)) Add("ballista", go);
			}
			Plugin.Debug($"Stall goods: {_kinds["meads"].Count} meads, {_kinds["dishes"].Count} dishes, {_kinds["arrows"].Count} arrows, " +
			             $"{_kinds["bolts"].Count} bolts, {_kinds["bombs"].Count} bombs, {_kinds["ballista"].Count} ballista ammo.");
		}

		private static void Add(string kind, GameObject go)
		{
			if (!_kinds[kind].Contains(go)) _kinds[kind].Add(go);
		}
	}
}
