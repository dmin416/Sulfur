using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.Stats;

namespace SulfurMods.ShotgunDamage;

[BepInPlugin("sulfur.mods.shotgundamage", "Sulfur Shotgun Damage", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    // Weapon item name, config key, damage per pellet. A pull fires pellets x shells, so
    // Flock 76 is 40x8x1, Arbiter 2 is 40x8x3 and the Breacher 8 is 70x8x(1-8).
    static readonly (string item, string label, float damage)[] Table =
    {
        ("Weapon_Flock", "Flock", 40f),
        ("Weapon_Arbiter2", "Arbiter2", 40f),
        ("Weapon_Mossman", "Mossman", 60f),
        ("Weapon_Majordome", "Majordome", 60f),
        ("Weapon_Breacher8", "Breacher8", 70f),
        ("Weapon_Mario", "Mario", 90f),
    };

    readonly Dictionary<string, ConfigEntry<float>> entries = new Dictionary<string, ConfigEntry<float>>();
    bool applied;

    private void Awake()
    {
        foreach (var row in Table)
            entries[row.item] = Config.Bind("PelletDamage", row.label, row.damage,
                "Damage of each pellet. Total per pull is this times pellets times shells fired.");
        Logger.LogInfo("Shotgun damage loaded.");
    }

    static CaliberType Caliber(WeaponSO weapon)
    {
        try { return weapon.caliber.GetAsset(); }
        catch { return null; }
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
        foreach (var item in items)
        {
            var probe = item as WeaponSO;
            if (probe == null || probe.name != "Weapon_Flock")
                continue;
            if (Caliber(probe) == null)
                return;
        }
        applied = true;
        foreach (var item in items)
        {
            var weapon = item as WeaponSO;
            if (weapon == null || !entries.TryGetValue(weapon.name, out var entry))
                continue;
            var caliber = Caliber(weapon);
            float perPellet = caliber.baseDamage * weapon.weaponType.GetDamageMultiplier();
            if (weapon.overrideDamage != 0f || perPellet <= 0f)
            {
                Logger.LogWarning(weapon.name + " has an override or no caliber damage, skipped.");
                continue;
            }
            float before = weapon.damageMultiplier;
            weapon.damageMultiplier = entry.Value / perPellet;
            Logger.LogInfo(weapon.name + ": caliber base " + caliber.baseDamage + ", pellets " + caliber.numberOfProjectiles
                + ", multiplier " + before + " -> " + weapon.damageMultiplier + " = " + entry.Value + " per pellet.");
        }
    }
}
