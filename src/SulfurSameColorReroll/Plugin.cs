using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.CharacterStats;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.Stats;
using PerfectRandom.Sulfur.Core.Units;
using PerfectRandom.Sulfur.Gameplay;
using UnityEngine;

namespace SulfurSameColorReroll
{
    /// <summary>
    /// Makes the oil reroll station reroll within the same oil category
    /// ("same color"), where the category is the oil's MAIN BENEFIT - what
    /// the flask color actually means in game:
    ///   light red/pink = extra projectiles, light teal/green = chance to
    ///   not consume ammo, tan/brown = damage, and so on.
    ///
    /// Verified against the game data: every loot-table oil's ItemTag
    /// matches its dominant beneficial modifier (all 18 Multishot-tagged
    /// oils carry ProjectileAmount, all 16 Recycle-tagged oils carry
    /// ConsumeAmmoChance, ...), so the category is read from the tag when
    /// present. The two damage families stay separate, as in game:
    /// OilDamageFlat (tan flasks) and OilDamagePercentage (dark-brown
    /// flasks) reroll only within themselves.
    /// Untagged oils (a handful, incl. test oils) are categorized by
    /// scanning their enchantment's beneficial modifiers instead, and
    /// anything unclassifiable falls back to the vanilla any-oil pool.
    /// By default the result also cannot be the exact same oil you put in.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "sulfur.mods.same-color-reroll";
        public const string PluginName = "SULFUR Same-Color Oil Reroll";
        public const string PluginVersion = "1.1.1";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> ExcludeSameOil;
        internal static ConfigEntry<bool> LogRerolls;

        private void Awake()
        {
            Log = Logger;
            ExcludeSameOil = Config.Bind("Reroll", "ExcludeSameOil", true,
                "A reroll never returns the exact oil you put in (when its category has alternatives).");
            LogRerolls = Config.Bind("Reroll", "LogRerolls", true,
                "Log each same-category reroll to BepInEx/LogOutput.log.");
            new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("Same-Color Oil Reroll active: the reroll station now rerolls within each oil's main-benefit category (light red = projectiles, light teal = ammo-saving, tan/brown = damage, ...).");
        }

        /// <summary>An oil's main benefit - the reroll category.</summary>
        internal enum Benefit
        {
            None,
            Projectiles,  // ProjectileAmount          (light red/pink flasks)
            AmmoSaving,   // ConsumeAmmoChance down    (light teal/green flasks)
            DamageFlat,   // Damage up, flat           (tan flasks)
            DamagePercent,// Damage up, %              (dark brown flasks)
            Crit,
            BulletSpeed,
            Bounce,
            Durability,
            Knockback,
            Penetration,
            Recoil,
            Reload,
            RPM,
            Size,
            Spread,
            Weight,
        }

        // Priority when an untagged oil has several beneficial signature
        // modifiers: the most defining effect wins. ProjectileMass (Weight)
        // and generic Damage are common filler, so they rank last.
        private static readonly Benefit[] DerivePriority =
        {
            Benefit.Projectiles, Benefit.AmmoSaving, Benefit.Penetration,
            Benefit.Bounce, Benefit.Crit, Benefit.RPM, Benefit.Reload,
            Benefit.Size, Benefit.Spread, Benefit.Recoil, Benefit.BulletSpeed,
            Benefit.Knockback, Benefit.Durability, Benefit.DamageFlat,
            Benefit.DamagePercent, Benefit.Weight,
        };

        private static readonly System.Collections.Generic.HashSet<ItemAttributes> LowerIsBetter =
            new System.Collections.Generic.HashSet<ItemAttributes>
            {
                ItemAttributes.ConsumeAmmoChance, ItemAttributes.ConsumeExtraAmmoChance,
                ItemAttributes.KickMultiplier, ItemAttributes.Spread, ItemAttributes.DurabilityLoss,
            };

