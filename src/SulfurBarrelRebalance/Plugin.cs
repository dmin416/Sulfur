using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.CharacterStats;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.Stats;
using PerfectRandom.Sulfur.Core.UI.ItemDescription;

namespace SulfurBarrelRebalance
{
    /// <summary>
    /// Rebalances barrel extensions into a clean tier ladder:
    ///   2" Barrel Extension:        Spread -25%, Damage +20%, Projectile speed +20%
    ///   4" Barrel Extension:        Spread -40%, Damage +40%, Projectile speed +40%
    ///   Shrouded Barrel Extension:  Spread -30%, Damage +30%, Projectile speed +30%
    ///                               and Max Durability +30%
    ///   6" Barrel Extension:        Spread -60%, Damage +60%, Projectile speed +60%
    /// Vanilla side effects that were not part of the spec are preserved by
    /// carrying over any other original modifiers (the 2"/4"/6" movement
    /// speed penalties of -5%/-10%/-20% while held, Shrouded's durability).
    /// The Improvised Barrel Extension is left exactly as vanilla.
    ///
    /// Implemented by rewriting the attachment's modifiersOnAttachToItem the
    /// first time it is attached or its tooltip is shown. Damage is a
    /// PercentAdd modifier on the weapon's Damage stat (base = caliber
    /// damage), projectile speed is a Flat addition to ProjectileTimeScale
    /// (base 1.0, same style as vanilla barrels), spread is PercentAdd.
    /// Safe for existing saves: modifier removal is by source id, not value.
    /// Percentages are configurable per tier.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "sulfur.mods.barrel-rebalance";
        public const string PluginName = "SULFUR Barrel Rebalance";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<float> Tier1Percent;
        internal static ConfigEntry<float> Tier2Percent;
        internal static ConfigEntry<float> Tier3Percent;

        private static readonly Dictionary<string, int> BarrelTiers = new Dictionary<string, int>
        {
            { "Attachment_BarrelExtension2i", 1 },
            { "Attachment_BarrelExtension4i", 2 },
            { "Attachment_ShroudedBarrelExtension", 4 },
            { "Attachment_BarrelExtension6i", 3 },
        };

        private static readonly HashSet<int> Rewritten = new HashSet<int>();

        private void Awake()
        {
            Log = Logger;
            Tier1Percent = Config.Bind("Barrels", "Tier1Percent_2inch", 20f,
                "2-inch barrel: Damage +% and projectile speed +%. Spread reduction is 25% (see Tier1SpreadPercent).");
            Tier2Percent = Config.Bind("Barrels", "Tier2Percent_4inch", 40f,
                "4-inch barrel: Damage +%, projectile speed +%, spread -%.");
            ShroudedPercent = Config.Bind("Barrels", "ShroudedPercent", 30f,
                "Shrouded barrel: Damage +%, projectile speed +%, spread -%.");
            ShroudedDurabilityPercent = Config.Bind("Barrels", "ShroudedDurabilityPercent", 30f,
                "Shrouded barrel: Max Durability +% (vanilla was +20%).");
            Tier3Percent = Config.Bind("Barrels", "Tier3Percent_6inch", 60f,
                "6-inch barrel: Damage +%, projectile speed +%, spread -%.");
            Tier1SpreadPercent = Config.Bind("Barrels", "Tier1SpreadPercent_2inch", 25f,
                "2-inch barrel spread reduction %.");
            new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("Barrel Rebalance active: 2\" -25% spread/+20% dmg/+20% speed, 4\" -40%/+40%/+40%, Shrouded -30%/+30%/+30%, 6\" -60%/+60%/+60%.");
        }

        internal static ConfigEntry<float> Tier1SpreadPercent;
        internal static ConfigEntry<float> ShroudedPercent;
        internal static ConfigEntry<float> ShroudedDurabilityPercent;

        internal static void Rewrite(ItemDefinition item)
        {
            if (item == null || item.modifiersOnAttachToItem == null) return;
            if (!BarrelTiers.TryGetValue(item.name, out int tier)) return;
            if (!Rewritten.Add(item.GetInstanceID())) return;

            float pct, spreadPct;
            switch (tier)
            {
                case 1: pct = Tier1Percent.Value / 100f; spreadPct = Tier1SpreadPercent.Value / 100f; break;
                case 2: pct = Tier2Percent.Value / 100f; spreadPct = pct; break;
                case 4: pct = ShroudedPercent.Value / 100f; spreadPct = pct; break;
                default: pct = Tier3Percent.Value / 100f; spreadPct = pct; break;
            }

            List<ItemModifierContainer> rewritten = new List<ItemModifierContainer>
            {
                new ItemModifierContainer { attribute = ItemAttributes.Spread, modType = StatModType.PercentAdd, value = -spreadPct },
                new ItemModifierContainer { attribute = ItemAttributes.Damage, modType = StatModType.PercentAdd, value = pct },
                new ItemModifierContainer { attribute = ItemAttributes.ProjectileTimeScale, modType = StatModType.Flat, value = pct },
            };
            // Preserve any other vanilla modifiers (move-speed penalties).
            // Shrouded's durability bonus is overridden to the configured value.
            foreach (ItemModifierContainer original in item.modifiersOnAttachToItem)
            {
                if (original.attribute == ItemAttributes.Spread ||
                    original.attribute == ItemAttributes.Damage ||
                    original.attribute == ItemAttributes.ProjectileTimeScale)
                    continue;
                if (tier == 4 && original.attribute == ItemAttributes.MaxDurability)
                {
                    rewritten.Add(new ItemModifierContainer
                    {
                        attribute = ItemAttributes.MaxDurability,
                        modType = StatModType.PercentAdd,
                        value = ShroudedDurabilityPercent.Value / 100f
                    });
                    continue;
                }
                rewritten.Add(original);
            }
            item.modifiersOnAttachToItem = rewritten;
            Log.LogInfo($"{item.name}: Spread -{spreadPct * 100:0}%, Damage +{pct * 100:0}%, Projectile speed +{pct * 100:0}% (other vanilla modifiers kept).");
        }
    }

    [HarmonyPatch(typeof(InventoryItem), nameof(InventoryItem.AddAttachment))]
    internal static class AddAttachmentPatch
    {
        private static void Prefix(ItemDefinition attachmentItem) { Plugin.Rewrite(attachmentItem); }
    }

    [HarmonyPatch(typeof(ItemDescription), nameof(ItemDescription.Setup), typeof(ItemDefinition))]
    internal static class ItemDescriptionPatch
    {
        private static void Prefix(ItemDefinition item) { Plugin.Rewrite(item); }
    }
}
