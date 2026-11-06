# SULFUR — How the game works (mod map)

## Engine / layout
- **Unity 6000.3.22f1, Mono** (not IL2CPP) → **BepInEx 5 + Harmony** is the mod path.
- Install: `SULFUR\`; game code in `Sulfur_Data\Managed\`.
- Big content in Addressables: `Sulfur_Data\StreamingAssets\aa\StandaloneWindows64\` (~46 bundles, ~1 GB).
- Decompile: `_decompiled\` (Core 759 / Gameplay 231 / LevelGen 35 / Assembly-CSharp 15).

## Assemblies that matter
| DLL | Role |
| --- | --- |
| `PerfectRandom.Sulfur.Core.dll` | Items, units, stats, saves, shops base, ServiceStation, enchantments, loot |
| `PerfectRandom.Sulfur.Gameplay.dll` | Stations (repair, oil reroll, craft, workbench…), most gameplay UI hooks |
| `PerfectRandom.Sulfur.LevelGeneration.dll` | Procgen / dungeon assembly |
| `Assembly-CSharp.dll` | Thin glue / global types (e.g. some managers) |

## Runtime systems (Harmony targets)
- **ServiceStation** hierarchy: RepairStation, OilRerollStation, CraftingStation, Workbench, ShopKeeper (Core), BulkAmmoShop, BaptismalFontStation, KeyStation, SacrificeStation, QuestGiver, ItemFrameStation, TeliaPrintStation, StampPurchase…
- **Items / builds:** `InventoryItem`, `ItemDefinition`, `ItemGrid`, attachments via `WeaponAttachmentVisual` / `WeaponAttachmentSlot` (barrel+muzzle both Muzzle=3 — visual conflict).
- **Oils / scrolls:** `EnchantmentDefinition` + `modifiersApplied` (`ItemModifierContainer`); polarity via `ItemAttributes` + `StatModType` (Flat/PercentAdd/PercentMult). Categories: `ItemTag` oil flags. Color ≈ main benefit.
- **Combat stats:** `EntityStats` / `ItemAttributes` (Damage, ProjectileAmount, ConsumeAmmoChance, Spread, RPM, …).
- **Food / heal:** consume buffs (`BuffDefinition`, HealthRegen); calorie mod rewrites these.
- **Economy:** coins on `EntityStats` (stash-first in safe zone); stamps = WorldResources id 10.
- **Saves:** `%LocalAppData%Low\Perfect Random\Sulfur\` — gzip JSON + UTF-8 BOM (`Sulfur_SaveSlot_N.sss`).
- **Loot:** `LootTable` / `LootEntry.lootWeight`; oil table `enchantmentOilLootTable`.
- **Progression:** weapon rank, durability, Telia recraft; achievements gated by `SulfurSaveState.CHEATS_ENABLED`.

## Asset / data extraction
- UnityPy / AssetStudio on Addressable bundles → ItemDefinitions, UnitSO, EnchantmentDefinitions, sprites.
- Balance dumps already under `_modbuild\` / `_decompiled\` (`enchantment_modifiers.json`, `calories.csv`, …).

## Existing personal mods (this repo)
AchievementEnabler, CalorieBalance, DisassembleAnywhere, MerchantRefresh, PureOils, BarrelRebalance, RepairAll, SameColorReroll — sources in `src/`, builds in `plugins/`.

## Typical new-mod patterns
1. BepInEx plugin → Harmony Prefix/Postfix/Transpiler on Core/Gameplay methods.  
2. Or rewrite ScriptableObject fields once at load (barrels, oils) with instance-id guards.  
3. Config via `BepInEx.Configuration` (`BepInEx\config\*.cfg` — delete cfg when changing defaults).  
4. Never need to repack game bundles for code mods.

## Hard constraints learned
- Shared CraftInventory UI → cloned buttons leak across merchants unless show/hide on Enter/Exit.  
- `RemoveEnchantment` forgets list removal — strip mods must also `enchantments.Remove`.  
- Merchant **R** rotates items; use other restock key.  
- Smoke-test: launch Sulfur.exe, confirm plugin lines in `BepInEx\LogOutput.log`.
