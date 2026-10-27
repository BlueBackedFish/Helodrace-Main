# Oil machinery regression checks

`dotnet run --project Source/Tests/OilMachinery/OilMachinery.Tests.csproj` links production drilling, extraction, power-selection, maintenance-workgiver and operating-heat components against deterministic game-boundary stubs. It covers fuel-only drilling, maintenance/empty-tank/switch pauses, guaranteed completion, fuel shutdown after completion, discovery of the new rig by the workgiver, electric pumping over another occupied cell, pressure depletion, the existing mechanical pump and heat shutdown.

`python Source/Tests/OilMachinery/verify_definitions.py` checks new definition references, localization, the development job/workgiver link, fuel/ticker contract, footprint and four-direction color/mask links. Use the bundled Python with Pillow.

Build the main net48 project against the installed game's assemblies to check real API signatures. An isolated output/intermediate path avoids overwriting unrelated local build state. Boundary-stub tests do not establish in-game pathfinding, construction UI, refueling or visual behavior; those remain manual checks.
