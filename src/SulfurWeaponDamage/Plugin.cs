using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;

namespace SulfurMods.WeaponDamage;

[BepInPlugin("sulfur.mods.weapondamage", "Sulfur Weapon Damage", "1.1.0")]
public class Plugin : BaseUnityPlugin
{
    // Weapon type, then each weapon with the barrel length in inches of its real analogue. The
    // shortest barrel in a type is the baseline and keeps its existing damage. Every other weapon
    // in the type is scaled up along the barrel curve from that baseline.
    static readonly (string type, (string name, float inches)[] weapons)[] Table =
    {
        ("Pistols", new (string, float)[]
        {
            ("P38 Dirk", 4.9f),
            ("Socom 9", 4.3f),
            ("Star & Witness", 4.5f),
            ("Gravekeeper", 5.0f),
            ("Bronco 89", 12.0f),
            ("Flicker", 1.0f),
            ("Terrier URB.", 4.3f),
            ("Beck 8", 3.0f),
            ("Salamander", 6.0f),
            ("Hell 'N' Back", 13.0f),
            ("Cavalier", 6.1f),
            ("Klein Bauer", 3.9f),
        }),
        ("Revolvers", new (string, float)[]
        {
            ("Palehorse Topclipper", 6.0f),
            ("Death's Toll", 4.0f),
            ("Snut .38", 2.0f),
            (".357 Balthazar", 4.0f),
            ("Blackwater", 7.5f),
        }),
        ("SMGs", new (string, float)[]
        {
            ("Ferryman", 10.4f),
            ("M3 Termite", 6.5f),
            ("Deathstar PG", 18.5f),
            ("Songbird", 7.1f),
            ("Chimera Rapid", 8.9f),
            ("Ripox Mowlock", 10.5f),
            ("Drifter 9", 4.0f),
            ("Vrede", 6.0f),
            ("Ploika Compact", 1.0f),
            ("Valet", 6.0f),
        }),
        ("AssaultRifles", new (string, float)[]
        {
            ("Corpsemaker", 16.3f),
            ("Type 80 Typhoon", 16.3f),
            ("M11A2 Fisk", 10.0f),
            ("Wingman", 19.8f),
            ("Nightlily 318", 10.4f),
            ("Socom ACR", 6.0f),
        }),
        ("LMGs", new (string, float)[]
        {
            ("Rektor 100rd", 20.5f),
            ("Duhar", 24.6f),
            ("Warpig", 20.5f),
            ("Chat-Pardeur 98", 18.5f),
        }),
        ("Rifles", new (string, float)[]
        {
            ("M182 Pierre-Fusil", 22.6f),
            ("Knop .22", 16.0f),
            ("Tailor Marksman MKII", 10.0f),
            ("Farsight", 20.0f),
        }),
        ("Snipers", new (string, float)[]
        {
            ("Rokua .308", 24.0f),
            ("Dolphin 99", 18.0f),
            ("Longboy", 26.0f),
            ("Impala Gravita", 8.0f),
        }),
    };

    // Per projectile damage by barrel length, taken from the shotgun scale. Flat past 28 in.
    static readonly (float inches, float damage)[] CurvePoints = { (1f, 40f), (6f, 60f), (10f, 70f), (28f, 90f) };

    static float Curve(float inches)
    {
        if (inches <= CurvePoints[0].inches)
            return CurvePoints[0].damage;
        for (int i = 1; i < CurvePoints.Length; i++)
        {
            var a = CurvePoints[i - 1];
            var b = CurvePoints[i];
            if (inches <= b.inches)
                return a.damage + (b.damage - a.damage) * (inches - a.inches) / (b.inches - a.inches);
        }
        return CurvePoints[CurvePoints.Length - 1].damage;
    }

    readonly Dictionary<string, float> userFactor = new Dictionary<string, float>();
    bool applied;

    static string Key(string text)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in text)
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    static CaliberType Caliber(WeaponSO weapon)
    {
        try { return weapon.caliber.GetAsset(); }
        catch { return null; }
    }

    private void Awake()
    {
        foreach (var row in Table)
        {
            float all = Config.Bind(row.type, "All", 1f, "Extra damage multiplier for every weapon in this type.").Value;
            foreach (var weapon in row.weapons)
            {
                float each = Config.Bind(row.type, weapon.name, 1f, "Extra damage multiplier for this weapon. Stacks with All.").Value;
                userFactor[Key(weapon.name)] = all * each;
            }
        }
        Logger.LogInfo("Weapon damage loaded.");
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
        var found = new Dictionary<string, WeaponSO>();
        foreach (var item in items)
        {
            var weapon = item as WeaponSO;
            if (weapon == null)
                continue;
            found[Key(weapon.name.StartsWith("Weapon_") ? weapon.name.Substring(7) : weapon.name)] = weapon;
        }
        foreach (var row in Table)
            foreach (var entry in row.weapons)
                if (found.TryGetValue(Key(entry.name), out var probe) && Caliber(probe) == null)
                    return;
        applied = true;
        foreach (var row in Table)
            Apply(row.type, row.weapons, found);
    }

    static float PerProjectile(WeaponSO weapon, CaliberType caliber)
    {
        return caliber.baseDamage * weapon.weaponType.GetDamageMultiplier();
    }

    void Apply(string type, (string name, float inches)[] weapons, Dictionary<string, WeaponSO> found)
    {
        int baseIndex = -1;
        for (int i = 0; i < weapons.Length; i++)
            if (found.ContainsKey(Key(weapons[i].name)) && (baseIndex < 0 || weapons[i].inches < weapons[baseIndex].inches))
                baseIndex = i;
        if (baseIndex < 0)
        {
            Logger.LogWarning(type + ": no weapons matched.");
            return;
        }
        var baseWeapon = found[Key(weapons[baseIndex].name)];
        var baseCaliber = Caliber(baseWeapon);
        float baseline = PerProjectile(baseWeapon, baseCaliber) * baseWeapon.damageMultiplier;
        float baseCurve = Curve(weapons[baseIndex].inches);
        Logger.LogInfo(type + ": baseline " + baseWeapon.name + " at " + baseline + " per projectile.");
        foreach (var entry in weapons)
        {
            if (!found.TryGetValue(Key(entry.name), out var weapon))
            {
                Logger.LogWarning("No weapon matched '" + entry.name + "'. Check the item name in the item database.");
                continue;
            }
            var caliber = Caliber(weapon);
            float perProjectile = PerProjectile(weapon, caliber);
            if (weapon.overrideDamage != 0f || perProjectile <= 0f)
            {
                Logger.LogWarning(weapon.name + " has an override or no caliber damage, skipped.");
                continue;
            }
            float target = baseline * Curve(entry.inches) / baseCurve * userFactor[Key(entry.name)];
            float before = weapon.damageMultiplier;
            weapon.damageMultiplier = target / perProjectile;
            Logger.LogInfo(weapon.name + ": " + entry.inches + " in, multiplier " + before + " -> " + weapon.damageMultiplier + " = " + target + " per projectile.");
        }
    }
}
