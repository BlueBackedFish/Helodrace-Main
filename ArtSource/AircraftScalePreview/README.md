# Aircraft physical-scale preview

`output/C17_F22_F15EX_real_scale_roll90.png` compares the existing C-17, F-22 and F-15EX artwork at 22 pixels per metre. C-17 remains the reference (53 m body length, existing 19-tile canvas). Top-view noses are aligned. Fighters are repeated at a true +90-degree roll about their longitudinal +Y axis, using the saved models at neutral frame 1. This is a 3D roll, not rotation of the PNG in its image plane.

Existing top-view textures, shape, paint and model files are preserved. Roll renders use the same emission materials and orthographic projection, without lights or shadows. Their outside outline has the same physical width as the C-17's existing 12-pixel outline. Transparent plain roll renders are also retained.

Physical lengths follow the existing model calibration: C-17 53 m, F-22 18.9 m, F-15EX 19.4 m. The existing references are [USAF C-17](https://www.af.mil/About-Us/Fact-Sheets/Display/Article/1529726/c-17-globemaster-iii/c-17-globemaster-iii/), [USAF F-22](https://www.af.mil/About-Us/Fact-Sheets/Display/Article/104506/f-22-raptor/), and [Boeing F-15EX](https://www.boeing.com/defense/fighters-and-bombers/f-15ex-eagle). F-15EX retains the supplied model's 13.19 m span, about 1.5% wider than Boeing's rounded 13.0 m specification; no independent-axis stretching is applied.

Regenerate from the repository root with Python, numpy and Pillow >= 9.1:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background -t 6 --python-exit-code 1 --python ArtSource/AircraftScalePreview/render_roll.py
python ArtSource/AircraftScalePreview/compose.py
```

`roll_validation.json` checks preserved longitudinal bounds, rotated transverse bounds and unchanged source model hashes. `comparison_validation.json` checks raster body lengths at the shared physical scale. Both fighter side silhouettes are slightly shorter than their 3D bounds because thin nozzle/tail faces become edge-on at exactly 90 degrees; their scale is preserved rather than stretching the raster to compensate. Visual review covers proportions, unclipped silhouettes, true side profiles and text readability. This preview does not change runtime assets or provide an in-game flight test.
