using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.CharacterStats;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.Stats;
using PerfectRandom.Sulfur.Core.UI.ItemDescription;

namespace SulfurMods.BarrelRebalance;

[BepInPlugin("sulfur.mods.barrels", "Sulfur Barrel Rebalance", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    internal static ConfigEntry<float> Spread2;
    internal static ConfigEntry<float> Damage2;
    internal static ConfigEntry<float> Speed2;
    internal static ConfigEntry<float> Spread4;
    internal static ConfigEntry<float> Damage4;
    internal static ConfigEntry<float> Speed4;
    internal static ConfigEntry<float> SpreadShrouded;
    internal static ConfigEntry<float> DamageShrouded;
    internal static ConfigEntry<float> SpeedShrouded;
    internal static ConfigEntry<float> DurabilityShrouded;
    internal static ConfigEntry<float> Spread6;
    internal static ConfigEntry<float> Damage6;
    internal static ConfigEntry<float> Speed6;

    static readonly Dictionary<ItemId, BarrelTier> Tiers = new Dictionary<ItemId, BarrelTier>();
    static readonly HashSet<ItemDefinition> Rewritten = new HashSet<ItemDefinition>();

    private void Awake()
    {
        Spread2 = Bind("Barrel2", "Spread", 25f);
        Damage2 = Bind("Barrel2", "Damage", 20f);
        Speed2 = Bind("Barrel2", "Speed", 20f);
        Spread4 = Bind("Barrel4", "Spread", 40f);
        Damage4 = Bind("Barrel4", "Damage", 40f);
        Speed4 = Bind("Barrel4", "Speed", 40f);
        SpreadShrouded = Bind("Shrouded", "Spread", 30f);
        DamageShrouded = Bind("Shrouded", "Damage", 30f);
        SpeedShrouded = Bind("Shrouded", "Speed", 30f);
        DurabilityShrouded = Bind("Shrouded", "Durability", 30f);
        Spread6 = Bind("Barrel6", "Spread", 60f);
        Damage6 = Bind("Barrel6", "Damage", 60f);
        Speed6 = Bind("Barrel6", "Speed", 60f);

        Tiers[ItemIds.Attachment_BarrelExtension2i] = BarrelTier.Two;
        Tiers[ItemIds.Attachment_BarrelExtension4i] = BarrelTier.Four;
        Tiers[ItemIds.Attachment_ShroudedBarrelExtension] = BarrelTier.Shrouded;
        Tiers[ItemIds.Attachment_BarrelExtension6i] = BarrelTier.Six;

        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, "sulfur.mods.barrels");
        Logger.LogInfo("Barrel ladder loaded. Improvised barrels are left alone.");
    }

    ConfigEntry<float> Bind(string section, string key, float value)
    {
        return Config.Bind(section, key, value, "Percent. Spread is applied as a reduction. Speed is a flat addition to projectile time scale.");
    }

    internal static void Rewrite(ItemDefinition attachment)
    {
        if (attachment == null || !Tiers.TryGetValue(attachment.id, out var tier))
            return;
        if (!Rewritten.Add(attachment))
            return;
        if (attachment.modifiersOnAttachToItem == null)
            attachment.modifiersOnAttachToItem = new List<ItemModifierContainer>();

        var list = attachment.modifiersOnAttachToItem;
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var modifier = list[i];
            if (modifier == null)
                continue;
            if (modifier.attribute == ItemAttributes.Spread
                || modifier.attribute == ItemAttributes.Damage
                || modifier.attribute == ItemAttributes.ProjectileTimeScale
                || (tier == BarrelTier.Shrouded && modifier.attribute == ItemAttributes.MaxDurability))
                list.RemoveAt(i);
        }

        float spread, damage, speed, durability;
        switch (tier)
        {
            case BarrelTier.Two:
                spread = Spread2.Value; damage = Damage2.Value; speed = Speed2.Value; durability = 0f;
                break;
            case BarrelTier.Four:
                spread = Spread4.Value; damage = Damage4.Value; speed = Speed4.Value; durability = 0f;
                break;
            case BarrelTier.Shrouded:
                spread = SpreadShrouded.Value; damage = DamageShrouded.Value; speed = SpeedShrouded.Value; durability = DurabilityShrouded.Value;
                break;
            default:
                spread = Spread6.Value; damage = Damage6.Value; speed = Speed6.Value; durability = 0f;
                break;
        }

        list.Add(Make(ItemAttributes.Spread, StatModType.PercentAdd, -spread / 100f));
        list.Add(Make(ItemAttributes.Damage, StatModType.PercentAdd, damage / 100f));
        list.Add(Make(ItemAttributes.ProjectileTimeScale, StatModType.Flat, speed / 100f));
        if (durability != 0f)
            list.Add(Make(ItemAttributes.MaxDurability, StatModType.PercentAdd, durability / 100f));
    }

    static ItemModifierContainer Make(ItemAttributes attribute, StatModType modType, float value)
    {
        return new ItemModifierContainer { attribute = attribute, modType = modType, value = value };
    }

    enum BarrelTier { Two, Four, Shrouded, Six }
}

[HarmonyPatch(typeof(InventoryItem), nameof(InventoryItem.AddAttachment))]
static class AddAttachmentPatch
{
    static void Prefix(ItemDefinition attachmentItem) => Plugin.Rewrite(attachmentItem);
}

[HarmonyPatch(typeof(ItemDescription), "AddAttachmentText")]
static class TooltipPatch
{
    static void Prefix(ItemDefinition attachment) => Plugin.Rewrite(attachment);
}
