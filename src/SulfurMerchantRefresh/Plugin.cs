using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.UI;
using PerfectRandom.Sulfur.Core.Units;
using UnityEngine.InputSystem;

namespace SulfurMerchantRefresh
{
    /// <summary>
    /// Pay a merchant to restock.
    ///
    /// Vanilla merchant stock is generated once and only regenerated when the
    /// game tick advances (ShopKeeper.LoadItemsFromSave) or under specific
    /// church conditions, so you normally cannot reroll a shop.
    ///
    /// While a shop is open, press the refresh key (default N): the merchant
    /// charges RefreshCostCoins (default 250, paid from your stash first when
    /// in the church safe zone, like the Workbench does) and generates a
    /// completely new inventory from their vendor table (always-available
    /// items + the usual number of random picks, including checkpoint
    /// expansions and secret/hidden stock when in the hidden shop).
    /// The new stock is saved immediately and also persists on shop exit,
    /// exactly like a natural reroll.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "sulfur.mods.merchant-refresh";
        public const string PluginName = "SULFUR Merchant Refresh";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        private static ConfigEntry<string> KeyConfig;
        private static ConfigEntry<int> RefreshCost;
        private static ConfigEntry<bool> NotifyOncePerVisit;
        private static Key _key = Key.N;
        private static int _notifiedShopId;
        private static float _lastFailAlertAt = -10f;

        private void Awake()
        {
            Log = Logger;
            KeyConfig = Config.Bind("Merchant", "RefreshKey", "N",
                "Unity InputSystem Key name pressed while a shop is open to pay for a restock.");
            RefreshCost = Config.Bind("Merchant", "RefreshCostCoins", 250,
                "Coins paid to the merchant per inventory refresh. 0 = free.");
            NotifyOncePerVisit = Config.Bind("Merchant", "NotifyOncePerShopVisit", true,
                "Show the 'Merchant restocked' notification only once per shop visit, no matter how many times you restock. Later restocks in the same visit still happen (and charge), silently.");
            if (!Enum.TryParse(KeyConfig.Value, true, out _key))
            {
                Log.LogWarning($"Unknown key '{KeyConfig.Value}', falling back to N.");
                _key = Key.N;
            }
            Log.LogInfo($"Merchant Refresh active: press {_key} in a shop to restock for {RefreshCost.Value} coins.");
        }

        private void Update()
        {
            try
            {
                // Reset the once-per-visit notification when no shop is open.
                var ui = StaticInstance<UIManager>.Instance != null ? StaticInstance<UIManager>.Instance.InventoryUI : null;
                if (ui == null || !ui.InShopUI) _notifiedShopId = 0;

                if (Keyboard.current == null || Keyboard.current[_key] == null) return;
                if (!Keyboard.current[_key].wasPressedThisFrame) return;
                TryRefresh();
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Merchant refresh failed: {ex.Message}");
            }
        }

        private static void Alert(string text)
        {
            if (StaticInstance<UIManager>.Instance != null)
                StaticInstance<UIManager>.Instance.ShowSmallAlert(text);
            Log.LogInfo(text);
        }

        private static void TryRefresh()
        {
            if (StaticInstance<GameManager>.Instance == null || StaticInstance<UIManager>.Instance == null) return;
            var inventoryUI = StaticInstance<UIManager>.Instance.InventoryUI;
            if (inventoryUI == null || !inventoryUI.InShopUI) return;

            ShopKeeper shop = inventoryUI.CurrentServiceStation as ShopKeeper;
            if (shop == null) return;

            var player = StaticInstance<GameManager>.Instance.PlayerUnit;
            if (player == null || player.Stats == null) return;

            bool inSafeZone = StaticInstance<GameManager>.Instance.InSafeZone;
            int cost = RefreshCost.Value;
            if (cost > 0)
            {
                if (player.Stats.GetCoins(inSafeZone) < cost)
                {
                    if (UnityEngine.Time.unscaledTime - _lastFailAlertAt > 3f)
                    {
                        _lastFailAlertAt = UnityEngine.Time.unscaledTime;
                        Alert($"Restocking costs {cost} coins.");
                    }
                    return;
                }
                // Same payment split as the Workbench: stash first in the safe zone.
                int fromBag = cost;
                if (inSafeZone)
                {
                    int fromStash = Math.Min(player.Stats.GetCoinsStash(), cost);
                    if (fromStash > 0) player.Stats.ModifyCoinsStash(-fromStash, "Merchant Refresh");
                    fromBag -= fromStash;
                }
                if (fromBag > 0) player.Stats.ModifyCoins(-fromBag, "Merchant Refresh");
            }

            // Clearing the cached stock makes vanilla SetupItems() generate a
            // fresh inventory from the vendor table and save it.
            var traverse = Traverse.Create(shop);
            var data = traverse.Property("inventoryData").GetValue<ShopKeeper.ShopKeeperInventoryData>();
            bool hidden = traverse.Field("hiddenActivated").GetValue<bool>();
            if (hidden) data.itemsHidden.Clear();
            else data.items.Clear();
            traverse.Method("SetupItems").GetValue();

            // One notification per shop visit; repeat restocks are silent.
            if (!NotifyOncePerVisit.Value || _notifiedShopId != shop.GetInstanceID())
            {
                _notifiedShopId = shop.GetInstanceID();
                Alert(cost > 0 ? $"Merchant restocked (-{cost} coins)." : "Merchant restocked.");
            }
            else
            {
                Log.LogInfo($"Merchant restocked again (-{cost} coins), notification suppressed (once per visit).");
            }
        }
    }
}
