# Independent early industrial generator

New 3×3 open-field dynamo design for the existing `HD_EarlyIndustrialGenerator` definition. The old shared motor image/mask are preserved as family/style references, not reconstruction targets. The generator has an open iron yoke, two prominent copper field coils, an exposed armature, copper commutator with dark carbon brushes, a large belt-input pulley, and an output terminal board. It is mechanically and visually distinct from the new enclosed motor.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --threads 2 --python-exit-code 1 --python ArtSource/EarlyIndustrialGenerator/build_generator.py
```

The scene is `HD_EarlyIndustrialGenerator.blend`; texture prefix is `Buildings/HD_EarlyIndustrialGenerator`, referenced only by the generator building. Three 64° orthographic views and corresponding color masks are supplied. Iron is tintable; copper windings/commutator, dark brushes and insulating board retain their fixed colors. The building's existing mechanical-to-electric behavior and 3×3 footprint are unchanged. Rendering/mask checks are recorded in `validation.json`; no in-game test is claimed.
