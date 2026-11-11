# AN/FPS-117 radome building

An editable Blender model of a near-spherical white geodesic radar dome on a tall beige metal-clad equipment building. The user's two reference photographs determine the revised silhouette: the dome continues below its equator, rests on a narrower tall base and is surrounded by an overhanging maintenance catwalk with guardrails. Diagonal walkway supports, vertical wall seams, a service door and entry steps, high windows, side ventilation and a rear external ladder complete the exterior. The antenna is enclosed by the opaque radome. This is a stylized installation, not an engineering reconstruction of one particular site.

The model follows `../IndustrialMachines/PRESENTATION_CALIBRATION.md`: one model unit per tile, provisional 4x4 footprint, 1024x1024 transparent RGBA, drawSize (6,6), constant 12px exterior outline and four actual 64-degree orthographic views. Fixed RGB Emission materials provide restrained height-based white panel colors, without lights, cast shadows, AO or texture noise. The narrow triangular seams are modeled panel joints. Composite panels, beige cladding, concrete and railing retain their authored colors; the masks are unselected black with matching alpha. No shared palette or existing asset is changed. The steep game cameras foreshorten the tall base; the saved model retains the reference's elevated structure.

Rebuild from the repository root:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b -t 6 --python-exit-code 1 --python ArtSource/ANFPS117/build.py
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' -b -t 6 --python-exit-code 1 --python ArtSource/ANFPS117/render_reference_view.py
```

Outputs: `HD_ANFPS117.blend`, `preview_four_views.png`, smaller review sheets and `validation.json`; game-ready sprites and matching masks use `Textures/Buildings/HD_ANFPS117_{north,east,south,west}.png` and `...m.png`. Exact ground registration is recorded as `expected_draw_offset` in the validation file. The source scene can be opened and edited independently.

`reference_view.png` is an additional 20-degree orthographic shape review, with the same authored materials and transparent background. It has no postprocessed outline and is not a game sprite. Its script reads the saved model without saving camera changes to the source scene.

Primary shape references are the user-supplied photographs `codex-clipboard-83464f72-1ce4-471d-b18b-2975782802e4.png` (beige base, encircling catwalk, railings and ladder) and `codex-clipboard-6c0ec6e9-e548-4e9a-abdf-6be15b913611.png` (near-spherical dome and tall metal-clad base). Neither photograph is embedded in the textures. Background scenery, cars, fencing and the first photo's annex are omitted. Earlier identification references: [Lockheed Martin FPS-117](https://www.lockheedmartin.com/en-us/products/ground-based-air-surveillance-radars/fps-117.html) and [manufacturer fact sheet](https://www.radomes.org/museum/equip/FPS-117LMfactsheet.pdf). Exterior proportions, panel tessellation and service details are authored for sprite readability.

The exporter reopens the saved scene and checks all four camera angles, native dimensions, clear margins and color/mask alpha equality. A mesh hash also checks geometry preservation across export/save/reload. Four-view previews are reviewed for the sphere silhouette, panel joints, taller equipment building, walkway railings and rear ladder. Gameplay definitions, detection behavior and in-game visual testing are outside this model-only addition.
