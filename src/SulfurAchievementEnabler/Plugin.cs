using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using PerfectRandom.Sulfur.Core;

namespace SulfurAchievementEnabler
{
    /// <summary>
    /// SULFUR blocks achievements on any save whose CHEATS_ENABLED flag is set
    /// (AchievementManager.UnlockAchievement / AddProgress / SetLocalStat all
    /// early-out). This mod makes those three methods ignore the flag, so
    /// achievements and Steam stats keep working on cheat/modded saves.
    /// The flag itself is left untouched: save UI, gameplay-modifier unlocks
    /// and everything else that reads CHEATS_ENABLED behaves as vanilla.
    /// Standalone: does not depend on any balance mod.
    /// </summary>
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    public class Plugin : BaseUnityPlugin
    {
        public const string PluginGuid = "sulfur.mods.achievement-enabler";
        public const string PluginName = "SULFUR Achievement Enabler";
        public const string PluginVersion = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<bool> ModEnabled;

        private void Awake()
        {
            Log = Logger;
            ModEnabled = Config.Bind("General", "Enabled", true,
                "If false, achievements behave exactly as vanilla (blocked on cheat saves).");
            if (!ModEnabled.Value)
            {
                Log.LogInfo("Disabled in config; achievements behave as vanilla.");
                return;
            }
            new Harmony(PluginGuid).PatchAll(typeof(Plugin).Assembly);
            Log.LogInfo("Achievement disabling is now disabled: achievements/Steam stats work even on cheat-enabled saves.");
        }
    }

    internal static class CheatGateBypass
    {
        // Called at the top of each gated method: clear the flag just for the
        // duration of the call and remember whether it needs restoring.
        internal static bool Clear(out bool wasEnabled)
        {
            wasEnabled = false;
            SulfurSaveState save = SulfurSaveState.active;
            if (save != null && save.CHEATS_ENABLED)
            {
                save.CHEATS_ENABLED = false;
                wasEnabled = true;
            }
            return wasEnabled;
        }

        internal static void Restore(bool wasEnabled)
        {
            if (wasEnabled && SulfurSaveState.active != null)
            {
                SulfurSaveState.active.CHEATS_ENABLED = true;
            }
        }
    }

    [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.UnlockAchievement))]
    internal static class UnlockAchievementPatch
    {
        private static void Prefix(out bool __state) { CheatGateBypass.Clear(out __state); }
        private static void Postfix(bool __state) { CheatGateBypass.Restore(__state); }
    }

    [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.AddProgress))]
    internal static class AddProgressPatch
    {
        private static void Prefix(out bool __state) { CheatGateBypass.Clear(out __state); }
        private static void Postfix(bool __state) { CheatGateBypass.Restore(__state); }
    }

    [HarmonyPatch(typeof(AchievementManager), nameof(AchievementManager.SetLocalStat))]
    internal static class SetLocalStatPatch
    {
        private static void Prefix(out bool __state) { CheatGateBypass.Clear(out __state); }
        private static void Postfix(bool __state) { CheatGateBypass.Restore(__state); }
    }
}
