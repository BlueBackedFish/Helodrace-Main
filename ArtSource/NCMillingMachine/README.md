# Early CNC / numerical-control milling machine

New original 3x2 circa-1955-1959 knee-type vertical mill. The broad cast foot, vertical column, overarm, spindle, T-slot table and vise retain the existing mill's mechanical family. Servo feed motors and a separate analog/punched-tape control cabinet identify its numerical-control upgrade. The machine stays open and blocky for small isometric sprites.

`build_mill.py` uses `../EarlyAutomation/machine_common.py` and the shared fixed-color sprite primitives. It exports `HD_NCMillingMachine.blend`, four actual 64-degree orthographic views, 768px transparent color/mask pairs and fixed 12px exterior outlines. drawSize is (4.5,4.5); see `validation.json` for the compensated ground-anchor offset. All camera views share the same geometry, colors and scale.

`Defs/ColdWar/Buildings/NumericalControl.xml` provides a staffed 2200W worktable with work-speed factor 2.5, the existing line-shaft milling recipes and tier-2 milling-facility support at basic/gunsmith benches. It unlocks at `HelodNumericalControl`. Its construction uses existing early electronics rather than requiring semiconductor production.

Historical control guidance: [MIT's August 1952 announcement](https://cdn.libraries.mit.edu/dissemination/diponline/AC0069_NewReleases/NewsRelease_1950/AC0069_1952/AC0069_195208_005.pdf). This is a compact game variant of the early NC equipment family, not a reconstruction of MIT's machine. No modern machining-center shell or screen is added.

Build this script with Blender, then use `IndustrialMachines/normalize_density.py -- HD_NCMillingMachine` for later publication. Saved-scene and definition checks are supplied; no in-game visual/play test has been performed.
