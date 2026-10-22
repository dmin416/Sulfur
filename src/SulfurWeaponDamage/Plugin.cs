using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;
using PerfectRandom.Sulfur.Core.Stats;

namespace SulfurMods.WeaponDamage;

[BepInPlugin("sulfur.mods.weapondamage", "Sulfur Weapon Damage", "1.1.0")]
public class Plugin : BaseUnityPlugin
{
    // Weapon type, then each weapon with the barrel length in inches of its real analogue. The
    // shortest barrel in a type is the baseline and keeps its multiplier. Every other weapon in the
    // type has its own multiplier scaled up along the barrel curve. Caliber damage is scaled separately in ScaleCalibers.
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

    // Caliber damage relative to 9mm, from muzzle energy with the exponent fitted to the shotgun scale
    // (9mm about 550 J = 60, 12ga pellet about 280 J = 40, so damage goes as energy^0.6). The weapon
    // is any one gun known to fire that caliber. 9mm is the reference and stays as it is.
    static readonly (string label, string weapon, float factor)[] Calibers =
    {
        ("556", "Gravekeeper", 1.97f),   // about 1700 J
        ("762", "Salamander", 2.98f),    // about 3400 J
        ("50BMG", "Ferryman", 7.84f),    // about 17000 J
    };
    const string NineMmWeapon = "Socom 9";

    readonly Dictionary<string, float> userFactor = new Dictionary<string, float>();
    readonly Dictionary<string, float> caliberFactor = new Dictionary<string, float>();
    bool applied;

    static string Key(string text)
    {
        var sb = new System.Text.StringBuilder();
        foreach (char c in text)
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
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
        foreach (var cal in Calibers)
            caliberFactor[cal.label] = Config.Bind("Calibers", cal.label, cal.factor,
                "Damage of this caliber as a multiple of 9mm. Set to 0 to leave the caliber alone.").Value;
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
        if (!ScaleCalibers(found))
            return;
        applied = true;
        foreach (var row in Table)
            Apply(row.type, row.weapons, found);
    }

    static CaliberType Caliber(WeaponSO weapon)
    {
        try { return weapon.caliber.GetAsset(); }
        catch { return null; }
    }

    bool ScaleCalibers(Dictionary<string, WeaponSO> found)
    {
        if (!found.TryGetValue(Key(NineMmWeapon), out var reference))
        {
            Logger.LogWarning("No 9mm reference weapon found, calibers left alone.");
            return true;
        }
        var nine = Caliber(reference);
        if (nine == null)
            return false;
        float reference9 = nine.baseDamage;
        foreach (var cal in Calibers)
        {
            if (caliberFactor[cal.label] <= 0f || !found.TryGetValue(Key(cal.weapon), out var gun))
                continue;
            var caliber = Caliber(gun);
            if (caliber == null)
                return false;
            float before = caliber.baseDamage;
            caliber.baseDamage = reference9 * caliberFactor[cal.label];
            Logger.LogInfo("Caliber " + cal.label + " (" + gun.name + "): base damage " + before + " -> " + caliber.baseDamage + ", 9mm is " + reference9 + ".");
        }
        return true;
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
        float baseCurve = Curve(weapons[baseIndex].inches);
        Logger.LogInfo(type + ": baseline " + found[Key(weapons[baseIndex].name)].name + " keeps its multiplier.");
        foreach (var entry in weapons)
        {
            if (!found.TryGetValue(Key(entry.name), out var weapon))
            {
                Logger.LogWarning("No weapon matched '" + entry.name + "'. Check the item name in the item database.");
                continue;
            }
            float before = weapon.damageMultiplier;
            weapon.damageMultiplier = before * Curve(entry.inches) / baseCurve * userFactor[Key(entry.name)];
            Logger.LogInfo(weapon.name + ": " + entry.inches + " in, multiplier " + before + " -> " + weapon.damageMultiplier + ".");
        }
    }
}
