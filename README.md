# SULFUR personal mods

Personal BepInEx / Harmony mods for a legitimately owned copy of [SULFUR](https://store.steampowered.com/app/2124120/SULFUR/).

**This repo does not contain the game.** Drop the built plugin DLLs into `BepInEx/plugins/` of your install.

## Mods

| Plugin | What it does |
| --- | --- |
| SulfurAchievementEnabler | Achievements work on cheat-enabled saves |
| SulfurCalorieBalance | Player HP ×10; food heals from calories |
| SulfurDisassembleAnywhere | Hover weapon + **K** → strip attachments/oils into backpack |
| SulfurMerchantRefresh | In shop, **N** → restock for 250 coins |
| SulfurPureOils | Oils/scrolls keep only beneficial modifiers |
| SulfurBarrelRebalance | Barrel extension spread/damage/speed retune |
| SulfurRepairAll | Repair All button at the repair lady |
| SulfurSameColorReroll | Oil reroll stays in the same main-benefit category |

## Build

Each project under `src/` is `netstandard2.1`. Point references at your game's `Sulfur_Data/Managed/` and `BepInEx/core/`, then:

```bash
dotnet build src/<ModName>/<ModName>.csproj -c Release
```

Copy the DLL into `plugins/<ModName>/`.