        /// <summary>
        /// The category an oil rerolls within. Tagged oils use their tag
        /// (verified to match their main beneficial modifier); the flat-
        /// damage and %-damage families stay separate. Untagged oils
        /// are classified from their enchantment's beneficial modifiers.
        /// </summary>
        internal static Benefit CategoryOf(ItemDefinition item)
        {
            if (item == null) return Benefit.None;
            ItemTag tags = item.itemTags;
            if (tags.HasFlag(ItemTag.OilMultishot)) return Benefit.Projectiles;
            if (tags.HasFlag(ItemTag.OilRecycle)) return Benefit.AmmoSaving;
            if (tags.HasFlag(ItemTag.OilDamageFlat)) return Benefit.DamageFlat;
            if (tags.HasFlag(ItemTag.OilDamagePercentage)) return Benefit.DamagePercent;
            if (tags.HasFlag(ItemTag.OilCritChance)) return Benefit.Crit;
            if (tags.HasFlag(ItemTag.OilBulletSpeed)) return Benefit.BulletSpeed;
            if (tags.HasFlag(ItemTag.OilBounce)) return Benefit.Bounce;
            if (tags.HasFlag(ItemTag.OilDurability)) return Benefit.Durability;
            if (tags.HasFlag(ItemTag.OilKnockback)) return Benefit.Knockback;
            if (tags.HasFlag(ItemTag.OilPenetration)) return Benefit.Penetration;
            if (tags.HasFlag(ItemTag.OilRecoil)) return Benefit.Recoil;
            if (tags.HasFlag(ItemTag.OilReload)) return Benefit.Reload;
            if (tags.HasFlag(ItemTag.OilRPM)) return Benefit.RPM;
            if (tags.HasFlag(ItemTag.OilSize)) return Benefit.Size;
            if (tags.HasFlag(ItemTag.OilSpread)) return Benefit.Spread;
            if (tags.HasFlag(ItemTag.OilWeight)) return Benefit.Weight;

            if (!item.appliesEnchantment.IsValid) return Benefit.None;
            EnchantmentDefinition def = item.appliesEnchantment.GetAsset();
            if (def == null || def.modifiersApplied == null) return Benefit.None;

            var found = new System.Collections.Generic.HashSet<Benefit>();
            foreach (ItemModifierContainer mod in def.modifiersApplied)
            {
                Benefit benefit = BenefitOf(mod);
                if (benefit == Benefit.None) continue;
                bool good = LowerIsBetter.Contains(mod.attribute) ? mod.value < 0f : mod.value > 0f;
                if (good) found.Add(benefit);
            }
            foreach (Benefit benefit in DerivePriority)
                if (found.Contains(benefit)) return benefit;
            return Benefit.None;
        }

        private static Benefit BenefitOf(ItemModifierContainer mod)
        {
            switch (mod.attribute)
            {
                case ItemAttributes.ProjectileAmount: return Benefit.Projectiles;
                case ItemAttributes.ConsumeAmmoChance:
                case ItemAttributes.ConsumeExtraAmmoChance: return Benefit.AmmoSaving;
                case ItemAttributes.Damage:
                case ItemAttributes.DamageModifier:
                    // Flat damage and %-damage are separate categories.
                    return mod.modType == StatModType.Flat ? Benefit.DamageFlat : Benefit.DamagePercent;
                case ItemAttributes.CritChance:
                case ItemAttributes.CritChanceADS: return Benefit.Crit;
                case ItemAttributes.ProjectileTimeScale: return Benefit.BulletSpeed;
                case ItemAttributes.ProjectileBounce: return Benefit.Bounce;
                case ItemAttributes.MaxDurability:
                case ItemAttributes.DurabilityLoss: return Benefit.Durability;
                case ItemAttributes.KnockbackPower: return Benefit.Knockback;
                case ItemAttributes.ProjectilePenetration: return Benefit.Penetration;
                case ItemAttributes.KickMultiplier: return Benefit.Recoil;
                case ItemAttributes.ReloadSpeed: return Benefit.Reload;
                case ItemAttributes.RPM: return Benefit.RPM;
                case ItemAttributes.ProjectileScale: return Benefit.Size;
                case ItemAttributes.Spread: return Benefit.Spread;
                case ItemAttributes.ProjectileMass: return Benefit.Weight;
                default: return Benefit.None;
            }
        }

