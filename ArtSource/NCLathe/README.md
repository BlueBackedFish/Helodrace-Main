# Early CNC / numerical-control lathe

New original 3x2 electric lathe, circa 1955-1959. The open bed, cast headstock, large three-jaw chuck, workpiece, tailstock and servo carriage preserve the silhouette family of the existing turret lathe. A separate tall relay cabinet carries analog dials and a coarse two-reel paper-tape reader. There is no modern enclosure, touchscreen or automatic robot operator.

`build_lathe.py` uses `../EarlyAutomation/machine_common.py` and the established fixed-color sprite primitives. It exports `HD_NCLathe.blend`, four real 64-degree orthographic views, 768px transparent color/mask pairs and fixed 12px exterior outlines. drawSize is (4.5,4.5); see `validation.json` for the compensated ground-anchor offset. No lighting, AO, PBR or view-dependent paint is used.

`Defs/ColdWar/Buildings/NumericalControl.xml` makes this a staffed worktable: 1800W, work-speed factor 2.5, existing turret-lathe recipes, and a tier-3 lathe facility for basic/gunsmith benches. It unlocks at `HelodNumericalControl`, following thermionic electronics and metal-cutting research. New construction uses existing electrical/electronic components, avoiding a dependency on semiconductor production.

Historical guidance: [MIT's August 1952 numerical-control milling announcement](https://cdn.libraries.mit.edu/dissemination/diponline/AC0069_NewReleases/NewsRelease_1950/AC0069_1952/AC0069_195208_005.pdf) describes paper-tape input and servo axes. Those early NC control principles are applied to this original lathe variant; the game label uses early CNC for discoverability. No claim of a particular historical lathe reconstruction is made.

Build this script with Blender, then use `IndustrialMachines/normalize_density.py -- HD_NCLathe` for later publication. Saved-scene and definition checks are supplied; no in-game visual/play test has been performed.
