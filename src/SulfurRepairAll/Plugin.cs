using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.UI;
using PerfectRandom.Sulfur.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SulfurRepairAll
{
    /// <summary>
    /// Adds a "Repair All" button next to the repair lady's normal Repair
    /// button.
    ///
    /// Vanilla repair only fixes items you manually place in her 7x3 grid,
    /// at (PriceBase * 20% * missing durability) per item. This button pays
    /// exactly the same per-item formula for every repairable item in your
    /// backpack AND equipped paperdoll (PlayerBackpackGrid.AllItems includes
    /// equipped items), in one payment - stash first in the safe zone, just
    /// like vanilla. Items currently placed in her service grid are left to
    /// the normal Repair button.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "sulfur.mods.repair-all";
        public const string PluginName = "SULFUR Repair All";
        public const string PluginVersion = "1.0.1";

        internal static ManualLogSource Log;
        internal static ConfigEntry<float> CostFactorPercent;

        private static readonly Dictionary<int, Button> Buttons = new Dictionary<int, Button>();

        private void Awake()
        {
            Log = Logger;
            CostFactorPercent = Config.Bind("RepairAll", "CostFactorPercentOfVanilla", 100f,
                "Repair All price as % of the vanilla per-item repair formula. 100 = same price the repair lady would charge item-by-item.");
            new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("Repair All active: the repair lady now has a 'Repair All' button.");
        }

        internal static List<InventoryItem> GetRepairableInventoryItems(out int totalCost)
        {
            List<InventoryItem> repairable = new List<InventoryItem>();
            totalCost = 0;
            if (StaticInstance<UIManager>.Instance == null) return repairable;
            ItemGrid backpack = StaticInstance<UIManager>.Instance.PlayerBackpackGrid;
            if (backpack == null) return repairable;

            float factor = 0.2f * (CostFactorPercent.Value / 100f);
            foreach (InventoryItem item in backpack.AllItems()) // includes equipped paperdoll items
            {
                if (item == null || !item.IsRepairable) continue;
                repairable.Add(item);
                totalCost += (int)(item.PriceBase * factor * (1f - item.DurabilityNormalized));
            }
            return repairable;
        }

        internal static void CreateButton(RepairStation station)
        {
            Traverse traverse = Traverse.Create(station);
            Button original = traverse.Field("repairConfirmButton").GetValue<Button>();
            if (original == null) return;
            if (Buttons.ContainsKey(station.GetInstanceID())) return;

            GameObject clone = Object.Instantiate(original.gameObject, original.transform.parent);
            clone.name = "RepairAllButton";
            // All service stations share the one CraftInventory UI, so this
            // clone lives in a shared container. It must stay hidden except
            // while the repair station's UI is actually open, otherwise it
            // shows up in every merchant's table.
            clone.SetActive(false);
            Button button = clone.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => DoRepairAll(station));

            RectTransform rect = clone.GetComponent<RectTransform>();
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 210f);
            RectTransform originalRect = original.GetComponent<RectTransform>();
            rect.anchoredPosition = originalRect.anchoredPosition + new Vector2(180f, 0f);

            Buttons[station.GetInstanceID()] = button;
            RefreshButton(station);
            Log.LogInfo("Added Repair All button to a repair station.");
        }

        internal static void SetButtonVisible(RepairStation station, bool visible)
        {
            if (Buttons.TryGetValue(station.GetInstanceID(), out Button button) && button != null)
                button.gameObject.SetActive(visible);
        }

        internal static void HideAllButtons()
        {
            foreach (Button button in Buttons.Values)
                if (button != null) button.gameObject.SetActive(false);
        }

        internal static void RefreshButton(RepairStation station)
        {
            if (!Buttons.TryGetValue(station.GetInstanceID(), out Button button) || button == null) return;
            List<InventoryItem> repairable = GetRepairableInventoryItems(out int cost);
            TextMeshProUGUI label = button.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
            if (label != null)
                label.text = repairable.Count == 0 ? "Repair All" : $"Repair All ({cost})";
            var player = StaticInstance<GameManager>.Instance != null ? StaticInstance<GameManager>.Instance.PlayerUnit : null;
            bool canAfford = player != null && player.Stats != null &&
                             player.Stats.GetCoins(StaticInstance<GameManager>.Instance.InSafeZone) >= cost;
            button.interactable = repairable.Count > 0 && canAfford;
        }

        internal static void DoRepairAll(RepairStation station)
        {
            var gameManager = StaticInstance<GameManager>.Instance;
            if (gameManager == null || gameManager.PlayerUnit == null) return;
            var player = gameManager.PlayerUnit;

            List<InventoryItem> repairable = GetRepairableInventoryItems(out int cost);
            if (repairable.Count == 0) return;
            if (player.Stats.GetCoins(gameManager.InSafeZone) < cost)
            {
                StaticInstance<UIManager>.Instance.ShowSmallAlert($"Repairing everything costs {cost} coins.");
                return;
            }

            // Same payment split as vanilla RepairStation.DoRepair.
            int fromBag = cost;
            if (gameManager.InSafeZone)
            {
                int fromStash = Mathf.Min(player.Stats.GetCoinsStash(), cost);
                if (fromStash > 0) player.Stats.ModifyCoinsStash(-fromStash, station);
                fromBag -= fromStash;
            }
            if (fromBag > 0) player.Stats.ModifyCoins(-fromBag, station);

            Traverse traverse = Traverse.Create(station);
            traverse.Field("changesToFlushToDisk_OnConsole").SetValue(true);
            traverse.Method("OnServicePerformedEffects").GetValue();
            if (StaticInstance<SoundBankUI>.Instance != null)
                StaticInstance<SoundBankUI>.Instance.PlayClip(UISounds.UI_Repair);

            foreach (InventoryItem item in repairable)
            {
                item.ModifyDurability(item.DurabilityMax); // same call vanilla repair makes
                if (StaticInstance<AnalyticsManager>.Instance != null)
                    StaticInstance<AnalyticsManager>.Instance.TrackItemRepair(item);
            }

            traverse.Method("UpdateCombination").GetValue(); // refreshes vanilla price/button (and ours via postfix)
            StaticInstance<UIManager>.Instance.ShowSmallAlert($"Repaired {repairable.Count} item(s) for {cost} coins.");
            Log.LogInfo($"Repair All: repaired {repairable.Count} items for {cost} coins.");
        }
    }

    [HarmonyPatch(typeof(RepairStation), "DoSetup")]
    internal static class RepairStationSetupPatch
    {
        private static void Postfix(RepairStation __instance) { Plugin.CreateButton(__instance); }
    }

    [HarmonyPatch(typeof(RepairStation), "UIOnEnter")]
    internal static class RepairStationEnterPatch
    {
        private static void Postfix(RepairStation __instance)
        {
            Plugin.SetButtonVisible(__instance, true);
            Plugin.RefreshButton(__instance);
        }
    }

    [HarmonyPatch(typeof(RepairStation), "UIOnExit")]
    internal static class RepairStationExitPatch
    {
        private static void Postfix(RepairStation __instance) { Plugin.SetButtonVisible(__instance, false); }
    }

    // Safety net: entering any other service station (merchants, workbench,
    // reroll station, ...) hides the button even if an exit was missed.
    [HarmonyPatch(typeof(ServiceStation), "Enter", new System.Type[0])]
    internal static class OtherStationEnterPatch
    {
        private static void Postfix(ServiceStation __instance)
        {
            if (!(__instance is RepairStation)) Plugin.HideAllButtons();
        }
    }

    [HarmonyPatch(typeof(RepairStation), "UpdateCombination")]
    internal static class RepairStationCombinationPatch
    {
        private static void Postfix(RepairStation __instance) { Plugin.RefreshButton(__instance); }
    }
}
