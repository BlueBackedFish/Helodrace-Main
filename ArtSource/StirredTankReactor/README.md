# Circa-1950 stirred reaction kettle

New original 3×3 sprite-proxy model: a jacketed, dished reaction kettle with an overhead cast motor, reduction gearbox, visible seal/coupling and supporting bridge. A charging manhole, coarse bolted lid, thermal dial, jacket circulation pump and pipes, sight port, analog pushbutton box and bottom discharge handwheel identify it as an agitated process vessel. Open rails and three substantial vessel supports avoid a blank floor slab. Neutral metal, dark motor paint, a muted sight glass and limited brass fit the workshop family.

Period guidance: [US2522940A, published September 1950](https://patents.google.com/patent/US2522940A/en), documents a jacketed steel reaction kettle with an agitator, thermometer, vent and discharge arrangement. [De Dietrich's manufacturer history](https://www.dedietrich.com/en/history) establishes the older lined-vessel family. These guide the external machinery language; this is an invented game asset, not an exact historic product or engineering diagram. No modern touchscreen, sanitary fitting forest or exposed internal impeller is added.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --threads 2 --python-exit-code 1 --python ArtSource/StirredTankReactor/build_reactor.py
```

Scene: `HD_StirredTankReactor.blend`. North/East/South/West share scale 4.5, a zero ground target and 64° downward orthographic cameras. Transparent 512px color/mask pairs receive a 12px unselected black silhouette. Use game drawSize (4.5,4.5), zero drawOffset and the rotatable Graphic_Multi definition with unsuffixed texPath; the footprint stays 3×3. The active mask now follows the standard `HD_StirredTankReactor_northm.png` name. The obsolete `_north_m.png` and original color are preserved in `Reference/` and packed in the Blender scene. Review sheets and saved-scene checks are generated; no in-game verification is claimed.

Four-direction export (2026-10-26): North / East / South / West are actual 64-degree orthographic renders of the same saved geometry and colors; West is not mirrored East. See preview_four_views.png and validation.json.
