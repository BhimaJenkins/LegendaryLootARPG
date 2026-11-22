using System;
using System.Reflection;
using HarmonyLib;

namespace LegendaryLoot.Vendors
{
	/// <summary>
	/// While a vendor's shop is open, its buttons own the controller: other mods that read the triggers straight
	/// from the gamepad (Keen Combat's Right Trigger skill and its Right Bumper heavy attack) are paused until the shop closes.
	/// Vanilla already ignores player input while the shop is open. Patched on the first shop opening, when every
	/// mod has loaded.
	/// </summary>
	public static class MenuInputGuard
	{
		private static readonly string[] Targets =
		{
			"KeenCombat.Patches.AttackInputPatch:Postfix",
			"KeenCombat.Patches.AttackInputPatch:Prefix",
		};

		private static bool _done;

		public static bool ShopOpen => StoreGui.IsVisible();

		private static void PatchOthers()
		{
			if (_done) return;
			_done = true;
			var harmony = new Harmony(Plugin.ModGuid + ".menuguard");
			MethodInfo guard = AccessTools.Method(typeof(MenuInputGuard), nameof(Guard));
			foreach (string target in Targets)
			{
				string[] parts = target.Split(':');
				Type type = AccessTools.TypeByName(parts[0]);
				MethodInfo m = type != null ? AccessTools.Method(type, parts[1]) : null;
				if (m == null) continue;
				try
				{
					harmony.Patch(m, prefix: new HarmonyMethod(guard));
					Plugin.Log.LogInfo($"Shop input guard: {target} pauses while a shop is open.");
				}
				catch (Exception ex)
				{
					Plugin.Log.LogWarning($"Shop input guard couldn't patch {target}: {ex.Message}");
				}
			}
		}

		private static bool Guard() => !ShopOpen;

		[HarmonyPatch(typeof(StoreGui), nameof(StoreGui.Show))]
		private static class Patch_Show
		{
			private static void Prefix() => PatchOthers();
		}
	}
}
