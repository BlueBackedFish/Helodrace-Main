# Safe-conduct trade and door access

Run from the repository root:

```powershell
dotnet run --project Source/Tests/SafeConductAccess/SafeConductAccess.Tests.csproj
Source/Tests/Communications/Verify-SafeConductHooks.ps1
```

The standalone checks compile the production access wrappers and faction scope against minimal native-boundary stubs. They cover the additional trading-menu hostility rejection, real royal-permit and social restrictions, matching the actual clicked trader/faction/kind, player negotiators, ordinary hostile and neutral visitors, merchant/guard/carrier access to player doors, prisoner restrictions, unrelated factions' doors, safe-conduct breach, and nested/exception-safe scope cleanup.

The native Harmony script registers all nine patches against the installed game, including the exact trade-menu iterator and door eligibility method. This verifies both instruction replacements and the clicked-pawn field. Native movement, door/fence behavior and the rendered trade menu still require in-game checks listed in `../Communications/README.md`.
