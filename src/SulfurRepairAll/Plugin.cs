using System.Collections.Generic;
using BepInEx;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.UI;
using PerfectRandom.Sulfur.Gameplay;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace SulfurMods.RepairAll;

[BepInPlugin("sulfur.mods.repairall", "Sulfur Repair All", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    internal static Plugin Instance;

    private void Awake()
    {
        Instance = this;
        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, "sulfur.mods.repairall");
        Logger.LogInfo("Repair All loaded. The extra button appears beside Repair.");
    }
}

static class RepairAllButton
{
    const string ObjectName = "RepairAllButton";
    static readonly HashSet<int> Hooked = new HashSet<int>();

    internal static void Ensure(RepairStation station)
    {
        var originalField = AccessTools.Field(typeof(RepairStation), "repairConfirmButton");
        var original = originalField != null ? originalField.GetValue(station) as Button : null;
        if (original == null)
            return;
        var existing = original.transform.parent.Find(ObjectName);
        Button button;
        if (existing == null)
        {
            var copy = Object.Instantiate(original.gameObject, original.transform.parent);
            copy.name = ObjectName;
            var copyRect = copy.GetComponent<RectTransform>();
            var originalRect = original.GetComponent<RectTransform>();
            copyRect.anchoredPosition = originalRect.anchoredPosition + new Vector2(130f, 0f);
            button = copy.GetComponent<Button>();
            button.onClick = new Button.ButtonClickedEvent();
        }
        else
        {
            button = existing.GetComponent<Button>();
        }
        if (button != null && Hooked.Add(button.GetInstanceID()))
        {
            var captured = station;
            button.onClick.AddListener(() => Run(captured));
        }
        Refresh(station);
    }

    internal static void Refresh(RepairStation station)
    {
        if (station == null)
            return;
        var originalField = AccessTools.Field(typeof(RepairStation), "repairConfirmButton");
        var original = originalField != null ? originalField.GetValue(station) as Button : null;
        if (original == null)
            return;
        var existing = original.transform.parent.Find(ObjectName);
        if (existing == null)
            return;
        var button = existing.GetComponent<Button>();
        var label = existing.GetComponentInChildren<TextMeshProUGUI>();
        int cost = Quote(out _);
        if (label != null)
            label.text = "Repair All (" + cost + ")";
        var player = StaticInstance<GameManager>.Instance.PlayerUnit;
        bool afford = player != null && player.Stats.GetCoins(StaticInstance<GameManager>.Instance.InSafeZone) >= cost;
        button.interactable = cost > 0 && afford;
    }

    static int Quote(out List<InventoryItem> items)
    {
        items = new List<InventoryItem>();
        var ui = StaticInstance<UIManager>.Instance;
        if (ui == null || ui.InventoryUI == null || ui.InventoryUI.PlayerBackpackGrid == null)
            return 0;
        int cost = 0;
        foreach (var item in ui.InventoryUI.PlayerBackpackGrid.AllItems())
        {
            if (item == null || !item.IsRepairable || item.DurabilityNormalized >= 0.999f)
                continue;
            items.Add(item);
            cost += (int)(item.PriceBase * 0.2f * (1f - item.DurabilityNormalized));
        }
        return Mathf.Max(0, cost);
    }

    static void Run(RepairStation station)
    {
        int cost = Quote(out var items);
        if (items.Count == 0 || cost <= 0)
            return;
        var player = StaticInstance<GameManager>.Instance.PlayerUnit;
        if (player.Stats.GetCoins(StaticInstance<GameManager>.Instance.InSafeZone) < cost)
            return;
        int remaining = cost;
        if (StaticInstance<GameManager>.Instance.InSafeZone)
        {
            int stash = player.Stats.GetCoinsStash();
            int fromStash = Mathf.Min(stash, remaining);
            if (fromStash > 0)
            {
                player.Stats.ModifyCoinsStash(-fromStash, station);
                remaining -= fromStash;
            }
        }
        if (remaining > 0)
            player.Stats.ModifyCoins(-remaining, station);
        foreach (var item in items)
            item.ModifyDurability(item.DurabilityMax);
        Refresh(station);
        var ui = StaticInstance<UIManager>.Instance;
        if (ui != null)
            ui.ShowSmallAlert("Repaired " + items.Count + " for " + cost + ".", 2.5f, UISounds.UI_Repair);
    }
}

[HarmonyPatch(typeof(RepairStation), "UIOnEnter")]
static class EnterPatch
{
    static void Postfix(RepairStation __instance) => RepairAllButton.Ensure(__instance);
}

[HarmonyPatch(typeof(RepairStation), "UpdateCombination")]
static class CombinationPatch
{
    static void Postfix(RepairStation __instance) => RepairAllButton.Refresh(__instance);
}
