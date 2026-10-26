# Circa-1950 high-pressure reactor

New original 3×3 sprite-proxy asset: a heavy upright pressure converter with dished heads, a thick eight-stud closure, side heat exchanger, closed circulation loop, hand valve, front pump, analog instrument stand, rear service step and short ladder. The open skid avoids a broad empty floor plate. Neutral cast iron, bright machined collars, dark recesses and limited brass match the workshop family.

Historical family guidance: [KIT's preserved 1923 BASF ammonia converter](https://www.100objekte.kit.edu/en/object/036/) establishes the earlier pressure-vessel lineage, not a claimed 1950 model. [A 1952 primary paper on ammonia pressure-vessel steel](https://www.jstage.jst.go.jp/article/tetsutohagane1915/38/9/38_9_654/_article/-char/en) anchors the requested mid-century context. The model is an invented compact game machine, not a copy or an engineering specification.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --threads 2 --python-exit-code 1 --python ArtSource/HighPressureReactor/build_reactor.py
```

Scene: `HD_HaberBoschHighPressureReactor.blend`. North/East/South/West are 512×512 transparent PNGs with aligned material masks, 64° downward orthographic cameras, scale 4.5 and ground target (0,0,0). Export adds a 12px black unselected silhouette. One model unit equals one game tile when paired with drawSize (4.5,4.5) and zero drawOffset. The existing 3×3 footprint is retained; the definition now rotates using Graphic_Multi and the unsuffixed texPath. The correct active material mask is `HD_HaberBoschHighPressureReactor_northm.png`; the obsolete `_north_m.png` is preserved only in `Reference/`. Original color/mask references are packed in the scene. See per-scene validation and review sheets. No in-game test has been performed.

Four-direction export (2026-10-26): North / East / South / West are actual 64-degree orthographic renders of the same saved geometry and colors; West is not mirrored East. See preview_four_views.png and validation.json.
