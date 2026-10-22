using BepInEx;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;

namespace SulfurMods.AchievementEnabler;

[BepInPlugin("sulfur.mods.achievements", "Sulfur Achievement Enabler", "1.0.0")]
public class Plugin : BaseUnityPlugin
{
    private void Awake()
    {
        Harmony.CreateAndPatchAll(typeof(Plugin).Assembly, "sulfur.mods.achievements");
        Logger.LogInfo("Achievements stay enabled on cheat saves.");
    }
}

static class CheatGate
{
    internal static void Open(ref bool restore)
    {
        restore = SulfurSaveState.active != null && SulfurSaveState.active.CHEATS_ENABLED;
        if (restore)
            SulfurSaveState.active.CHEATS_ENABLED = false;
    }

    internal static void Close(bool restore)
    {
        if (restore && SulfurSaveState.active != null)
            SulfurSaveState.active.CHEATS_ENABLED = true;
    }
}

[HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.AddProgress))]
static class AddProgressPatch
{
    static void Prefix(ref bool __state) => CheatGate.Open(ref __state);
    static void Postfix(bool __state) => CheatGate.Close(__state);
}

[HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.SetLocalStat))]
static class SetLocalStatPatch
{
    static void Prefix(ref bool __state) => CheatGate.Open(ref __state);
    static void Postfix(bool __state) => CheatGate.Close(__state);
}

[HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.UnlockAchievement))]
static class UnlockPatch
{
    static void Prefix(ref bool __state) => CheatGate.Open(ref __state);
    static void Postfix(bool __state) => CheatGate.Close(__state);
}
