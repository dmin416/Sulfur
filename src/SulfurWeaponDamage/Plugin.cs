using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;

namespace SulfurMods.WeaponDamage;

[BepInPlugin("sulfur.mods.weapondamage", "Sulfur Weapon Damage", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    // Weapon type, then display names from the wiki. Each type gets one multiplier for the whole
    // type and one per weapon, and the two multiply. Add a type here to cover more weapons.
    static readonly (string type, string[] names)[] Table =
    {
        ("Pistols", new[]
        {
            "P38 Dirk", "Socom 9", "Star & Witness", "Gravekeeper", "Beck 8", "Salamander", "Bronco 89",
            "Flicker", "Unknown", "Hell 'N' Back", "Cavalier", "Terrier URB.", "Klein Bauer",
        }),
        ("Revolvers", new[]
        {
            "Snut .38", "Palehorse Topclipper", ".357 Balthazar", "Wyatt PULSAR", "Blackwater", "Death's Toll",
        }),
        ("SMGs", new[]
        {
            "Drifter 9", "Vrede", "Ploika Compact", "Ferryman", "M3 Termite", "Deathstar PG", "Valet",
            "Songbird", "Chimera Rapid", "Ripox Mowlock",
        }),
        ("AssaultRifles", new[]
        {
            "Corpsemaker", "Catacoil Rapid X", "Type 80 Typhoon", "M11A2 Fisk", "Wingman", "Socom ACR",
            "CYB47", "Nightlily 318",
        }),
        ("LMGs", new[]
        {
            "Rektor 100rd", "Duhar", "Neuraxis F22", "Warpig", "Chat-Pardeur 98",
        }),
        ("Rifles", new[]
        {
            "Knop .22", "M182 Pierre-Fusil", "Tailor Marksman MKII", "Farsight",
        }),
        ("Snipers", new[]
        {
            "Rokua .308", "Dolphin 99", "D4RT", "Impala Gravita", "Longboy",
        }),
    };

    readonly Dictionary<string, float> multipliers = new Dictionary<string, float>();
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
            float all = Config.Bind(row.type, "All", 1f, "Damage multiplier for every weapon in this type.").Value;
            foreach (var name in row.names)
            {
                float each = Config.Bind(row.type, name, 1f, "Damage multiplier for this weapon. Stacks with All.").Value;
                multipliers[Key(name)] = all * each;
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
        applied = true;
        var matched = new HashSet<string>();
        foreach (var item in items)
        {
            var weapon = item as WeaponSO;
            if (weapon == null)
                continue;
            string id = Key(weapon.name.StartsWith("Weapon_") ? weapon.name.Substring(7) : weapon.name);
            if (!multipliers.TryGetValue(id, out float factor))
                continue;
            matched.Add(id);
            float before = weapon.damageMultiplier;
            weapon.damageMultiplier = before * factor;
            Logger.LogInfo(weapon.name + ": multiplier " + before + " -> " + weapon.damageMultiplier + " (x" + factor + ").");
        }
        foreach (var id in multipliers.Keys)
            if (!matched.Contains(id))
                Logger.LogWarning("No weapon matched '" + id + "'. Check the item name in the item database.");
    }
}
