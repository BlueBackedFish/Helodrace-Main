# Independent early industrial electric motor

New enclosed 3×3 motor for `HD_EarlyIndustrialLargeMotor`. The old shared electrical-machine texture is retained in `Reference/` for style/context. This design uses a cylindrical stator, eight long cooling ribs, end shields, a raised terminal box with lid, two saddle feet, a shaft output coupling, and a rear fan cover. Its closed gray housing and cooling ribs distinguish it from the open copper-coil generator. Fan ventilation is colored directly onto partitioned faces of the end disk; no vent bars protrude from that surface.

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.1\blender.exe' -b --threads 2 --python-exit-code 1 --python ArtSource/EarlyIndustrialMotor/build_motor.py
```

Scene: `HD_EarlyIndustrialLargeMotor.blend`. The existing motor prefix, 3×3 footprint and electrical-to-mechanical behavior are retained. The generator now has its own prefix, scene and design. Three transparent 512px directions and material masks are exported at 64° with a common scale. The iron housing remains tintable; the cable gland retains its dark insulating color. See `validation.json` and the review sheets. In-game testing has not been performed.
