using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.UI;
using UnityEngine.InputSystem;

namespace SulfurDisassembleAnywhere
{
    /// <summary>
    /// Disassemble (strip) weapons anywhere, no Workbench required.
    ///
    /// Vanilla only lets the Workbench detach ATTACHMENTS, for 10% of the
    /// weapon price, at the station; oils/scrolls are permanent (the Baptismal
    /// Font destroys enchantments with no refund).
    ///
    /// With the inventory open, hover (or gamepad-navigate to) one of your
    /// weapons and press the disassemble key (default K):
    ///  - every attachment is removed and returned to your backpack,
    ///  - every enchantment is removed and the OIL or SCROLL item that applied
    ///    it is returned to your backpack (EnchantmentDefinition remembers it
    ///    via ItemThatAppliedThis),
    ///  - the weapon itself stays in place, bare (default), or is also
    ///    destroyed if DestroyWeaponAfterStrip is enabled.
    /// If the backpack cannot fit everything, nothing is removed.
    /// Only player-owned weapons can be stripped (shop/stash items are safe).
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "sulfur.mods.disassemble-anywhere";
        public const string PluginName = "SULFUR Disassemble Anywhere";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        private static ConfigEntry<string> KeyConfig;
        private static ConfigEntry<bool> DestroyWeapon;
        private static ConfigEntry<int> FlatCost;
        private static Key _key = Key.K;

        private void Awake()
        {
            Log = Logger;
            KeyConfig = Config.Bind("Disassemble", "DisassembleKey", "K",
                "Unity InputSystem Key name pressed while hovering a weapon in the inventory to disassemble it.");
            DestroyWeapon = Config.Bind("Disassemble", "DestroyWeaponAfterStrip", false,
                "Also destroy the weapon itself after its attachments/oils/scrolls are recovered. Default false: the bare weapon is kept.");
            FlatCost = Config.Bind("Disassemble", "FlatCostCoins", 0,
                "Coins charged per disassembly. 0 = free (vanilla Workbench charges 10% of weapon price for attachments only).");
            if (!Enum.TryParse(KeyConfig.Value, true, out _key))
            {
                Log.LogWarning($"Unknown key '{KeyConfig.Value}', falling back to K.");
                _key = Key.K;
            }
            Log.LogInfo($"Disassemble Anywhere active: hover a weapon and press {_key}.");
        }

        private void Update()
        {
            try
            {
                if (Keyboard.current == null || !_keyWasPressed()) return;
                TryDisassembleHovered();
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Disassemble failed: {ex.Message}");
            }
        }

        private static bool _keyWasPressed()
        {
            return Keyboard.current[_key] != null && Keyboard.current[_key].wasPressedThisFrame;
        }

        private static void Alert(string text)
        {
            if (StaticInstance<UIManager>.Instance != null)
                StaticInstance<UIManager>.Instance.ShowSmallAlert(text);
            Log.LogInfo(text);
        }

        private static void TryDisassembleHovered()
        {
            if (StaticInstance<GameManager>.Instance == null || StaticInstance<UIManager>.Instance == null) return;
            var inventoryUI = StaticInstance<UIManager>.Instance.InventoryUI;
            if (inventoryUI == null || inventoryUI.draggingItem != null) return;

            InventoryItem item = inventoryUI.hoveredOrNavigatedInventoryItem;
            if (item == null || item.itemDefinition == null) return;
            if (!item.OwnedByPlayer) return;
            if (item.weaponDefinition == null) return; // weapons only

            List<ItemDefinition> attachments = new List<ItemDefinition>(item.attachments);
            List<EnchantmentDefinition> enchantments = new List<EnchantmentDefinition>(item.enchantments);
            if (attachments.Count == 0 && enchantments.Count == 0)
            {
                Alert($"{item.itemDefinition.displayName} has no attachments or enchantments to remove.");
                return;
            }

            // What we hand back: attachments + the oil/scroll item behind each enchantment.
            List<ItemDefinition> recovered = new List<ItemDefinition>();
            recovered.AddRange(attachments);
            int unrecoverable = 0;
            foreach (EnchantmentDefinition enchantment in enchantments)
            {
                if (enchantment.ItemThatAppliedThis != null) recovered.Add(enchantment.ItemThatAppliedThis);
                else unrecoverable++;
            }

            var player = StaticInstance<GameManager>.Instance.PlayerUnit;
            if (player == null || player.Stats == null) return;

            int cost = FlatCost.Value;
            if (cost > 0 && player.Stats.GetCoins(StaticInstance<GameManager>.Instance.InSafeZone) < cost)
            {
                Alert($"Disassembly costs {cost} coins.");
                return;
            }

            // Add everything first; abort untouched if it does not fit.
            ItemGrid backpack = StaticInstance<UIManager>.Instance.PlayerBackpackGrid;
            List<ItemDefinition> added = new List<ItemDefinition>();
            foreach (ItemDefinition part in recovered)
            {
                if (backpack.AddItem(part, isPickup: true, announce: true))
                {
                    added.Add(part);
                }
                else
                {
                    foreach (ItemDefinition rollback in added) backpack.RemoveItem(rollback, "Disassemble rollback", 1);
                    Alert("Not enough backpack space to disassemble.");
                    return;
                }
            }

            if (cost > 0) player.Stats.ModifyCoins(-cost, "Disassemble Anywhere");

            foreach (ItemDefinition attachment in attachments) item.RemoveAttachment(attachment);
            foreach (EnchantmentDefinition enchantment in enchantments)
            {
                item.RemoveEnchantment(enchantment);   // removes its stat modifiers
                item.enchantments.Remove(enchantment); // vanilla RemoveEnchantment forgets the list entry
            }

            string name = item.itemDefinition.displayName;
            if (DestroyWeapon.Value) item.GetRemovedByExternalFactor("Disassembled (mod)");

            SulfurSaveState.WriteActiveToDisk();
            Alert($"Disassembled {name}: {attachments.Count} attachment(s), {enchantments.Count - unrecoverable} oil/scroll(s) recovered" +
                  (unrecoverable > 0 ? $" ({unrecoverable} enchantment(s) had no recoverable item)" : "") +
                  (DestroyWeapon.Value ? ", weapon destroyed." : "."));
        }
    }
}
