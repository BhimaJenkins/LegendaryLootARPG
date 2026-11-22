using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HarmonyLib;
using LegendaryLoot.Core;
using LegendaryLoot.Data;
using LegendaryLoot.Loot;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace LegendaryLoot.Vendors
{
	/// <summary>
	/// The QUESTS tab (quests.yml): Haldor's BOUNTIES (hunt a starred creature, or clear a camp), Hildir's DELVES
	/// (find a marked chest inside a dungeon) and the Bog Witch's QUESTS (bring trophies).
	///  - Each vendor offers a few each in-game day, from your boss biome or lower. Your active quests are listed
	///    first; the button reads Accept, Claim (done) or Abandon (press twice).
	///  - Bounties and delves are circled on your map. A bounty's creatures appear when you get close; the kill
	///    counts wherever it happens (even if someone else lands the blow). A delve's chest glows once you're inside.
	///  - Quests are personal and saved with your character.
	/// </summary>
	public static class QuestBoard
	{
		public enum Kind { Hunt, Camp, Delve, Trophy }

		public class Quest
		{
			public string Id;
			public Kind Kind;
			public string Vendor;
			public string Title;
			public int Step;
			public bool HasTarget;
			public Vector3 Target, Pin;
			public int Progress, Needed = 1;
			public bool Done;
			public long ChestUser, ChestId; // delve: the chosen chest
			public int Difficulty = -1;     // bounties: index into quests.yml difficulties
			public float LocatingSince = -1f, NextSpawn;
			public bool Locating => !HasTarget && LocatingSince >= 0f;
		}

		private static readonly List<Quest> Active = new List<Quest>();
		private static Player _loadedFor;

		private const string SaveKey = "LLA.quests";
		private const string TakenKey = "LLA.questsTaken";
		private const string ZdoKey = "LLA_Quest";
		private const string LocateRpcPrefix = "LLA|";

		// ---------- vendor kinds ----------

		public static string Label(Kind k) => k == Kind.Hunt || k == Kind.Camp ? "BOUNTY" : k == Kind.Delve ? "DELVE" : "QUEST";

		private static bool Gives(string vendor, Kind k)
		{
			switch (vendor.ToLowerInvariant())
			{
				case "haldor": return k == Kind.Hunt || k == Kind.Camp;
				case "hildir": return k == Kind.Delve;
				case "bogwitch": return k == Kind.Trophy;
				default: return false;
			}
		}

		// ---------- saving ----------

		private static void EnsureLoaded()
		{
			Player p = Player.m_localPlayer;
			if (p == _loadedFor) return;
			_loadedFor = p;
			Active.Clear();
			if (p == null || p.m_customData == null || !p.m_customData.TryGetValue(SaveKey, out string text)) return;
			foreach (string line in text.Split('\n'))
			{
				string[] f = line.Split('|');
				if (f.Length < 14) continue;
				try
				{
					var q = new Quest
					{
						Id = f[0],
						Kind = (Kind)Enum.Parse(typeof(Kind), f[1]),
						Vendor = f[2],
						Title = f[3],
						Step = Int(f[4]),
						HasTarget = f[5] == "1",
						Target = new Vector3(Flt(f[6]), Flt(f[7]), Flt(f[8])),
						Pin = new Vector3(Flt(f[9]), 0f, Flt(f[10])),
						Progress = Int(f[11]),
						Needed = Int(f[12]),
						Done = f[13] == "1",
						ChestUser = f.Length > 15 ? long.Parse(f[14], CultureInfo.InvariantCulture) : 0,
						ChestId = f.Length > 15 ? long.Parse(f[15], CultureInfo.InvariantCulture) : 0,
						Difficulty = f.Length > 16 ? Int(f[16]) : -1,
					};
					if (!q.HasTarget && (q.Kind == Kind.Camp || q.Kind == Kind.Delve)) q.LocatingSince = -1f; // ask again
					Active.Add(q);
				}
				catch (Exception ex)
				{
					Plugin.Log.LogWarning($"Couldn't read a saved quest: {ex.Message}");
				}
			}
		}

		private static void Save()
		{
			Player p = Player.m_localPlayer;
			if (p == null) return;
			var sb = new StringBuilder();
			foreach (Quest q in Active)
			{
				if (sb.Length > 0) sb.Append('\n');
				sb.Append(q.Id).Append('|').Append(q.Kind).Append('|').Append(q.Vendor).Append('|').Append(q.Title.Replace("|", "/")).Append('|')
					.Append(q.Step).Append('|').Append(q.HasTarget ? '1' : '0').Append('|')
					.Append(S(q.Target.x)).Append('|').Append(S(q.Target.y)).Append('|').Append(S(q.Target.z)).Append('|')
					.Append(S(q.Pin.x)).Append('|').Append(S(q.Pin.z)).Append('|')
					.Append(q.Progress).Append('|').Append(q.Needed).Append('|').Append(q.Done ? '1' : '0').Append('|')
					.Append(q.ChestUser.ToString(CultureInfo.InvariantCulture)).Append('|').Append(q.ChestId.ToString(CultureInfo.InvariantCulture)).Append('|')
					.Append(q.Difficulty);
			}
			p.m_customData[SaveKey] = sb.ToString();
			_version++;
		}

		private static int Int(string s) => int.Parse(s, CultureInfo.InvariantCulture);
		private static float Flt(string s) => float.Parse(s, CultureInfo.InvariantCulture);
		private static string S(float f) => f.ToString("0.##", CultureInfo.InvariantCulture);

		private static int Day => EnvMan.instance != null ? EnvMan.instance.GetDay() : 0;

		private static HashSet<string> TakenToday()
		{
			var set = new HashSet<string>();
			Player p = Player.m_localPlayer;
			if (p == null || !p.m_customData.TryGetValue(TakenKey, out string text)) return set;
			string[] parts = text.Split('\n');
			if (parts.Length == 0 || parts[0] != Day.ToString(CultureInfo.InvariantCulture)) return set;
			for (int i = 1; i < parts.Length; i++) set.Add(parts[i]);
			return set;
		}

		private static void MarkTaken(string vendor, string title)
		{
			Player p = Player.m_localPlayer;
			if (p == null) return;
			HashSet<string> set = TakenToday();
			set.Add(vendor + ":" + title);
			p.m_customData[TakenKey] = Day.ToString(CultureInfo.InvariantCulture) + "\n" + string.Join("\n", set);
		}

		/// <summary>Config "Reset my quests": drop every active quest and its pins, and clear today's taken offers.</summary>
		public static void ResetAll()
		{
			Player p = Player.m_localPlayer;
			if (p == null)
			{
				Plugin.Log.LogInfo("Reset my quests: load into a world first.");
				return;
			}
			EnsureLoaded();
			foreach (Quest q in Active) RemovePin(q);
			Active.Clear();
			RemoveSavedPins(null, null); // also any left behind by earlier builds
			p.m_customData.Remove(TakenKey);
			Save();
			Refresh();
			if (StoreGui.IsVisible()) StoreGui.instance.FillList();
			p.Message(MessageHud.MessageType.Center, "Quests reset.");
			Plugin.Log.LogInfo("Quests reset: active quests dropped and today's offers restored.");
		}

		// ---------- offers ----------

		private class Offer
		{
			public Kind Kind;
			public string Title;
			public int Step;
			public int Difficulty = -1;
		}

		private static bool IsBounty(Kind k) => k == Kind.Hunt || k == Kind.Camp;

		private static DifficultyDef Diff(int i)
		{
			List<DifficultyDef> list = DataStore.Quests.Difficulties;
			return i >= 0 && i < list.Count ? list[i] : null;
		}

		private static string DiffTag(int i)
		{
			DifficultyDef d = Diff(i);
			return d == null ? "" : $" <color={d.Color}>({d.Name})</color>";
		}

		private static string StarRange(DifficultyDef d)
		{
			if (d == null) return "";
			string max = d.MaxStars >= 5 ? d.MaxStars + "" : d.MaxStars + "";
			if (d.MinStars == d.MaxStars) return d.MinStars == 0 ? "" : d.MinStars + "-star ";
			return d.MaxStars >= 5 && d.MinStars >= 3 ? $"{d.MinStars}+ star " : $"{d.MinStars}-{max} star ";
		}

		private static int StepOf(string biome) => Mathf.Max(0, DataStore.Loot.BiomeStep(biome));

		/// <summary>
		/// Quests reach one biome past your last boss: Meadows with none, Black Forest after Eikthyr, Swamp after the
		/// Elder... (the shop's gear stops at the boss's own biome).
		/// </summary>
		public static int QuestStep()
		{
			Player p = Player.m_localPlayer;
			bool any = false;
			foreach (string key in DataStore.Vendors.BossKeys.Keys)
				if ((ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(key)) || (p != null && p.HaveUniqueKey(key))) { any = true; break; }
			if (!any) return 0;
			return Mathf.Min(Shop.BossStep() + 1, Mathf.Max(0, DataStore.Loot.BiomeOrder.Count - 1));
		}

		/// <summary>True when the last list built had no quests or offers (the note says so).</summary>
		public static bool LastListEmpty;

		private static List<Offer> Offers(string vendor)
		{
			QuestFile qf = DataStore.Quests;
			int boss = QuestStep();
			var all = new List<Offer>();
			if (Gives(vendor, Kind.Hunt)) foreach (HuntDef d in qf.Hunts) all.Add(new Offer { Kind = Kind.Hunt, Title = d.Title, Step = StepOf(d.Biome) });
			if (Gives(vendor, Kind.Camp)) foreach (CampDef d in qf.Camps) all.Add(new Offer { Kind = Kind.Camp, Title = d.Title, Step = StepOf(d.Biome) });
			if (Gives(vendor, Kind.Delve)) foreach (DelveDef d in qf.Delves) all.Add(new Offer { Kind = Kind.Delve, Title = d.Title, Step = StepOf(d.Biome) });
			if (Gives(vendor, Kind.Trophy)) foreach (TrophyDef d in qf.Trophies) all.Add(new Offer { Kind = Kind.Trophy, Title = d.Title, Step = StepOf(d.Biome) });
			all.RemoveAll(o => string.IsNullOrEmpty(o.Title) || o.Step > boss);

			// Today's pick: shuffled by world, day and vendor (the same for everyone).
			int seed = (WorldGenerator.instance != null ? WorldGenerator.instance.GetSeed() : 0) * 13 + Day * 7907 + vendor.GetHashCode();
			var rng = new System.Random(seed);
			for (int i = all.Count - 1; i > 0; i--)
			{
				int j = rng.Next(i + 1);
				(all[i], all[j]) = (all[j], all[i]);
			}
			HashSet<string> taken = TakenToday();
			var list = new List<Offer>();
			int picked = 0;
			foreach (Offer o in all)
			{
				if (picked >= qf.OffersPerVendor) break;
				if (taken.Contains(vendor + ":" + o.Title)) continue;
				if (Active.Exists(q => q.Vendor == vendor && q.Title == o.Title)) continue;
				picked++;
				// Bounties come in every difficulty; taking one takes that bounty off the board.
				if (IsBounty(o.Kind) && qf.Difficulties.Count > 0)
					for (int d = 0; d < qf.Difficulties.Count; d++)
						list.Add(new Offer { Kind = o.Kind, Title = o.Title, Step = o.Step, Difficulty = d });
				else list.Add(o);
			}
			return list;
		}

		// ---------- the list in the QUESTS tab ----------

		private class Row
		{
			public Quest Quest;   // an active quest
			public Offer Offer;   // or an offer
			public string Description;
		}

		private static readonly Dictionary<Trader.TradeItem, Row> Rows = new Dictionary<Trader.TradeItem, Row>();
		private static List<Trader.TradeItem> _list;
		private static string _listKey;
		private static int _version;

		public static void Refresh() => _listKey = null;

		public static List<Trader.TradeItem> Entries(Trader trader)
		{
			EnsureLoaded();
			Shop.ShopOf(trader, out string vendor);
			if (vendor == null) return new List<Trader.TradeItem>();
			string key = $"{vendor}|{Day}|{_version}|{QuestStep()}|{TrophyCountKey()}";
			if (key == _listKey && _list != null) { LastListEmpty = _list.Count == 0; return _list; }
			_listKey = key;
			Rows.Clear();
			_list = new List<Trader.TradeItem>();

			foreach (Quest q in Active)
				if (q.Vendor == vendor)
					AddRow(new Row { Quest = q, Description = Status(q) }, q.Kind, q.Title + DiffTag(q.Difficulty), Tooltip(q));
			foreach (Offer o in Offers(vendor))
				AddRow(new Row { Offer = o, Description = Short(o) }, o.Kind, o.Title + DiffTag(o.Difficulty), OfferTooltip(o, vendor));
			LastListEmpty = _list.Count == 0;
			return _list;
		}

		private static void AddRow(Row row, Kind kind, string title, string tooltip)
		{
			string tag = row.Quest == null ? Label(kind) : row.Quest.Done ? "DONE" : "ACTIVE";
			string color = row.Quest == null ? Coins.GoldHex : row.Quest.Done ? "#7CFC7C" : "#8FD3FF";
			var ti = new Trader.TradeItem
			{
				m_prefab = null,
				m_stack = 1,
				m_price = 0,
				m_name = $"<color={color}>{tag}:</color> {title}",
				m_tooltip = tooltip,
				m_incrementKey = "LLA_quest", // so the button doesn't need a free inventory slot
				m_buyPlayerEffects = new EffectList(),
			};
			Rows[ti] = row;
			_list.Add(ti);
		}

		public static bool Is(Trader.TradeItem ti) => ti != null && Rows.ContainsKey(ti);

		/// <summary>The Buy button's word for the selected row.</summary>
		public static string ButtonLabel(Trader.TradeItem ti)
		{
			if (ti == null || !Rows.TryGetValue(ti, out Row row)) return "Accept";
			if (row.Quest == null) return "Accept";
			if (row.Quest.Done || (row.Quest.Kind == Kind.Trophy && HaveTrophies(row.Quest))) return "Claim";
			return _abandonArmed == row.Quest ? "Abandon?" : "Abandon";
		}

		private static string Short(Offer o)
		{
			switch (o.Kind)
			{
				case Kind.Hunt: { HuntDef d = Hunt(o.Title); return d != null ? $"Slay a {StarRange(Diff(o.Difficulty))}{Name(d.Creature)}." : ""; }
				case Kind.Camp: { CampDef d = Camp(o.Title); return d != null ? $"Defeat {d.Count} {StarRange(Diff(o.Difficulty))}foes at a marked camp." : ""; }
				case Kind.Delve: return "Find the marked chest in a dungeon.";
				default: { TrophyDef d = Trophy(o.Title); return d != null ? $"Bring {d.Count} {Name(d.Trophy)}." : ""; }
			}
		}

		private static string Status(Quest q)
		{
			if (q.Done) return "Done! Claim your reward.";
			switch (q.Kind)
			{
				case Kind.Hunt: return q.HasTarget ? "Find it in the circled area." : "Looking for a spot...";
				case Kind.Camp: return !q.HasTarget ? "Finding the camp..." : $"{q.Progress} of {q.Needed} defeated.";
				case Kind.Delve: return !q.HasTarget ? "Finding the dungeon..." : "Search the circled dungeon.";
				default:
				{
					TrophyDef d = Trophy(q.Title);
					int have = d != null ? CountTrophies(d) : 0;
					return $"You have {have} of {q.Needed}." + (have >= q.Needed ? " Claim it!" : "");
				}
			}
		}

		private static string Name(string prefab)
		{
			GameObject go = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefab) : null;
			if (go == null && ObjectDB.instance != null) go = ObjectDB.instance.GetItemPrefab(prefab);
			if (go == null) return prefab;
			Character c = go.GetComponent<Character>();
			if (c != null) return Localization.instance.Localize(c.m_name);
			ItemDrop i = go.GetComponent<ItemDrop>();
			return i != null ? Localization.instance.Localize(i.m_itemData.m_shared.m_name) : prefab;
		}

		private static string Reward(Kind k, int step, int difficulty)
		{
			int coins = RewardCoins(k, step, difficulty);
			DifficultyDef dd = Diff(difficulty);
			string bump = dd == null ? "" : dd.TierBump > 0 ? " (a tier higher)" : dd.BossBump ? " (boss tier bump)" : "";
			string gear = IsBounty(k) ? "a weapon" + bump : k == Kind.Delve ? "a piece of armor" : "a staff or trinket";
			return $"<color=#A8A8A8>Reward:</color> <color={Vendors.Coins.GoldHex}>{Vendors.Coins.Exact(coins)} coins</color> + {gear}";
		}

		private static int RewardCoins(Kind k, int step, int difficulty)
		{
			QuestFile qf = DataStore.Quests;
			float c = k == Kind.Hunt || k == Kind.Camp ? qf.BountyCoins : k == Kind.Delve ? qf.DelveCoins : qf.TrophyCoins;
			DifficultyDef dd = Diff(difficulty);
			if (dd != null) c *= dd.Coins;
			return Mathf.Max(1, Mathf.RoundToInt(c * VendorTables.AtStep(DataStore.Vendors.BiomeBuyMultiplier, step)));
		}

		private static string Desc(Kind k, string title)
		{
			switch (k)
			{
				case Kind.Hunt: return Hunt(title)?.Description ?? "";
				case Kind.Camp: return Camp(title)?.Description ?? "";
				case Kind.Delve: return Delve(title)?.Description ?? "";
				default: return Trophy(title)?.Description ?? "";
			}
		}

		private static string OfferTooltip(Offer o, string vendor)
		{
			string d = Desc(o.Kind, o.Title);
			return (d.Length > 0 ? d + "\n\n" : "") + Short(o) + "\n\n" + Reward(o.Kind, o.Step, o.Difficulty) +
			       (ActiveCount(vendor) >= DataStore.Quests.MaxActivePerVendor ? "\n\n<color=#E06060>You have too many quests here. Finish or abandon one first.</color>" : "");
		}

		private static string Tooltip(Quest q)
		{
			string d = Desc(q.Kind, q.Title);
			return (d.Length > 0 ? d + "\n\n" : "") + Status(q) + "\n\n" + Reward(q.Kind, q.Step, q.Difficulty) +
			       (q.Done ? "" : "\n\n<color=#A8A8A8>Press Abandon twice to give it up.</color>");
		}

		private static int ActiveCount(string vendor) => Active.FindAll(q => q.Vendor == vendor).Count;

		private static HuntDef Hunt(string t) => DataStore.Quests.Hunts.Find(d => d.Title == t);
		private static CampDef Camp(string t) => DataStore.Quests.Camps.Find(d => d.Title == t);
		private static DelveDef Delve(string t) => DataStore.Quests.Delves.Find(d => d.Title == t);
		private static TrophyDef Trophy(string t) => DataStore.Quests.Trophies.Find(d => d.Title == t);

		// ---------- trophies ----------

		private static string TrophyName(TrophyDef d)
		{
			GameObject go = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(d.Trophy) : null;
			ItemDrop i = go != null ? go.GetComponent<ItemDrop>() : null;
			return i != null ? i.m_itemData.m_shared.m_name : null;
		}

		private static int CountTrophies(TrophyDef d)
		{
			string n = TrophyName(d);
			Player p = Player.m_localPlayer;
			return n != null && p != null ? p.GetInventory().CountItems(n) : 0;
		}

		private static bool HaveTrophies(Quest q)
		{
			TrophyDef d = Trophy(q.Title);
			return d != null && CountTrophies(d) >= q.Needed;
		}

		private static string TrophyCountKey()
		{
			var sb = new StringBuilder();
			foreach (Quest q in Active)
				if (q.Kind == Kind.Trophy) { TrophyDef d = Trophy(q.Title); sb.Append(d != null ? CountTrophies(d) : 0).Append(','); }
			return sb.ToString();
		}

		// ---------- the button ----------

		private static Quest _abandonArmed;

		[HarmonyPatch(typeof(StoreGui), nameof(StoreGui.BuySelectedItem))]
		private static class Patch_Button
		{
			[HarmonyPriority(Priority.First)]
			private static bool Prefix(StoreGui __instance)
			{
				Trader.TradeItem ti = __instance.m_selectedItem;
				if (ti == null || !Rows.TryGetValue(ti, out Row row)) return true;
				Shop.ShopOf(__instance.m_trader, out string vendor);
				if (row.Offer != null) Accept(vendor, row.Offer);
				else Use(row.Quest);
				Refresh();
				int sel = __instance.GetSelectedItemIndex();
				__instance.FillList();
				if (sel >= 0) __instance.SelectItem(Mathf.Min(sel, __instance.m_itemList.Count - 1), false);
				return false;
			}
		}

		/// <summary>Offers can't be taken past the limit: the button greys out.</summary>
		[HarmonyPatch(typeof(StoreGui), nameof(StoreGui.CanAfford))]
		private static class Patch_CanAfford
		{
			private static void Postfix(Trader.TradeItem item, ref bool __result)
			{
				if (item == null || !Rows.TryGetValue(item, out Row row)) return;
				__result = row.Quest != null || (StoreGui.instance != null && Shop.ShopOf(StoreGui.instance.m_trader, out string v) != null &&
				                                 ActiveCount(v) < DataStore.Quests.MaxActivePerVendor);
			}
		}

		private static void Accept(string vendor, Offer o)
		{
			Player p = Player.m_localPlayer;
			if (p == null) return;
			if (ActiveCount(vendor) >= DataStore.Quests.MaxActivePerVendor)
			{
				p.Message(MessageHud.MessageType.Center, "Finish or abandon a quest here first.");
				return;
			}
			var q = new Quest
			{
				Id = $"{ZNet.GetUID():X}-{DateTime.UtcNow.Ticks:X}",
				Kind = o.Kind,
				Vendor = vendor,
				Title = o.Title,
				Step = o.Step,
				Difficulty = o.Difficulty,
			};
			switch (o.Kind)
			{
				case Kind.Hunt:
				{
					HuntDef d = Hunt(o.Title);
					if (d == null || !RandomSpot(p.transform.position, d.Biome, d.Water, out Vector3 spot))
					{
						p.Message(MessageHud.MessageType.Center, "No place for that hunt nearby. Try another.");
						return;
					}
					SetTarget(q, spot);
					break;
				}
				case Kind.Camp:
				{
					CampDef d = Camp(o.Title);
					if (d == null) return;
					q.Needed = Mathf.Max(1, d.Count);
					if (d.Locations.Count > 0) Locate(q, d.Locations);
					else if (RandomSpot(p.transform.position, d.Biome, false, out Vector3 spot)) SetTarget(q, spot);
					else { p.Message(MessageHud.MessageType.Center, "No place for that camp nearby. Try another."); return; }
					break;
				}
				case Kind.Delve:
				{
					DelveDef d = Delve(o.Title);
					if (d == null || d.Locations.Count == 0) return;
					Locate(q, d.Locations);
					break;
				}
				default:
				{
					TrophyDef d = Trophy(o.Title);
					if (d == null) return;
					q.Needed = Mathf.Max(1, d.Count);
					break;
				}
			}
			Active.Add(q);
			MarkTaken(vendor, o.Title);
			Save();
			p.Message(MessageHud.MessageType.Center, $"{Label(o.Kind)}: {o.Title}{DiffTag(o.Difficulty)}" + (q.HasTarget || q.Locating ? " - check your map." : ""));
			Plugin.Debug($"[Quests] Accepted {o.Kind} '{o.Title}' from {vendor}.");
		}

		private static void Use(Quest q)
		{
			Player p = Player.m_localPlayer;
			if (p == null || q == null) return;
			if (q.Kind == Kind.Trophy && !q.Done && HaveTrophies(q))
			{
				TrophyDef d = Trophy(q.Title);
				p.GetInventory().RemoveItem(TrophyName(d), q.Needed);
				q.Done = true;
			}
			if (q.Done)
			{
				Claim(q);
				return;
			}
			if (_abandonArmed != q)
			{
				_abandonArmed = q;
				p.Message(MessageHud.MessageType.Center, "Press again to abandon this quest.");
				return;
			}
			_abandonArmed = null;
			RemovePin(q);
			Active.Remove(q);
			Save();
			p.Message(MessageHud.MessageType.Center, $"Abandoned: {q.Title}");
		}

		private static void Claim(Quest q)
		{
			Player p = Player.m_localPlayer;
			int coins = RewardCoins(q.Kind, q.Step, q.Difficulty);
			Vendors.Coins.Give(p, coins);
			LootRoller.Result r = RewardItem(q);
			if (r != null) Give(p, r);
			RemovePin(q);
			Active.Remove(q);
			Save();
			StoreGui store = StoreGui.instance;
			if (store != null)
			{
				store.m_buyEffects.Create(p.transform.position, Quaternion.identity);
				if (store.m_trader != null) store.m_trader.OnBought(null);
			}
			p.Message(MessageHud.MessageType.Center, $"Quest complete! {Vendors.Coins.Exact(coins)} coins" +
			                                         (r != null ? " and " + TooltipPatches.GetDisplayName(Shop.Make(r), Localization.instance.Localize(r.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name)) : "") + ".");
			Plugin.Debug($"[Quests] Claimed '{q.Title}': {coins} coins" + (r != null ? $" + {Tiers.GetLabel(r.Magic.Tier)} {r.Prefab.name}" : ""));
		}

		/// <summary>The guaranteed gear: from the giver's shop list (any you could have, known recipe or not).</summary>
		private static LootRoller.Result RewardItem(Quest q)
		{
			ShopDef shop = null;
			foreach (KeyValuePair<string, ShopDef> kv in DataStore.Vendors.Shops)
				if (string.Equals(kv.Key, q.Vendor, StringComparison.OrdinalIgnoreCase)) shop = kv.Value;
			if (shop == null) return null;
			List<ItemPool.Entry> pool = Shop.PoolAll(shop, q.Step);
			if (pool.Count == 0) return null;
			Tier tier = LootRoller.RollTier(q.Step);
			DifficultyDef dd = Diff(q.Difficulty);
			if (dd != null)
			{
				if (dd.TierBump > 0) tier = Tiers.Bump(tier, dd.TierBump);
				if (dd.BossBump && Random.value < DataStore.Loot.BossTierBumpChance) tier = Tiers.Bump(tier);
			}
			for (Tier t = tier; t >= Tier.Uncommon; t--)
			{
				var left = new List<ItemPool.Entry>(pool);
				while (left.Count > 0)
				{
					int i = Random.Range(0, left.Count);
					ItemPool.Entry e = left[i];
					left.RemoveAt(i);
					MagicItem m = AffixRoller.Roll(new ItemDrop.ItemData { m_shared = e.Shared }, t, null);
					if (m != null) return new LootRoller.Result { Prefab = e.Prefab, Magic = m };
				}
			}
			return null;
		}

		private static void Give(Player p, LootRoller.Result r)
		{
			Inventory inv = p.GetInventory();
			ItemDrop.ItemData added = inv.HaveEmptySlot() ? inv.AddItem(r.Prefab.name, 1, 1, 0, 0L, "", false, true) : null;
			if (added != null)
			{
				added.SetMagic(r.Magic);
				inv.Changed();
				return;
			}
			ItemDrop drop = DropPatches.Spawn(r, p.transform.position + p.transform.forward + Vector3.up, p);
			if (drop != null) p.Message(MessageHud.MessageType.Center, "Your bags are full. The reward fell at your feet.");
		}

		// ---------- places ----------

		private static bool RandomSpot(Vector3 from, string biome, bool water, out Vector3 spot)
		{
			spot = from;
			WorldGenerator wg = WorldGenerator.instance;
			if (wg == null) return false;
			if (!Enum.TryParse(biome, true, out Heightmap.Biome want)) return false;
			float waterLevel = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
			QuestFile qf = DataStore.Quests;
			for (int tries = 0; tries < 400; tries++)
			{
				float dist = Random.Range(qf.SearchMin, qf.SearchMax) * (tries > 200 ? 1.6f : 1f);
				float ang = Random.value * Mathf.PI * 2f;
				float x = from.x + Mathf.Cos(ang) * dist, z = from.z + Mathf.Sin(ang) * dist;
				if (wg.GetBiome(x, z) != want) continue;
				float h = wg.GetHeight(x, z);
				if (water ? h > waterLevel - 8f : h < waterLevel + 1f) continue;
				spot = new Vector3(x, water ? waterLevel : h, z);
				return true;
			}
			return false;
		}

		private static void Locate(Quest q, List<string> names)
		{
			Player p = Player.m_localPlayer;
			if (p == null || Game.instance == null || names.Count == 0) return;
			QuestFile qf = DataStore.Quests;
			float ang = Random.value * Mathf.PI * 2f, dist = Random.Range(qf.SearchMin, qf.SearchMax);
			Vector3 from = p.transform.position + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * dist;
			// Host or single player: search the world's list of dungeons directly. They're all placed when the world is
			// made, so unexplored ones count. Names match exactly or in part ("Crypt" finds Crypt2-4).
			if (ZNet.instance != null && ZNet.instance.IsServer() && ZoneSystem.instance != null)
			{
				if (FindNearest(names, from, out Vector3 found, out string which))
				{
					SetTarget(q, found);
					Save();
					Plugin.Debug($"[Quests] '{q.Title}': {which} at {found}.");
				}
				else
				{
					q.LocatingSince = Time.time - 60f; // not found: NotFound runs on the next tick
					Plugin.Log.LogWarning($"Quests: no {string.Join(" / ", names)} in this world. Use lla-locations to list the world's location names.");
				}
				return;
			}
			string name = names[Random.Range(0, names.Count)];
			q.LocatingSince = Time.time;
			Game.instance.DiscoverClosestLocation(name, from, LocateRpcPrefix + q.Id, (int)Minimap.PinType.None, false, false);
			Plugin.Debug($"[Quests] Looking for the closest {name} to {from} for '{q.Title}'.");
		}

		/// <summary>Closest location whose name matches one of the names (exactly, or containing it).</summary>
		public static bool FindNearest(List<string> names, Vector3 from, out Vector3 pos, out string which)
		{
			pos = Vector3.zero;
			which = null;
			float best = float.MaxValue;
			foreach (bool exact in new[] { true, false })
			{
				foreach (ZoneSystem.LocationInstance li in ZoneSystem.instance.m_locationInstances.Values)
				{
					string n = li.m_location?.m_prefabName;
					if (string.IsNullOrEmpty(n)) continue;
					bool match = false;
					foreach (string want in names)
					{
						if (string.IsNullOrEmpty(want)) continue;
						if (exact ? n.Equals(want, StringComparison.OrdinalIgnoreCase) : n.IndexOf(want, StringComparison.OrdinalIgnoreCase) >= 0) { match = true; break; }
					}
					if (!match) continue;
					float d = Flat(li.m_position, from);
					if (d < best) { best = d; pos = li.m_position; which = n; }
				}
				if (which != null) return true;
			}
			return false;
		}

		/// <summary>lla-locations: the world's location names containing the text, with how many there are.</summary>
		public static string ListLocations(string filter)
		{
			if (ZoneSystem.instance == null || ZNet.instance == null || !ZNet.instance.IsServer()) return "Only the host (or single player) can list locations.";
			var counts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
			foreach (ZoneSystem.LocationInstance li in ZoneSystem.instance.m_locationInstances.Values)
			{
				string n = li.m_location?.m_prefabName;
				if (string.IsNullOrEmpty(n) || (!string.IsNullOrEmpty(filter) && n.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)) continue;
				counts[n] = counts.TryGetValue(n, out int c) ? c + 1 : 1;
			}
			if (counts.Count == 0) return $"No locations matching '{filter}'.";
			var parts = new List<string>();
			foreach (var kv in counts) parts.Add($"{kv.Key} x{kv.Value}");
			return string.Join(", ", parts);
		}

		/// <summary>The server's answer to a location search: ours never become a vanilla pin.</summary>
		[HarmonyPatch(typeof(Game), nameof(Game.RPC_DiscoverLocationResponse))]
		private static class Patch_Located
		{
			private static bool Prefix(string pinName, Vector3 pos)
			{
				if (pinName == null || !pinName.StartsWith(LocateRpcPrefix, StringComparison.Ordinal)) return true;
				EnsureLoaded();
				string id = pinName.Substring(LocateRpcPrefix.Length);
				Quest q = Active.Find(x => x.Id == id);
				if (q != null && !q.HasTarget)
				{
					SetTarget(q, pos);
					Save();
					Plugin.Debug($"[Quests] '{q.Title}' is at {pos}.");
				}
				return false;
			}
		}

		private static void SetTarget(Quest q, Vector3 pos)
		{
			q.HasTarget = true;
			q.LocatingSince = -1f;
			q.Target = pos;
			float off = DataStore.Quests.PinOffset;
			Vector2 r = Random.insideUnitCircle * off;
			q.Pin = new Vector3(pos.x + r.x, 0f, pos.z + r.y);
			AddPin(q);
		}

		private static Minimap.PinType PinType(Quest q) => q.Kind == Kind.Delve ? Minimap.PinType.Hildir1 : Minimap.PinType.Boss;

		private static void AddPin(Quest q)
		{
			if (Minimap.instance == null || !q.HasTarget || q.Kind == Kind.Trophy) return;
			RemoveSavedPins(q.Pin, q.Title);
			Minimap.instance.AddPin(q.Pin, PinType(q), q.Title, true, false, 0L);
			EnsureArea(q);
		}

		private static void RemovePin(Quest q)
		{
			if (q.HasTarget) RemoveSavedPins(q.Pin, q.Title);
			if (Areas.TryGetValue(q.Id, out Minimap.PinData area))
			{
				if (Minimap.instance != null && area != null) Minimap.instance.RemovePin(area);
				Areas.Remove(q.Id);
			}
		}

		/// <summary>
		/// Removes our saved pins. (Vanilla's RemovePin(position) only finds pins currently drawn on the open map,
		/// so it missed pins when the map was closed.) With no position: every pin named after any quest.
		/// </summary>
		private static void RemoveSavedPins(Vector3? at, string title)
		{
			Minimap map = Minimap.instance;
			if (map == null) return;
			var titles = new HashSet<string>();
			if (title != null) titles.Add(title);
			else
			{
				QuestFile qf = DataStore.Quests;
				foreach (HuntDef d in qf.Hunts) titles.Add(d.Title);
				foreach (CampDef d in qf.Camps) titles.Add(d.Title);
				foreach (DelveDef d in qf.Delves) titles.Add(d.Title);
			}
			foreach (Minimap.PinData pin in map.m_pins.ToArray())
			{
				if (pin.m_type != Minimap.PinType.Boss && pin.m_type != Minimap.PinType.Hildir1) continue;
				if (!titles.Contains(pin.m_name)) continue;
				if (at.HasValue && Utils.DistanceXZ(pin.m_pos, at.Value) > 2f) continue;
				map.RemovePin(pin);
			}
		}

		// The circle around a bounty's pin (the raid-style area marker). Not saved with the map: redrawn each session.
		private static readonly Dictionary<string, Minimap.PinData> Areas = new Dictionary<string, Minimap.PinData>();
		private static Minimap _areasFor;

		private static void EnsureArea(Quest q)
		{
			if (Minimap.instance == null || !q.HasTarget || !IsBounty(q.Kind)) return;
			if (_areasFor != Minimap.instance) { Areas.Clear(); _areasFor = Minimap.instance; }
			if (Areas.TryGetValue(q.Id, out Minimap.PinData have) && have != null) return;
			Minimap.PinData area = Minimap.instance.AddPin(q.Pin, Minimap.PinType.EventArea, "", false, false, 0L);
			if (area == null) return;
			area.m_worldSize = DataStore.Quests.AreaRadius * 2f;
			Areas[q.Id] = area;
		}

		// ---------- in the world ----------

		private static float _nextTick;

		[HarmonyPatch(typeof(Player), nameof(Player.Update))]
		private static class Patch_Tick
		{
			private static void Postfix(Player __instance)
			{
				if (__instance != Player.m_localPlayer || Time.time < _nextTick) return;
				_nextTick = Time.time + 1f;
				EnsureLoaded();
				if (Active.Count > 0) Tick(__instance);
			}
		}

		private static void Tick(Player p)
		{
			Vector3 pos = p.transform.position;
			foreach (Quest q in Active.ToArray())
			{
				if (q.Done) continue;
				if (!q.HasTarget)
				{
					if (q.Kind != Kind.Camp && q.Kind != Kind.Delve) continue;
					if (q.LocatingSince < 0f)
					{
						// Saved while still searching (or the search was lost): ask again.
						if (q.Kind == Kind.Camp) Locate(q, Camp(q.Title)?.Locations ?? new List<string>());
						else Locate(q, Delve(q.Title)?.Locations ?? new List<string>());
					}
					else if (Time.time - q.LocatingSince > 10f) NotFound(p, q);
					continue;
				}
				switch (q.Kind)
				{
					case Kind.Hunt:
					case Kind.Camp:
					{
						EnsureArea(q);
						if (p.InInterior()) break;
						QuestFile qf = DataStore.Quests;
						// Spawn early enough that they're there before you reach the circle.
						float spawnAt = Mathf.Max(qf.ApproachDistance, qf.AreaRadius + qf.PinOffset + 20f);
						if (Flat(pos, q.Target) < spawnAt) KeepSpawned(q);
						if (Flat(pos, q.Pin) < qf.AreaRadius) Hunt(q, p);
						break;
					}
					case Kind.Delve:
						if (p.InInterior() && Flat(pos, q.Target) < 150f) DelveInside(p, q);
						break;
				}
			}
		}

		private static void NotFound(Player p, Quest q)
		{
			q.LocatingSince = -1f;
			if (q.Kind == Kind.Camp)
			{
				CampDef d = Camp(q.Title);
				if (d != null && RandomSpot(p.transform.position, d.Biome, false, out Vector3 spot))
				{
					SetTarget(q, spot);
					Save();
					return;
				}
			}
			Active.Remove(q);
			Save();
			p.Message(MessageHud.MessageType.Center, $"{q.Title}: no such place in this world. The quest was dropped.");
		}

		private static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

		/// <summary>Bounty creatures: keep the number still to defeat standing at the target while you're near.</summary>
		private static void KeepSpawned(Quest q)
		{
			if (Time.time < q.NextSpawn) return;
			int alive = 0;
			foreach (Character c in Character.GetAllCharacters())
			{
				ZDO zdo = c.m_nview != null && c.m_nview.IsValid() ? c.m_nview.GetZDO() : null;
				if (zdo != null && !c.IsDead() && zdo.GetString(ZdoKey) == q.Id) alive++;
			}
			int need = q.Needed - q.Progress - alive;
			if (need <= 0) return;
			q.NextSpawn = Time.time + 15f;

			string[] names;
			DifficultyDef dd = Diff(q.Difficulty) ?? Diff(0);
			int minStars = dd != null ? dd.MinStars : 0, maxStars = dd != null ? Mathf.Max(dd.MinStars, dd.MaxStars) : 1;
			if (q.Kind == Kind.Hunt)
			{
				HuntDef d = Hunt(q.Title);
				if (d == null) return;
				names = new[] { d.Creature };
			}
			else
			{
				CampDef d = Camp(q.Title);
				if (d == null || d.Creatures.Count == 0) return;
				names = d.Creatures.ToArray();
			}
			for (int i = 0; i < need; i++)
			{
				string name = names[q.Kind == Kind.Hunt ? 0 : (alive + i == 0 ? names.Length - 1 : Random.Range(0, names.Length))];
				GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(name) : null;
				if (prefab == null) { Plugin.Log.LogWarning($"quests.yml: no creature called {name}."); continue; }
				Vector2 r = Random.insideUnitCircle * (q.Kind == Kind.Hunt ? 2f : 8f);
				Vector3 at = q.Target + new Vector3(r.x, 0f, r.y);
				HuntDef hd = q.Kind == Kind.Hunt ? Hunt(q.Title) : null;
				if (hd != null && hd.Water) at.y = ZoneSystem.instance.m_waterLevel;
				else if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(at, out float h)) at.y = h + 0.5f;
				GameObject go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
				Character c = go.GetComponent<Character>();
				if (c == null) continue;
				// Stars rolled in the difficulty's range; a camp's leader gets the top of it. Level = stars + 1.
				bool leader = q.Kind == Kind.Camp && alive + i == 0;
				int level = (leader ? maxStars : Random.Range(minStars, maxStars + 1)) + 1;
				c.SetLevel(Mathf.Clamp(level, 1, 10));
				Plugin.Debug($"[Quests] {name} for '{q.Title}' ({Diff(q.Difficulty)?.Name ?? "no difficulty"}): {level - 1} stars.");
				c.m_nview.GetZDO().Set(ZdoKey, q.Id);
			}
			Plugin.Debug($"[Quests] Spawned {need} for '{q.Title}' at {q.Target}.");
		}

		/// <summary>
		/// You're inside the bounty's circle: its creatures know you're there and come for you. Creatures that only
		/// flee (deer and other animals) are left alone.
		/// </summary>
		private static void Hunt(Quest q, Player p)
		{
			foreach (Character c in Character.GetAllCharacters())
			{
				if (c.IsDead() || c.m_nview == null || !c.m_nview.IsValid() || !c.m_nview.IsOwner()) continue;
				if (c.m_nview.GetZDO().GetString(ZdoKey) != q.Id) continue;
				MonsterAI ai = c.GetComponent<MonsterAI>();
				if (ai == null) continue; // animals run away; nothing to aggro
				if (ai.m_targetCreature == p) continue;
				ai.SetHuntPlayer(true);
				ai.SetAlerted(true);
				ai.m_targetCreature = p;
			}
		}

		/// <summary>On the game that owns a quest creature: when it dies, tell everyone (the quest's owner counts it).</summary>
		[HarmonyPatch(typeof(Character), nameof(Character.RPC_Damage))]
		private static class Patch_Death
		{
			private static void Prefix(Character __instance, out float __state) => __state = __instance.GetHealth();

			private static void Postfix(Character __instance, float __state)
			{
				if (__state <= 0f || __instance.GetHealth() > 0f) return;
				ZDO zdo = __instance.m_nview != null && __instance.m_nview.IsValid() ? __instance.m_nview.GetZDO() : null;
				string id = zdo?.GetString(ZdoKey);
				if (string.IsNullOrEmpty(id)) return;
				zdo.Set(ZdoKey, ""); // count it once
				ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, KillRpc, id);
			}
		}

		private const string KillRpc = "LLA_QuestKill";

		[HarmonyPatch(typeof(Game), nameof(Game.Start))]
		private static class Patch_Register
		{
			private static void Postfix()
			{
				ZRoutedRpc.instance.Register<string>(KillRpc, (sender, id) => OnKill(id));
				_loadedFor = null;
			}
		}

		private static void OnKill(string id)
		{
			EnsureLoaded();
			Quest q = Active.Find(x => x.Id == id);
			if (q == null || q.Done) return;
			q.Progress++;
			if (q.Progress >= q.Needed) { q.Done = true; RemovePin(q); } // done: nothing left to find on the map
			Save();
			Player.m_localPlayer?.Message(MessageHud.MessageType.Center,
				q.Done ? $"{Label(q.Kind)} complete: {q.Title}. Return to {VendorName(q.Vendor)}." : $"{q.Title}: {q.Progress} of {q.Needed}");
		}

		private static string VendorName(string v) =>
			v.Equals("BogWitch", StringComparison.OrdinalIgnoreCase) ? "the Bog Witch" : v;

		// ---------- delves ----------

		private static readonly Dictionary<string, LightShaft> Beams = new Dictionary<string, LightShaft>();

		private static void DelveInside(Player p, Quest q)
		{
			ZDOID chestId = new ZDOID(q.ChestUser, (uint)q.ChestId);
			GameObject chest = q.ChestId != 0 && ZNetScene.instance != null ? ZNetScene.instance.FindInstance(chestId) : null;
			if (q.ChestId == 0 && PlaceChest(p, q, out GameObject placed))
			{
				ZDOID pid = placed.GetComponent<ZNetView>().GetZDO().m_uid;
				q.ChestUser = pid.UserID;
				q.ChestId = pid.ID;
				Save();
				chest = placed;
				int back = DataStore.Quests.RefillDelves ? Refill(p.transform.position) : 0;
				p.Message(MessageHud.MessageType.Center, back >= 3 ? "Somewhere in here, a chest glows... and the dead stir." : "Somewhere in here, a chest glows...");
			}
			if (q.ChestId == 0)
			{
				Plugin.Log.LogInfo($"Delve '{q.Title}': couldn't place a chest; marking one of the dungeon's own.");
				// Fallback: the dungeon's own chest farthest from where you came in.
				Container best = null;
				float far = -1f;
				foreach (Container c in UnityEngine.Object.FindObjectsOfType<Container>())
				{
					if (c.m_nview == null || !c.m_nview.IsValid() || SellBox.Is(c)) continue;
					if (c.GetComponent<Piece>() != null && c.GetComponent<Piece>().IsPlacedByPlayer()) continue;
					float d = Vector3.Distance(c.transform.position, p.transform.position);
					if (d > 150f) continue;
					if (d > far) { far = d; best = c; }
				}
				if (best == null)
				{
					q.Done = true;
					RemovePin(q);
					Save();
					p.Message(MessageHud.MessageType.Center, (Delve(q.Title)?.Found ?? "You found it!") + $" Return to {VendorName(q.Vendor)}.");
					return;
				}
				ZDOID id = best.m_nview.GetZDO().m_uid;
				q.ChestUser = id.UserID;
				q.ChestId = id.ID;
				Save();
				chest = best.gameObject;
				p.Message(MessageHud.MessageType.Center, "Somewhere in here, a chest glows...");
			}
			if (chest != null && (!Beams.TryGetValue(q.Id, out LightShaft beam) || beam == null))
				Beams[q.Id] = LightShaft.Mark(chest, Tier.Legendary);
		}

		private static int _floorMask;

		/// <summary>
		/// Places the delve's own chest on the floor of the room deepest from where you came in (rooms within 200 m,
		/// not dead-end caps). It's a normal saved chest, so it stays for everyone.
		/// </summary>
		private static bool PlaceChest(Player p, Quest q, out GameObject chest) => PlaceChest(p, Delve(q.Title)?.Chest, q.Title, out chest, out _);

		private static GameObject _holder;

		/// <summary>
		/// Places a chest on the floor of the spot deepest from where you are: the farthest room, creature spawner or
		/// chest of this dungeon (within 200 m) that has floor under it. The chest is made switched off and its
		/// random-spawn part removed before it wakes up, so it can't hide itself.
		/// </summary>
		public static bool PlaceChest(Player p, string chestName, string why, out GameObject chest, out string report)
		{
			chest = null;
			report = "";
			if (ZNetScene.instance == null) { report = "no scene"; return false; }
			GameObject prefab = null;
			foreach (string n in new[] { chestName, "TreasureChest_forestcrypt", "piece_chest_wood" })
				if (!string.IsNullOrEmpty(n) && (prefab = ZNetScene.instance.GetPrefab(n)) != null) break;
			if (prefab == null) { report = "no chest prefab"; return false; }
			if (_floorMask == 0) _floorMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");

			Dungeon dungeon = Dungeon.At(p.transform.position);
			// Depth is measured from the dungeon's entrance room (where you come in), not from where you stand.
			Vector3 start = dungeon.Entrance != null ? dungeon.Entrance.transform.position : p.transform.position;
			var spots = new List<KeyValuePair<string, Vector3>>();
			int rooms = 0, spawners = 0, chests = 0;
			foreach (Room r in dungeon.Rooms)
				if (r != null && !r.m_endCap) { rooms++; spots.Add(new KeyValuePair<string, Vector3>("room " + r.name, r.transform.position)); }
			foreach (CreatureSpawner sp in CreatureSpawner.m_creatureSpawners)
				if (sp != null && dungeon.Contains(sp.transform.position)) { spawners++; spots.Add(new KeyValuePair<string, Vector3>("spawner", sp.transform.position)); }
			foreach (Container c in UnityEngine.Object.FindObjectsOfType<Container>())
				if (c != null && !SellBox.Is(c) && (c.GetComponent<Piece>() == null || !c.GetComponent<Piece>().IsPlacedByPlayer()) && dungeon.Contains(c.transform.position))
				{ chests++; spots.Add(new KeyValuePair<string, Vector3>("chest " + c.name, c.transform.position + c.transform.forward * 1.2f)); }

			string bestName = null;
			Vector3 bestSpot = Vector3.zero;
			float far = -1f;
			int noFloor = 0;
			foreach (var kv in spots)
			{
				Vector3 pos = kv.Value;
				if (!Character.InInterior(pos)) continue;
				float dist = Vector3.Distance(pos, start);
				if (dist > 400f || dist <= far) continue;
				if (!Floor(pos, out Vector3 floor)) { noFloor++; continue; }
				far = dist;
				bestName = kv.Key;
				bestSpot = floor;
			}
			report = $"this dungeon ({dungeon.Name}): {rooms} rooms, {spawners} spawners, {chests} chests, entrance {(dungeon.Entrance != null ? "found" : "not found")}, {noFloor} spots without floor; ";
			if (bestName == null || far < 8f) { report += "no spot deep enough"; return false; }

			if (_holder == null) { _holder = new GameObject("LLA_ChestHolder"); _holder.SetActive(false); UnityEngine.Object.DontDestroyOnLoad(_holder); }
			chest = UnityEngine.Object.Instantiate(prefab, _holder.transform);
			foreach (RandomSpawn rs in chest.GetComponentsInChildren<RandomSpawn>(true)) UnityEngine.Object.DestroyImmediate(rs);
			// Position it first: it wakes up (and saves where it is) the moment it leaves the switched-off holder.
			chest.transform.position = bestSpot;
			chest.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
			chest.transform.SetParent(null, true);
			chest.SetActive(true);
			ZNetView nv = chest.GetComponent<ZNetView>();
			if (nv == null || !nv.IsValid()) { UnityEngine.Object.Destroy(chest); chest = null; report += "the chest didn't register"; return false; }
			report += $"placed {prefab.name} at the {bestName}, {far:0} m in";
			Plugin.Log.LogInfo($"Delve '{why}': {report}.");
			return true;
		}

		/// <summary>
		/// A cleared dungeon fills up again: every spawner in it whose creature is dead may spawn once more (they come
		/// back as you explore). Spawners near you are left alone, so nothing appears at the entrance.
		/// </summary>
		private static int Refill(Vector3 from)
		{
			int count = 0;
			Dungeon dungeon = Dungeon.At(from);
			foreach (CreatureSpawner sp in CreatureSpawner.m_creatureSpawners.ToArray())
			{
				if (sp == null || sp.m_nview == null || !sp.m_nview.IsValid()) continue;
				Vector3 pos = sp.transform.position;
				if (!Character.InInterior(pos) || !dungeon.Contains(pos)) continue;
				float d = Vector3.Distance(pos, from);
				if (d < 15f) continue;
				ZDO zdo = sp.m_nview.GetZDO();
				ZDOConnection con = zdo.GetConnection();
				bool spawned = con != null && con.m_type == ZDOExtraData.ConnectionType.Spawned;
				if (!spawned) continue;                                  // never spawned yet: it will on its own
				if (sp.SpawnedCreatureStillExists(con.m_target)) continue; // still alive: leave it
				if (!sp.m_nview.IsOwner()) zdo.SetOwner(ZDOMan.GetSessionID());
				zdo.SetConnection(ZDOExtraData.ConnectionType.None, ZDOID.None);
				zdo.Set(ZDOVars.s_aliveTime, 0L);
				count++;
			}
			if (count > 0) Plugin.Debug($"[Quests] Delve: {count} of this dungeon's creatures will return.");
			return count;
		}

		/// <summary>
		/// The floor right under a spot (looking down from just 0.5 m up, since tomb ceilings are low), with headroom
		/// above it and a chest-sized space clear of walls.
		/// </summary>
		private static bool Floor(Vector3 at, out Vector3 spot)
		{
			spot = at;
			int mask = _floorMask != 0 ? _floorMask : ~0;
			if (!Physics.Raycast(at + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 2.5f, mask)) return false;
			spot = hit.point + Vector3.up * 0.05f;
			if (Physics.Raycast(spot + Vector3.up * 0.1f, Vector3.up, 1.4f, mask)) return false;                       // no headroom
			if (Physics.CheckCapsule(spot + Vector3.up * 0.55f, spot + Vector3.up * 0.9f, 0.45f, mask, QueryTriggerInteraction.Ignore))
				return false;                                                                                         // in a wall
			return true;
		}

		[HarmonyPatch(typeof(Container), nameof(Container.Interact))]
		private static class Patch_OpenChest
		{
			private static void Postfix(Container __instance, Humanoid character)
			{
				if (character == null || character != Player.m_localPlayer || __instance.m_nview == null || !__instance.m_nview.IsValid()) return;
				ZDOID id = __instance.m_nview.GetZDO().m_uid;
				EnsureLoaded();
				foreach (Quest q in Active)
				{
					if (q.Kind != Kind.Delve || q.Done || q.ChestId == 0 || q.ChestUser != id.UserID || q.ChestId != id.ID) continue;
					q.Done = true;
					RemovePin(q);
					Save();
					if (Beams.TryGetValue(q.Id, out LightShaft beam) && beam != null) UnityEngine.Object.Destroy(beam);
					((Player)character).Message(MessageHud.MessageType.Center, (Delve(q.Title)?.Found ?? "You found it!") + $" Return to {VendorName(q.Vendor)}.");
				}
			}
		}

		/// <summary>
		/// The shop selects a row by its old position after the list is rebuilt; when the new list is shorter (or
		/// empty), keep it in range instead of failing.
		/// </summary>
		[HarmonyPatch(typeof(StoreGui), nameof(StoreGui.SelectItem))]
		private static class Patch_SelectInRange
		{
			[HarmonyPriority(Priority.First)]
			private static bool Prefix(StoreGui __instance, ref int index)
			{
				int n = __instance.m_itemList != null ? __instance.m_itemList.Count : 0;
				int m = __instance.m_trader != null ? __instance.m_trader.GetAvailableItems().Count : 0;
				n = Mathf.Min(n, m);
				if (n <= 0)
				{
					__instance.m_selectedItem = null;
					__instance.UpdateBuyButton();
					return false;
				}
				index = Mathf.Clamp(index, 0, n - 1);
				return true;
			}
		}

		// ---------- the rows ----------

		/// <summary>Quest rows: the description replaces the price, and the coin and item pictures are hidden.</summary>
		[HarmonyPatch(typeof(StoreGui), nameof(StoreGui.FillList))]
		private static class Patch_FillList
		{
			private static void Postfix(StoreGui __instance)
			{
				if (!Tabs.QuestsOpen || __instance.m_trader == null) return;
				List<Trader.TradeItem> items = __instance.m_trader.GetAvailableItems();
				for (int i = 0; i < items.Count && i < __instance.m_itemList.Count; i++)
				{
					if (!Rows.TryGetValue(items[i], out Row r)) continue;
					GameObject row = __instance.m_itemList[i];
					Transform price = Utils.FindChild(row.transform, "price");
					TMP_Text text = price != null ? price.GetComponent<TMP_Text>() : null;
					if (text != null)
					{
						text.text = r.Description;
						text.color = new Color(0.85f, 0.82f, 0.75f);
						text.fontStyle = FontStyles.Italic;
						text.enableWordWrapping = false;
						text.overflowMode = TextOverflowModes.Ellipsis;
					}
					Transform icon = row.transform.Find("icon");
					foreach (Image img in row.GetComponentsInChildren<Image>(true))
					{
						if (img.gameObject == row || (icon != null && img.transform == icon)) continue;
						string sprite = img.sprite != null ? img.sprite.name : "";
						if (sprite.IndexOf("coin", StringComparison.OrdinalIgnoreCase) >= 0) img.enabled = false;
					}
					float left = 0f;
					if (icon != null)
					{
						RectTransform irt = (RectTransform)icon;
						left = irt.localPosition.x - irt.pivot.x * irt.rect.width;
						icon.gameObject.SetActive(false);
					}
					Transform nameT = row.transform.Find("name");
					RectTransform rowRt = (RectTransform)row.transform;
					foreach (TMP_Text tt in new[] { nameT != null ? nameT.GetComponent<TMP_Text>() : null, text })
						if (tt != null && icon != null) Stretch(tt, left, rowRt);
				}
			}
		}

		/// <summary>Left-aligns a text from x = left (row space) to the row's right edge, keeping its line.</summary>
		private static void Stretch(TMP_Text t, float left, RectTransform row)
		{
			RectTransform rt = t.rectTransform;
			var parent = (RectTransform)rt.parent;
			float l = parent.InverseTransformPoint(row.TransformPoint(new Vector3(left, 0f, 0f))).x;
			float r = parent.InverseTransformPoint(row.TransformPoint(new Vector3(row.rect.xMax - 10f, 0f, 0f))).x;
			float width = Mathf.Max(40f, r - l);
			float h = rt.rect.height;
			float y = rt.localPosition.y + (0.5f - rt.pivot.y) * h;
			rt.anchorMin = new Vector2(0.5f, 0.5f);
			rt.anchorMax = new Vector2(0.5f, 0.5f);
			rt.pivot = new Vector2(0f, 0.5f);
			rt.sizeDelta = new Vector2(width, h);
			rt.localPosition = new Vector3(l, y, rt.localPosition.z);
			t.alignment = TextAlignmentOptions.MidlineLeft;
		}
	}
}