        internal static ItemDefinition PickSameCategory(OilRerollStation station, ItemDefinition input)
        {
            LootTable table = StaticInstance<GameManager>.Instance.Settings.LootSettings.enchantmentOilLootTable;
            Benefit category = CategoryOf(input);

            ItemDefinition result = WeightedPick(table, candidate =>
                candidate != null && candidate.IsEnchantment && candidate.AllowedInGame &&
                candidate.appliesEnchantment.IsValid && !candidate.appliesEnchantment.GetAsset().IsElemental &&
                (category == Benefit.None || CategoryOf(candidate) == category) &&
                (!ExcludeSameOil.Value || candidate != input));

            if (result == null && ExcludeSameOil.Value)
            {
                // Category had no alternative: allow returning the same oil.
                result = WeightedPick(table, candidate =>
                    candidate != null && candidate.IsEnchantment && candidate.AllowedInGame &&
                    candidate.appliesEnchantment.IsValid && !candidate.appliesEnchantment.GetAsset().IsElemental &&
                    (category == Benefit.None || CategoryOf(candidate) == category));
            }
            if (result == null)
            {
                // Last resort: vanilla behavior (any oil from the table).
                result = Traverse.Create(station).Method("GetRandomOil").GetValue<ItemDefinition>();
            }
            return result;
        }

        private static ItemDefinition WeightedPick(LootTable table, System.Func<ItemDefinition, bool> filter)
        {
            float total = 0f;
            foreach (LootEntry entry in table.entries)
                if (filter(entry.lootItem)) total += entry.lootWeight;
            if (total <= 0f) return null;

            float roll = Random.Range(0f, total);
            foreach (LootEntry entry in table.entries)
            {
                if (!filter(entry.lootItem)) continue;
                roll -= entry.lootWeight;
                if (roll <= 0f) return entry.lootItem;
            }
            return null;
        }

        internal static bool DoRerollSameCategory(OilRerollStation station)
        {
            Traverse traverse = Traverse.Create(station);
            List<InventoryItem> itemsToReroll = traverse.Field("itemsToReroll").GetValue<List<InventoryItem>>();
            int currentRerollCost = traverse.Field("currentRerollCost").GetValue<int>();
            Unit playerUnit = traverse.Field("playerUnit").GetValue<Unit>();
            ItemGrid serviceGrid = traverse.Field("serviceGrid").GetValue<ItemGrid>();

            if (itemsToReroll.Count == 0) return false;
            if (playerUnit.Stats.GetCoins(StaticInstance<GameManager>.Instance.InSafeZone) < currentRerollCost) return false;

            traverse.Field("isRerolling").SetValue(true);
            SulfurSaveState.active.REROLLSTATIONCOUNTER++;

            // Identical payment to vanilla (stash first in the safe zone).
            int fromBag = currentRerollCost;
            if (StaticInstance<GameManager>.Instance.InSafeZone)
            {
                int fromStash = Mathf.Min(playerUnit.Stats.GetCoinsStash(), currentRerollCost);
                if (fromStash > 0) playerUnit.Stats.ModifyCoinsStash(-fromStash, station);
                fromBag -= fromStash;
            }
            if (fromBag > 0) playerUnit.Stats.ModifyCoins(-fromBag, station);

            traverse.Field("changesToFlushToDisk_OnConsole").SetValue(true);
            station.OnServicePerformedEffects();
            if (StaticInstance<SoundBankUI>.Instance != null)
                StaticInstance<SoundBankUI>.Instance.PlayClip(UISounds.UI_Transaction);

            List<ItemDefinition> inputs = new List<ItemDefinition>();
            foreach (InventoryItem item in itemsToReroll) inputs.Add(item.itemDefinition);
            foreach (InventoryItem item in itemsToReroll) item.GetConsumedByExternalFactor(station.gameObject);

            foreach (ItemDefinition input in inputs)
            {
                ItemDefinition result = PickSameCategory(station, input);
                if (result != null && serviceGrid != null)
                {
                    serviceGrid.AddItem(result, isPickup: false, announce: false);
                    if (LogRerolls.Value)
                        Log.LogInfo($"Rerolled {input.name} [{CategoryOf(input)}] -> {result.name} [{CategoryOf(result)}]");
                }
            }

            traverse.Field("isRerolling").SetValue(false);
            traverse.Method("UpdateCombination").GetValue();
            return false; // never run the vanilla DoReroll
        }
    }

    [HarmonyPatch(typeof(OilRerollStation), "DoReroll")]
    internal static class DoRerollPatch
    {
        private static bool Prefix(OilRerollStation __instance)
        {
            return Plugin.DoRerollSameCategory(__instance);
        }
    }
}
