# M14 Combat Extended compatibility

Implemented 2026-09-14 in Main and the sibling Helodrace-CombatExtended addon.

- All three M14 barrels select CE AmmoSet_762x51mmNATO.
- The 7.62 M80 cartridge selector uses CE Ammo_762x51mmNATO_FMJ.
- CE patch adds ammo and fire-mode components, a 20-round nominal magazine,
  2.5-second nominal reload, CE shooting verb and CE melee tool.
- Existing modular bridges retain authority over installed magazine capacity,
  ammo selection, assembled stats and functional-part restrictions.
- M14 is included in the CE startup verb validator with a 7.62 NATO FMJ fallback.
- Saved geometry, animation and default attachments are unchanged.

Files in the CE addon:
- Patches/CombatExtended_HelodModularM14.xml
- Source/HelodraceCombatExtended/CEModularWeaponStats.cs

Verification: CE build passed with zero warnings/errors; all four patch XPath
targets match exactly once; three barrel ammo sets and cartridge selector validated.
In-game spawning, pawn loadouts, reload/fire and part-removal behavior still need
runtime testing after restarting RimWorld with both Main and the CE addon enabled.

This supersedes the original import note that CE support was not yet included.
