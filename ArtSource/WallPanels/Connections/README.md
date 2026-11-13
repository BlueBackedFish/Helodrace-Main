# Blender wall connections

Sixteen masks use N=1, E=2, S=4, W=8. Geometry is a union-extruded outline with no internal wall faces at junctions. The cross, T and corner variants meet neighboring modules at identical 0.18-wide ports.

`HD_SandwichWall_Connections.blend` contains the gallery and packed atlas. Each GLB is an independent model at the origin; the gallery spacing is applied only after its export.

`../Runtime/Wall_*.obj` is adapted specifically for the installed 1.7 Waylith ObjMeshLoader: that loader reflects OBJ X and reverses winding. The export compensates for this behavior. Generic OBJ viewers may therefore show a horizontally mirrored asset; the GLBs are intended for generic viewers. Native loader checks verify the actual NESW ports and normals.

Runtime models and the atlas are copied to the mod's `Meshes/WallPanels` and `Textures/Buildings/WallPanels` directories. `Building_SandwichPanelWall` chooses the mask and draws the native mesh. The engine map view projects authored height into a short northward facade and constrains depth to the building layers.

The Blender renders are art QA. In-game placement/rendering and wall-to-door appearance still require live verification. Model source thickness .18 is 75% of an assumed .24 baseline; the baseline is not an engine wall mesh measurement.

Regenerate with Blender background mode and `build_connections_blender.py`, after `build_wall_blender.py`. `render_panel_icons.py -- --output <mod Textures/Buildings/WallPanels>` rebuilds the construction icons.
