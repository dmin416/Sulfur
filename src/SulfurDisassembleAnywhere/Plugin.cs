using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SulfurMods.DisassembleAnywhere;

[BepInPlugin("sulfur.mods.disassemble", "Sulfur Disassemble Anywhere", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    internal static ConfigEntry<KeyCode> Key;
    internal static ConfigEntry<int> Cost;
    internal static ConfigEntry<bool> DestroyWeapon;
    internal static Plugin Instance;

    private void Awake()
    {
        Instance = this;
        Key = Config.Bind("General", "Key", KeyCode.K, "Strip attachments and oils from the hovered item.");
        Cost = Config.Bind("General", "Cost", 0, "Flat coin cost. Stash is spent first in a safe zone. 0 is free.");
        DestroyWeapon = Config.Bind("General", "DestroyWeapon", false, "Destroy the weapon after it has been stripped. Default keeps the bare weapon.");
        Logger.LogInfo("Disassemble anywhere loaded. Default key is K.");
    }

    private void Update()
    {
        if (!Keys.Down(Key.Value))
            return;
        var ui = StaticInstance<UIManager>.Instance;
        if (ui == null || ui.InventoryUI == null)
            return;
        var item = ui.InventoryUI.hoveredOrNavigatedInventoryItem;
        if (item == null || !item.OwnedByPlayer)
            return;

        var returns = new List<ItemDefinition>();
        if (item.attachments != null)
        {
            foreach (var attachment in item.attachments)
            {
                if (attachment != null)
                    returns.Add(attachment);
            }
        }
        int oils = 0;
        int missingOils = 0;
        if (item.enchantments != null)
        {
            foreach (var enchantment in item.enchantments)
            {
                if (enchantment == null)
                    continue;
                if (enchantment.ItemThatAppliedThis != null)
                {
                    returns.Add(enchantment.ItemThatAppliedThis);
                    oils++;
                }
                else
                    missingOils++;
            }
        }
        if (returns.Count == 0 && (item.enchantments == null || item.enchantments.Count == 0))
        {
            Alerts.ShowOnce("Nothing to strip.");
            return;
        }

        var player = StaticInstance<GameManager>.Instance.PlayerUnit;
        int cost = Mathf.Max(0, Cost.Value);
        if (cost > 0 && player.Stats.GetCoins(StaticInstance<GameManager>.Instance.InSafeZone) < cost)
        {
            Alerts.ShowOnce("Not enough coins.");
            return;
        }

        var pack = ui.InventoryUI.PlayerBackpackGrid;
        var added = new List<InventoryItem>();
        var before = new HashSet<InventoryItem>(pack.AllItems());
        foreach (var def in returns)
        {
            if (!pack.AddItem(def, false, false, null))
            {
                foreach (var spawned in added)
                    Items.DestroyItem(spawned);
                Alerts.ShowOnce("Not enough pack space. Nothing was removed.");
                return;
            }
            foreach (var candidate in pack.AllItems())
            {
                if (!before.Contains(candidate) && !added.Contains(candidate))
                    added.Add(candidate);
            }
        }

        if (cost > 0)
            Coins.Pay(player, cost);

        if (item.attachments != null)
        {
            var copy = new List<ItemDefinition>(item.attachments);
            foreach (var attachment in copy)
                item.RemoveAttachment(attachment);
        }
        if (item.enchantments != null)
        {
            var copy = new List<EnchantmentDefinition>(item.enchantments);
            foreach (var enchantment in copy)
            {
                item.RemoveEnchantment(enchantment);
                item.enchantments.Remove(enchantment);
            }
        }
        if (DestroyWeapon.Value)
            Items.DestroyItem(item);

        string note = "Stripped " + (returns.Count - oils) + " attachments and " + oils + " oils.";
        if (missingOils > 0)
            note += " " + missingOils + " oils had no item to return.";
        Alerts.ShowOnce(note);
    }
}

static class Items
{
    static readonly System.Reflection.MethodInfo Destroy = AccessTools.Method(typeof(InventoryItem), "DestroyFromInventory");

    internal static void DestroyItem(InventoryItem item)
    {
        if (item != null && Destroy != null)
            Destroy.Invoke(item, null);
    }
}

static class Coins
{
    internal static void Pay(PerfectRandom.Sulfur.Core.Units.Unit player, int cost)
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
