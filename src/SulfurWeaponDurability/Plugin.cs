using System.Reflection;
using BepInEx;
using PerfectRandom.Sulfur.Core;
using PerfectRandom.Sulfur.Core.Items;

namespace SulfurMods.WeaponDurability;

[BepInPlugin("sulfur.mods.weapondurability", "Sulfur Weapon Durability", "0.1.0")]
public class Plugin : BaseUnityPlugin
{
    bool logged;

    private void Awake()
    {
        Logger.LogInfo("Weapon durability probe loaded. It logs every durability field on each weapon.");
    }

    // Probe only: lists every field and property with "durab" in its name on each weapon with its
    // current value, so the per-weapon max durability table can target the right member.
    private void Update()
    {
        if (logged)
            return;
        var loading = StaticInstance<AsyncAssetLoading>.Instance;
        if (loading == null || loading.itemDatabase == null)
            return;
        var items = loading.itemDatabase.GetRawList();
        if (items == null || items.Count == 0)
            return;
        logged = true;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var item in items)
        {
            var weapon = item as WeaponSO;
            if (weapon == null)
                continue;
            for (var type = weapon.GetType(); type != null && type != typeof(object); type = type.BaseType)
            {
                foreach (var field in type.GetFields(flags | BindingFlags.DeclaredOnly))
                    if (field.Name.ToLowerInvariant().Contains("durab"))
                        Logger.LogInfo(weapon.name + " field " + type.Name + "." + field.Name + " = " + field.GetValue(weapon));
                foreach (var prop in type.GetProperties(flags | BindingFlags.DeclaredOnly))
                    if (prop.Name.ToLowerInvariant().Contains("durab") && prop.GetIndexParameters().Length == 0 && prop.CanRead)
                    {
                        object value;
                        try { value = prop.GetValue(weapon); }
                        catch { value = "unreadable"; }
                        Logger.LogInfo(weapon.name + " property " + type.Name + "." + prop.Name + " = " + value);
                    }
            }
        }
    }
}
