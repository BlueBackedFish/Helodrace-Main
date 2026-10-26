Current tile-density export: 768×768px, drawSize (4.5,4.5), fixed 12px outline, 64° North/East/South/West cameras. The canvas grows by 256px per longest footprint tile; density and game-space outline width are identical across buildings. Vertical drawOffset is 0, compensating headroom without moving the ground anchor. Geometry and painted colors are preserved. These current exports supersede historical camera/resolution notes below. Publish with `ArtSource/IndustrialMachines/normalize_density.py`; see `ArtSource/IndustrialMachines/PRESENTATION_CALIBRATION.md`.

# Period cast-frame electric motor

Original c.1920–1940 motor on a 3×3 cast skid, separate from the open-field generator. A smooth cast stator has two broad reinforcing bands, a solid drive shield, a rear end shield with four broad flat vent regions, outboard split bearings and a flat-belt pulley. A small side terminal box carries two porcelain studs. The earlier long cooling-fin body and tall top box have been replaced. Period guidance is in `../IndustrialMachines/PERIOD_DESIGN.md`; original sprites remain in `Reference/`.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --threads 2 --python-exit-code 1 --python ArtSource/EarlyIndustrialMotor/build_motor.py
```

Scene: `HD_EarlyIndustrialLargeMotor.blend`. Existing texture prefix, footprint and mechanics are retained. North/East/South 512px transparent color/mask pairs use 64° downward orthographic cameras and one common scale. Vent regions are material faces at x=0.745 on the end shield, not protruding bars. Four slots span twelve mesh faces; `../IndustrialMachines/verify_assets.py` checks coplanarity. Brass, porcelain and insulation keep fixed colors in material masks. Saved-scene, framing and alpha checks are recorded in `validation.json`; 512px/2512px review sheets are generated. No in-game test is claimed.

Density/style correction: a 1.50×1.44 central casting and two 0.65×0.76 bearing wings replace the empty rectangular slab, reducing top area by 47.9%. Two shallow longitudinal casting ribs, a bordered stator service lid with four studs, lifting eye, end-shield tie bolts, insulated terminal bridge and split-bearing caps give the body readable structure. Neutral gray metal and a 12px black exported silhouette replace the low-contrast green body. The broad end vents remain coplanar with the shield.


Four-direction export (2026-10-26): North / East / South / West are actual 64-degree orthographic renders of the same saved geometry and colors; West is not mirrored East. See preview_four_views.png and validation.json.
