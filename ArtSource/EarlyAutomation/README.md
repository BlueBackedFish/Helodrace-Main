# Early NC machines and transistor workshop

The lathe and mill are original electrically driven early numerical-control variants of the existing open machine tools. Each is 3x2 so a separate paper-tape/analog control cabinet fits alongside the cutting machine. The fab is a single open 3x3 laboratory/workshop representing a late-1950s process family. Equipment is compressed for sprite readability; full factory engineering is not modeled.

All three use the established fixed industrial palette, fixed Emission shading, 64-degree North/East/South/West orthographic views, 768px color/mask pairs and 12px exterior outlines. One model unit remains one game tile. `machine_common.py` shares the control-cabinet geometry between the two NC machines. Individual source folders contain editable Blender scenes, builders, previews, validation and period-reference notes.

`verify_definitions.py [cnc|fab|all]` runs with ordinary Python/Pillow. It validates resolved references, bill workgivers, electric supplies, ticking heat comps, translations, recipe coverage, facility connections, research dependencies and texture pairs. `IndustrialMachines/verify_rotation.py` validates saved cameras, scale, masks, margins and geometry/color hashes.

`review_automation.py` creates review-only contact sheets at one common game scale. These backgrounds and labels are not written into game textures. No in-game visual/play test has been performed.
