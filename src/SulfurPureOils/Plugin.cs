using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.Stats;

namespace SulfurMods.PureOils;

[BepInPlugin("sulfur.mods.pureoils", "Sulfur Pure Oils", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    internal static Plugin Instance;
    internal static ManualLogSource Log;
    internal static readonly HashSet<EnchantmentDefinition> Filtered = new HashSet<EnchantmentDefinition>();

    private bool scanned;

    private void Awake()
    {
        Instance = this;
        Log = Logger;
        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, "sulfur.mods.pureoils");
        Logger.LogInfo("Pure oils loaded. Drawbacks are removed when an oil is registered.");
    }

    private void Update()
    {
        if (scanned)
            return;
        var loading = StaticInstance<AsyncAssetLoading>.Instance;
        if (loading == null || loading.enchantmentDatabase == null)
            return;
        var list = loading.enchantmentDatabase.GetRawList();
        if (list == null || list.Count == 0)
            return;
        int touched = 0;
        int removed = 0;
        foreach (var enchantment in list)
        {
            int count = OilFilter.Apply(enchantment, log: true);
            if (count > 0)
            {
                touched++;
                removed += count;
            }
            else
                OilFilter.Apply(enchantment, log: false);
        }
        scanned = true;
        Logger.LogInfo("Filtered " + list.Count + " oils. " + touched + " changed, " + removed + " modifiers removed.");
    }
}

static class OilFilter
{
    static readonly HashSet<ItemAttributes> BadWhenPositive = new HashSet<ItemAttributes>
    {
        ItemAttributes.Spread,
        ItemAttributes.ProjectileMass,
        ItemAttributes.ProjectileDrag,
        ItemAttributes.ProjectileGravityFactor,
        ItemAttributes.ConsumeExtraAmmoChance,
        ItemAttributes.DurabilityLoss,
        ItemAttributes.EnchantmentDurabilityCost,
        ItemAttributes.EnchantmentSelfDamage,
        ItemAttributes.DisableLootMoney,
        ItemAttributes.DisableLootOrgans
    };

    static readonly HashSet<ItemAttributes> GoodWhenHigher = new HashSet<ItemAttributes>
    {
        ItemAttributes.Damage,
        ItemAttributes.DamageModifier,
        ItemAttributes.MaxDurability,
        ItemAttributes.ProjectileTimeScale,
        ItemAttributes.ProjectileForce,
        ItemAttributes.ProjectileAmount,
        ItemAttributes.ProjectileScale,
        ItemAttributes.ProjectilePenetration,
        ItemAttributes.ProjectileLifeTime,
        ItemAttributes.ReloadSpeed,
        ItemAttributes.ItemStat_LootChanceMultiplier,
        ItemAttributes.ItemStat_MoveSpeed
    };

    internal static int Apply(EnchantmentDefinition enchantment, bool log)
    {
        if (enchantment == null || !Plugin.Filtered.Add(enchantment))
            return 0;
        enchantment.CostsDurability = false;
        if (enchantment.modifiersApplied == null)
            return 0;
        int removed = 0;
        for (int i = enchantment.modifiersApplied.Count - 1; i >= 0; i--)
        {
            var modifier = enchantment.modifiersApplied[i];
            if (modifier == null)
                continue;
            bool drop = (modifier.value > 0f && BadWhenPositive.Contains(modifier.attribute))
                || (modifier.value < 0f && GoodWhenHigher.Contains(modifier.attribute));
            if (!drop)
                continue;
            enchantment.modifiersApplied.RemoveAt(i);
            removed++;
            if (log && Plugin.Log != null)
                Plugin.Log.LogInfo("Removed " + enchantment.enchantmentName + " " + modifier.attribute + " " + modifier.value);
        }
        return removed;
    }
}

[HarmonyPatch(typeof(EnchantmentDefinition), nameof(EnchantmentDefinition.RegisterAppliedBy))]
static class RegisterPatch
{
    static void Prefix(EnchantmentDefinition __instance) => OilFilter.Apply(__instance, log: true);
}
