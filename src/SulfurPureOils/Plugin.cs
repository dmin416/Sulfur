using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.Stats;

namespace SulfurPureOils
{
    /// <summary>
    /// Removes the negative parts of oils (and scrolls, which are the same
    /// EnchantmentDefinition system).
    ///
    /// Vanilla oils routinely mix a buff with a drawback, e.g. Black Friday Oil
    /// gives +200% projectiles but Damage -30% / MaxDurability -30%, and
    /// Holy Purge adds DurabilityLoss x2.5 and EnchantmentSelfDamage 10.
    /// This mod strips every detrimental modifier from an enchantment
    /// definition at the moment it is applied (EnchantmentDefinition.
    /// RegisterAppliedBy runs before the modifiers are added, on both the
    /// normal and the random-oil paths), so oils become pure buffs.
    /// Already-enchanted weapons in a save are re-applied clean on load,
    /// because loading a weapon calls AddEnchantment again.
    ///
    /// "Detrimental" is judged by attribute direction:
    ///  - normally-higher-is-better attributes with a negative value
    ///    (Damage -30%, RPM -15%, MoveSpeed -20%, ...), and
    ///  - lower-is-better attributes with a positive value
    ///    (Spread, KickMultiplier, DurabilityLoss, EnchantmentSelfDamage,
    ///    EnchantmentDurabilityCost, ammo-consumption chances, Disable* loot/ads).
    /// Beneficial entries (including e.g. Spread -30% or ConsumeAmmoChance
    /// reductions, which are good) are kept untouched.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "sulfur.mods.pure-oils";
        public const string PluginName = "SULFUR Pure Oils";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> RemoveDurabilityCost;
        internal static ConfigEntry<bool> LogRemovedModifiers;

        // Attributes where a positive value hurts the player.
        private static readonly HashSet<ItemAttributes> LowerIsBetter = new HashSet<ItemAttributes>
        {
            ItemAttributes.DurabilityLoss,
            ItemAttributes.KickMultiplier,
            ItemAttributes.Spread,
            ItemAttributes.EnchantmentDurabilityCost,
            ItemAttributes.EnchantmentSelfDamage,
            ItemAttributes.ProjectileDrag,
            ItemAttributes.ProjectileGravityFactor,
            ItemAttributes.ConsumeAmmoChance,
            ItemAttributes.ConsumeExtraAmmoChance,
            ItemAttributes.DisableADS,
            ItemAttributes.DisableLootMoney,
            ItemAttributes.DisableLootOrgans,
        };

        private static readonly HashSet<int> Cleaned = new HashSet<int>();

        private void Awake()
        {
            Log = Logger;
            RemoveDurabilityCost = Config.Bind("Oils", "RemoveDurabilityCost", true,
                "Enchantments also stop causing per-shot durability loss (vanilla charges 1 + EnchantmentDurabilityCost per shot for them).");
            LogRemovedModifiers = Config.Bind("Oils", "LogRemovedModifiers", true,
                "Log every removed drawback modifier to BepInEx/LogOutput.log.");
            new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("Pure Oils active: oils/scrolls keep only their beneficial modifiers.");
        }

        internal static bool IsDetrimental(ItemModifierContainer mod)
        {
            if (mod == null || mod.value == 0f) return false;
            return LowerIsBetter.Contains(mod.attribute) ? mod.value > 0f : mod.value < 0f;
        }

        internal static void Sanitize(EnchantmentDefinition definition)
        {
            if (definition == null || definition.modifiersApplied == null) return;
            if (!Cleaned.Add(definition.GetInstanceID())) return;

            List<ItemModifierContainer> kept = new List<ItemModifierContainer>();
            foreach (ItemModifierContainer mod in definition.modifiersApplied)
            {
                if (IsDetrimental(mod))
                {
                    if (LogRemovedModifiers.Value)
                        Log.LogInfo($"{definition.name}: removed drawback {mod.attribute} {mod.value} ({mod.modType})");
                }
                else
                {
                    kept.Add(mod);
                }
            }
            definition.modifiersApplied = kept;
            if (RemoveDurabilityCost.Value && definition.CostsDurability)
            {
                definition.CostsDurability = false;
                if (LogRemovedModifiers.Value)
                    Log.LogInfo($"{definition.name}: removed per-shot durability cost");
            }
        }
    }

    [HarmonyPatch(typeof(EnchantmentDefinition), nameof(EnchantmentDefinition.RegisterAppliedBy))]
    internal static class RegisterAppliedByPatch
    {
        private static void Prefix(EnchantmentDefinition __instance)
        {
            Plugin.Sanitize(__instance);
        }
    }
}
