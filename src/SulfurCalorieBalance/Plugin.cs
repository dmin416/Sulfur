using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.Stats;
using PerfectRandom.Sulfur.Core.UI.ItemDescription;
using PerfectRandom.Sulfur.Core.Units;
using UnityEngine;

namespace SulfurCalorieBalance
{
    /// <summary>
    /// Balance overhaul, two parts:
    ///  1) Player health: vanilla player Stat_MaxHealth is 100 while even a
    ///     goblin has 120-200 and bosses 8,000-30,000. The Player UnitSO base
    ///     MaxHealth is scaled (default x10 -> 1000). Enemies are untouched.
    ///  2) Food healing: vanilla healing totals are effectively arbitrary
    ///     (Seaweed 5 HP, Surstromming 50 HP for 50s, a whole Pizza 250 HP
    ///     against a 100 HP cap). Every consumable's HealthRegen total is
    ///     re-derived from an approximate real-world calorie value:
    ///         heal HP = kcal * HpPerCalorie      (default 0.5, i.e. 2 kcal = 1 HP)
    ///     A banana (~105 kcal) heals ~52 HP, a cheeseburger (~550) ~275 HP,
    ///     a whole pizza (~2200) ~1100 HP (capped by the 1000 HP bar).
    ///     Non-food medicine (First Aid Kit, Health Potion) is not calorie
    ///     based: it keeps its vanilla share of the health bar, scaled by the
    ///     same health multiplier (80 HP potion -> 800 HP at x10).
    /// Healing duration is unchanged, so HP/second scales with the total.
    /// All numbers are config-editable; per-item calories can be overridden
    /// with a calories.csv placed next to this DLL or in BepInEx/config.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "sulfur.mods.calorie-balance";
        public const string PluginName = "SULFUR Calorie Balance";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<float> PlayerHealthMultiplier;
        internal static ConfigEntry<float> HpPerCalorie;
        internal static ConfigEntry<float> MedicalHealMultiplier;
        internal static ConfigEntry<bool> LogFoodChanges;

        internal static readonly Dictionary<string, float> Calories =
            new Dictionary<string, float>(CalorieTable.Defaults);

        internal static readonly HashSet<string> MedicalItems = new HashSet<string>
        {
            "Item_FirstAidKit", "Item_HealthPotion"
        };

        private void Awake()
        {
            Log = Logger;
            PlayerHealthMultiplier = Config.Bind("Player", "PlayerHealthMultiplier", 10f,
                "Player base MaxHealth multiplier. Vanilla is 100 HP; 10 = 1000 HP. Enemies unaffected. Requires restart.");
            HpPerCalorie = Config.Bind("Food", "HpPerCalorie", 0.5f,
                "HP healed per kcal of food. 0.5 = a 500 kcal meal heals 250 HP (25% of the 1000 HP bar).");
            MedicalHealMultiplier = Config.Bind("Food", "MedicalHealMultiplier", 10f,
                "First Aid Kit / Health Potion keep their vanilla fraction of the bar, scaled by this (default same as health multiplier).");
            LogFoodChanges = Config.Bind("Food", "LogFoodChanges", true,
                "Log each consumable's kcal and new heal total the first time it is seen.");

            LoadCalorieOverrides();
            new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo($"Calorie Balance active: player MaxHealth x{PlayerHealthMultiplier.Value} " +
                        $"(100 -> {100f * PlayerHealthMultiplier.Value:0} HP), food heals {HpPerCalorie.Value} HP/kcal, " +
                        $"{Calories.Count} calorie entries.");
        }

