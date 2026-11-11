# AN/FPS-117 radome building

An editable, newly authored Blender model of a white geodesic radar dome on a low circular equipment building. A mounting collar, concrete pad, service door, entry step and paired ventilation grilles complete the exterior. The antenna is enclosed by the opaque radome. This is a stylized installation, not an engineering reconstruction of one particular site.

The model follows `../IndustrialMachines/PRESENTATION_CALIBRATION.md`: one model unit per tile, provisional 4x4 footprint, 1024x1024 transparent RGBA, drawSize (6,6), constant 12px exterior outline and four actual 64-degree orthographic views. Fixed RGB Emission materials provide restrained height-based white panel colors, without lights, cast shadows, AO or texture noise. The narrow triangular seams are modeled panel joints. White composite panels and concrete retain their authored colors; the masks are unselected black with matching alpha. No shared palette or existing asset is changed.

Rebuild from the repository root:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b -t 6 --python-exit-code 1 --python ArtSource/ANFPS117/build.py
```

Outputs: `HD_ANFPS117.blend`, `preview_four_views.png`, smaller review sheets and `validation.json`; game-ready sprites and matching masks use `Textures/Buildings/HD_ANFPS117_{north,east,south,west}.png` and `...m.png`. Exact ground registration is recorded as `expected_draw_offset` in the validation file. The source scene can be opened and edited independently.

References: [Lockheed Martin FPS-117](https://www.lockheedmartin.com/en-us/products/ground-based-air-surveillance-radars/fps-117.html) identifies the radar; [manufacturer fact sheet](https://www.radomes.org/museum/equip/FPS-117LMfactsheet.pdf) illustrates a radome installation. The user requested the enclosed dome form. Exterior proportions, panel tessellation and service details are authored for sprite readability.

The exporter reopens the saved scene and checks all four camera angles, native dimensions, clear margins and color/mask alpha equality. A mesh hash also checks geometry preservation across export/save/reload. Four-view previews were visually reviewed for the dome silhouette, continuous panel joints, entry and side vents. Gameplay definitions, detection behavior and in-game visual testing are outside this model-only addition.
