using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.UI;
using PerfectRandom.Sulfur.Core.Units;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SulfurMods.MerchantRefresh;

[BepInPlugin("sulfur.mods.merchantrefresh", "Sulfur Merchant Refresh", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    internal static ConfigEntry<KeyCode> Key;
    internal static ConfigEntry<int> Cost;
    internal static Plugin Instance;
    static MethodInfo SetupItems;

    private void Awake()
    {
        Instance = this;
        Key = Config.Bind("General", "Key", KeyCode.N, "Reroll the open merchant. N is used so R can keep rotating items.");
        Cost = Config.Bind("General", "Cost", 250, "Coins charged per refresh. Stash is spent first in a safe zone.");
        SetupItems = AccessTools.Method(typeof(ShopKeeper), "SetupItems");
        Logger.LogInfo("Merchant refresh loaded. Default key is N, cost 250.");
    }

    private void Update()
    {
        if (!Keys.Down(Key.Value) || SetupItems == null)
            return;
        var ui = StaticInstance<UIManager>.Instance;
        if (ui == null || ui.InventoryUI == null || !ui.InventoryUI.InShopUI)
            return;
        var npc = ui.InventoryUI.CurrentShopKeeper;
        if (npc == null || npc.Shop == null)
            return;

        var shop = npc.Shop;
        var player = StaticInstance<GameManager>.Instance.PlayerUnit;
        int cost = Mathf.Max(0, Cost.Value);
        if (player.Stats.GetCoins(StaticInstance<GameManager>.Instance.InSafeZone) < cost)
        {
            Alerts.ShowOnce("Not enough coins.");
            return;
        }

        bool hidden = false;
        var hiddenField = AccessTools.Field(typeof(ShopKeeper), "hiddenActivated");
        if (hiddenField != null)
            hidden = (bool)hiddenField.GetValue(shop);

        if (shop.Grid != null)
            shop.Grid.RemoveAllItems();

        object data = AccessTools.Property(typeof(ShopKeeper), "inventoryData").GetValue(shop, null);
        var active = AccessTools.Field(data.GetType(), hidden ? "itemsHidden" : "items").GetValue(data) as IList;
        active?.Clear();

        if (cost > 0)
            Coins.Pay(player, cost);
        SetupItems.Invoke(shop, null);
        Alerts.ShowOnce("Stock refreshed.");
    }
}

static class Coins
{
    internal static void Pay(Unit player, int cost)
    {
        int remaining = cost;
        if (StaticInstance<GameManager>.Instance.InSafeZone)
        {
            int stash = player.Stats.GetCoinsStash();
            int fromStash = Mathf.Min(stash, remaining);
            if (fromStash > 0)
            {
                player.Stats.ModifyCoinsStash(-fromStash, Plugin.Instance);
                remaining -= fromStash;
            }
        }
        if (remaining > 0)
            player.Stats.ModifyCoins(-remaining, Plugin.Instance);
    }
}

static class Alerts
{
    internal static void ShowOnce(string text)
    {
        var ui = StaticInstance<UIManager>.Instance;
        if (ui == null)
            return;
        var field = AccessTools.Field(typeof(UIManager), "queuedSmallAlerts");
        if (field != null && field.GetValue(ui) is List<AnnouncementText> queue)
            queue.RemoveAll(entry => entry != null && entry.text == text);
        ui.ShowSmallAlert(text, 2.5f, UISounds.None);
    }
}

static class Keys
{
    internal static bool Down(KeyCode code)
    {
        var keyboard = Keyboard.current;
        if (keyboard == null || !TryMap(code, out Key key))
            return false;
        return keyboard[key].wasPressedThisFrame;
    }

    static bool TryMap(KeyCode code, out Key key)
    {
        if (code >= KeyCode.A && code <= KeyCode.Z)
        {
            key = (Key)((int)Key.A + (code - KeyCode.A));
            return true;
        }
        if (code >= KeyCode.Alpha1 && code <= KeyCode.Alpha9)
        {
            key = (Key)((int)Key.Digit1 + (code - KeyCode.Alpha1));
            return true;
        }
        if (code == KeyCode.Alpha0)
        {
            key = Key.Digit0;
            return true;
        }
        if (code >= KeyCode.F1 && code <= KeyCode.F12)
        {
            key = (Key)((int)Key.F1 + (code - KeyCode.F1));
            return true;
        }
        if (code >= KeyCode.Keypad0 && code <= KeyCode.Keypad9)
        {
            key = (Key)((int)Key.Numpad0 + (code - KeyCode.Keypad0));
            return true;
        }
        switch (code)
        {
            case KeyCode.Space: key = Key.Space; return true;
            case KeyCode.Return: key = Key.Enter; return true;
            case KeyCode.KeypadEnter: key = Key.NumpadEnter; return true;
            case KeyCode.Escape: key = Key.Escape; return true;
            case KeyCode.Tab: key = Key.Tab; return true;
            case KeyCode.Backspace: key = Key.Backspace; return true;
            case KeyCode.LeftShift: key = Key.LeftShift; return true;
            case KeyCode.RightShift: key = Key.RightShift; return true;
            case KeyCode.LeftControl: key = Key.LeftCtrl; return true;
            case KeyCode.RightControl: key = Key.RightCtrl; return true;
            case KeyCode.LeftAlt: key = Key.LeftAlt; return true;
            case KeyCode.RightAlt: key = Key.RightAlt; return true;
            case KeyCode.UpArrow: key = Key.UpArrow; return true;
            case KeyCode.DownArrow: key = Key.DownArrow; return true;
            case KeyCode.LeftArrow: key = Key.LeftArrow; return true;
            case KeyCode.RightArrow: key = Key.RightArrow; return true;
            default:
                key = Key.None;
                return false;
        }
    }
}
