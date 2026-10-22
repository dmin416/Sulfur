using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.Stats;
using PerfectRandom.Sulfur.Core.Units;
using UnityEngine;

namespace SulfurMods.CalorieBalance;

[BepInPlugin("sulfur.mods.caloriebalance", "Sulfur Calorie Balance", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    internal static ConfigEntry<float> HealthMultiplier;
    internal static ConfigEntry<float> KcalPerCell;
    internal static ConfigEntry<float> HpPerKcal;
    internal static Plugin Instance;

    private bool applied;

    private void Awake()
    {
        Instance = this;
        HealthMultiplier = Config.Bind("General", "HealthMultiplier", 10f, "Player max health is multiplied by this. Vanilla player health is 100.");
        KcalPerCell = Config.Bind("General", "KcalPerCell", 200f, "Default calories for one inventory cell, used when calories.csv has no row for that food.");
        HpPerKcal = Config.Bind("General", "HpPerKcal", 0.5f, "Healing granted per calorie. A full loaf and two snails of the same total size heal the same amount.");
        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, "sulfur.mods.caloriebalance");
        Logger.LogInfo("Calorie balance loaded. Foods are rewritten once the item database is ready.");
    }

    private void Update()
    {
        if (applied)
            return;
        var loading = StaticInstance<AsyncAssetLoading>.Instance;
        if (loading == null || loading.itemDatabase == null)
            return;
        var items = loading.itemDatabase.GetRawList();
        if (items == null || items.Count == 0)
            return;
        FoodTable.Apply(items, Logger);
        applied = true;
    }
}

static class HealthScale
{
    static readonly HashSet<int> ScaledAssets = new HashSet<int>();
    static readonly HashSet<int> ScaledCurrent = new HashSet<int>();

    internal static void ScaleAsset(UnitSO unit, Unit owner)
    {
        if (unit == null || owner == null || !owner.isPlayer)
            return;
        if (!ScaledAssets.Add(unit.GetInstanceID()))
            return;
        float mult = Plugin.HealthMultiplier.Value;
        var list = unit.characterBaseAttributesNew;
        for (int i = 0; i < list.Count; i++)
        {
            var attr = list[i];
            if (attr.type != EntityAttributes.Stat_MaxHealth)
                continue;
            attr.value *= mult;
            list[i] = attr;
        }
    }

    internal static void ScaleCurrent(Unit owner)
    {
        if (owner == null || !owner.isPlayer || owner.Stats == null)
            return;
        if (!ScaledCurrent.Add(owner.GetInstanceID()))
            return;
        if (!owner.Stats.TryGetAttribute(EntityAttributes.Status_CurrentHealth, out float hp))
            return;
        if (hp > 0f && hp <= 100.5f)
            owner.Stats.SetStatus(EntityAttributes.Status_CurrentHealth, hp * Plugin.HealthMultiplier.Value, false);
    }

    internal static void FillToMax()
    {
        var player = StaticInstance<GameManager>.Instance != null ? StaticInstance<GameManager>.Instance.PlayerUnit : null;
        if (player == null || player.Stats == null)
            return;
        if (player.Stats.TryGetAttribute(EntityAttributes.Stat_MaxHealth, out float max) && max > 0f)
            player.Stats.SetStatus(EntityAttributes.Status_CurrentHealth, max, false);
    }
}

[HarmonyPatch(typeof(EntityStats), nameof(EntityStats.SetBaseAttributes))]
static class SetBaseAttributesPatch
{
    static void Prefix(UnitSO unit, Unit owner) => HealthScale.ScaleAsset(unit, owner);
    static void Postfix(Unit owner) => HealthScale.ScaleCurrent(owner);
}

[HarmonyPatch(typeof(FloatingCardManager), nameof(FloatingCardManager.RemovePlayerModifiers))]
static class CardResetPatch
{
    static void Postfix() => HealthScale.FillToMax();
}

static class FoodTable
{
    internal static void Apply(List<ItemDefinition> items, ManualLogSource log)
    {
        string path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", "calories.csv");
        var saved = ReadCsv(path);
        var rows = new List<string>();
        int foods = 0;
        foreach (var item in items)
        {
            if (item == null || item.buffsOnConsume == null)
                continue;
            int healBuffs = 0;
            foreach (var buff in item.buffsOnConsume)
            {
                if (buff != null && buff.attributeId == EntityAttributes.Stat_HealthRegen)
                    healBuffs++;
            }
            if (healBuffs == 0)
                continue;

            if (item.id == ItemIds.Item_FirstAidKit || item.id == ItemIds.Item_HealthPotion)
            {
                ScaleMedicine(item, log);
                continue;
            }

            int cells = Mathf.Max(1, item.inventorySize.x * item.inventorySize.y);
            float kcal = saved.TryGetValue(item.name, out float fromFile)
                ? fromFile
                : cells * Plugin.KcalPerCell.Value;
            float heal = kcal * Plugin.HpPerKcal.Value;
            float each = heal / healBuffs;
            foreach (var buff in item.buffsOnConsume)
            {
                if (buff == null || buff.attributeId != EntityAttributes.Stat_HealthRegen)
                    continue;
                if (buff.duration <= 0f)
                    buff.duration = 1f;
                buff.totalValueOverride = each;
            }
            rows.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1},{2}", item.name, cells, kcal));
            foods++;
        }
        rows.Sort(System.StringComparer.Ordinal);
        var builder = new StringBuilder();
        builder.AppendLine("name,cells,kcal");
        foreach (var row in rows)
            builder.AppendLine(row);
        File.WriteAllText(path, builder.ToString());
        log.LogInfo("Rewrote " + foods + " foods. Edit calories.csv beside the plugin and restart to change them.");
    }

    static void ScaleMedicine(ItemDefinition item, ManualLogSource log)
    {
        float mult = Plugin.HealthMultiplier.Value;
        foreach (var buff in item.buffsOnConsume)
        {
            if (buff == null || buff.attributeId != EntityAttributes.Stat_HealthRegen)
                continue;
            float before = buff.totalValueOverride != 0f ? buff.totalValueOverride : buff.value;
            if (Mathf.Abs(before) < 2f)
            {
                log.LogInfo(item.name + " heal " + before + " looks like a fraction of the bar, left alone so it scales with max health.");
                continue;
            }
            if (buff.totalValueOverride != 0f)
                buff.totalValueOverride *= mult;
            else
                buff.value *= mult;
            log.LogInfo(item.name + " heal " + before + " scaled to " + (buff.totalValueOverride != 0f ? buff.totalValueOverride : buff.value) + ".");
        }
    }

    static Dictionary<string, float> ReadCsv(string path)
    {
        var map = new Dictionary<string, float>();
        if (!File.Exists(path))
            return map;
        foreach (var raw in File.ReadAllLines(path))
        {
            if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("name"))
                continue;
            var parts = raw.Split(',');
            if (parts.Length < 3)
                continue;
            if (float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float kcal))
                map[parts[0].Trim()] = kcal;
        }
        return map;
    }
}