        private void LoadCalorieOverrides()
        {
            string[] candidates =
            {
                Path.Combine(Path.GetDirectoryName(Info.Location) ?? string.Empty, "calories.csv"),
                Path.Combine(Paths.ConfigPath, "sulfur.calorie-balance.calories.csv")
            };
            foreach (string path in candidates)
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    int loaded = 0;
                    foreach (string line in File.ReadAllLines(path))
                    {
                        if (string.IsNullOrWhiteSpace(line) || line.StartsWith("ItemAsset")) continue;
                        string[] parts = line.Split(',');
                        if (parts.Length < 3) continue;
                        if (float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float kcal))
                        {
                            Calories[parts[0].Trim()] = kcal;
                            loaded++;
                        }
                    }
                    Log.LogInfo($"Loaded {loaded} calorie overrides from {path}");
                }
                catch (Exception ex)
                {
                    Log.LogWarning($"Could not read calorie overrides from {path}: {ex.Message}");
                }
            }
        }

        private static readonly HashSet<int> RewrittenItems = new HashSet<int>();

        /// <summary>Re-derive an item's HealthRegen heal total from calories (idempotent).</summary>
        internal static void RewriteFood(ItemDefinition item)
        {
            if (item == null || item.buffsOnConsume == null) return;
            if (!RewrittenItems.Add(item.GetInstanceID())) return;

            foreach (BuffDefinition buff in item.buffsOnConsume)
            {
                if (buff == null || buff.attributeId != EntityAttributes.Stat_HealthRegen) continue;
                if (buff.duration <= 0f) continue;

                float vanillaTotal = buff.totalValueOverride != 0f
                    ? buff.totalValueOverride
                    : buff.value * buff.duration;

                float newTotal;
                string basis;
                if (MedicalItems.Contains(item.name))
                {
                    newTotal = vanillaTotal * MedicalHealMultiplier.Value;
                    basis = $"medical, vanilla {vanillaTotal:0} x{MedicalHealMultiplier.Value}";
                }
                else if (Calories.TryGetValue(item.name, out float kcal))
                {
                    newTotal = kcal * HpPerCalorie.Value;
                    basis = $"{kcal:0} kcal x{HpPerCalorie.Value} HP/kcal";
                }
                else
                {
                    // Unknown (e.g. future game update): keep vanilla share of the bigger bar.
                    newTotal = vanillaTotal * PlayerHealthMultiplier.Value;
                    basis = $"no calorie entry, vanilla {vanillaTotal:0} x{PlayerHealthMultiplier.Value}";
                }

                buff.totalValueOverride = newTotal;
                buff.value = newTotal / buff.duration;
                if (LogFoodChanges.Value)
                    Log.LogInfo($"{item.name} ({item.displayName}): heals {newTotal:0} HP over {buff.duration:0.#}s [{basis}; vanilla {vanillaTotal:0} HP]");
            }
        }
    }

    /// <summary>Scale the Player UnitSO's base MaxHealth exactly once per session.</summary>
    [HarmonyPatch(typeof(EntityStats), nameof(EntityStats.SetBaseAttributes))]
    internal static class PlayerHealthPatch
    {
        private static readonly HashSet<int> ScaledAssets = new HashSet<int>();

        private static void Prefix(UnitSO unit, Unit owner)
        {
            if (unit == null || owner == null || !owner.isPlayer) return;
            if (!ScaledAssets.Add(unit.GetInstanceID())) return;

            List<AttributeContainerNew> attrs = unit.characterBaseAttributesNew;
            if (attrs == null) return;
            for (int i = 0; i < attrs.Count; i++)
            {
                AttributeContainerNew entry = attrs[i];
                if (entry.type != EntityAttributes.Stat_MaxHealth) continue;
                float vanilla = entry.value;
                entry.value = vanilla * Plugin.PlayerHealthMultiplier.Value;
                attrs[i] = entry;
                Plugin.Log.LogInfo($"Player MaxHealth: {vanilla:0} -> {entry.value:0} HP (x{Plugin.PlayerHealthMultiplier.Value}). Enemies unchanged.");
            }
        }
    }

    /// <summary>Apply calorie healing when an item is actually consumed.</summary>
    [HarmonyPatch(typeof(Unit), nameof(Unit.ConsumeItem), typeof(ItemDefinition))]
    internal static class ConsumeItemPatch
    {
        private static void Prefix(ItemDefinition item) { Plugin.RewriteFood(item); }
    }

    /// <summary>Make the hover preview show the calorie-based total.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.SetHoveredConsumable))]
    internal static class HoverPreviewPatch
    {
        private static void Prefix(ItemDefinition item) { Plugin.RewriteFood(item); }
    }

    /// <summary>Make inventory tooltips show the calorie-based total.</summary>
    [HarmonyPatch(typeof(ItemDescription), nameof(ItemDescription.Setup), typeof(ItemDefinition))]
    internal static class ItemDescriptionItemPatch
    {
        private static void Prefix(ItemDefinition item) { Plugin.RewriteFood(item); }
    }

    [HarmonyPatch(typeof(ItemDescription), nameof(ItemDescription.Setup), typeof(InventoryItem))]
    internal static class ItemDescriptionInventoryPatch
    {
        private static void Prefix(InventoryItem inventoryItem)
        {
            if (inventoryItem != null) Plugin.RewriteFood(inventoryItem.itemDefinition);
        }
    }

    /// <summary>
    /// FloatingCardManager.RemovePlayerModifiers hardcodes CurrentHealth = 100
    /// (vanilla full HP). After the x10 health change that would leave the
    /// player at 100/1000; restore to the (new) full bar instead.
    /// </summary>
    [HarmonyPatch(typeof(FloatingCardManager), nameof(FloatingCardManager.RemovePlayerModifiers))]
    internal static class RemovePlayerModifiersPatch
    {
        private static void Postfix()
        {
            try
            {
                Unit player = StaticInstance<GameManager>.Instance != null
                    ? StaticInstance<GameManager>.Instance.PlayerUnit
                    : null;
                if (player != null && player.Stats != null)
                {
                    player.Stats.SetStatus(EntityAttributes.Status_CurrentHealth,
                        player.Stats.GetAttribute(EntityAttributes.Stat_MaxHealth));
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"RemovePlayerModifiers fix failed: {ex.Message}");
            }
        }
    }
}
