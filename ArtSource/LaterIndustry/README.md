# Later industrial machine generations

Four separately authored source models extend the earlier machine family: a 1970s machining center (3x3), a 2000s mill-turn machine (4x3), a 1970s hydraulic rolling mill (4x3), and a 1970s electrohydraulic press (3x3). Each source directory has a standalone builder, editable Blender scene, four-view previews, validation and period-reference notes. `machine_common.py` supplies only shared primitive helpers and display/power-pack motifs.

All models use fixed Emission colors, the established neutral machine palette, 64-degree orthographic North/East/South/West views, transparent color/mask pairs and 12px exterior outlines. Native resolution is 256 times the longest footprint side: 768px or 1024px. Camera scale/drawSize is 1.5 times that side. View framing never changes geometry, hides parts or changes painted colors. Open guards/service openings are deliberate static sprite-proxy presentation choices.

The machines have active electric worktable definitions, staffed bills, Korean text and existing recipe coverage. The mill-turn combines turning and milling; the press also retains the helmet's generated recipe. Industrial automation unlocks the 1970s machines; integrated mill-turn machining unlocks the 2000s machine. Existing early machines retain their own definitions and operation.

Run `verify_definitions.py` with ordinary Python/Pillow to check source recipe parity, generated recipes, bill workers, power, material/research references, translations and higher-tier workbench facilities. Run `IndustrialMachines/verify_rotation.py` in Blender for saved geometry/color hashes, 64-degree cameras, texture resolution, alpha/masks, margins, fixed outline density and game graphic links.

`review_machines.py` creates common-game-scale contact sheets, trimming transparent margins without silhouette fitting. Review backgrounds/captions are absent from delivered game textures. No in-game visual/play test has been performed.
