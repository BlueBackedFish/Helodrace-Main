# HAM and company communications checks

From the repository root, run:

```powershell
dotnet run --project Source/Tests/Communications/Communications.Tests.csproj
dotnet run --project Source/Tests/TelegraphNetwork/TelegraphNetwork.Tests.csproj
Source/Tests/Communications/Verify-HamHooks.ps1
```

The communications tests exercise production HAM scheduling, independent company dispatches, atomic combined requests, request/travel boundaries, arrival retries, persisted schedule values, company names, exact trader-kind definitions, elite guard definitions, and matching translations. The telegraph tests cover installation and payments with minimal game stubs.

In-game validation still required:

1. Use a powered vanilla comms console. It must offer only HAM listening/chatting, with no faction, orbital-trader, or Biotech boss-call menu. Verify loss of power or interference cancels the job. Check an existing base-class console too: routing must not depend on the HAM subclass. LOW must be absent from other consoles' normal communication lists, and direct LOW dialogue requests must be blocked. A queued vanilla communication job on a HAM console must turn into HAM listening.
2. Use multiple HAM radios in one settlement on one day. There must be only one 15% mission roll; saving and reloading must not reroll it. A successful roll uses the game's eligible random-quest selection and may still fail if no quest is available.
3. Open an installed telegraph during business hours. Verify four separate company tabs. Close or empty the line and verify every company tab disappears.
4. With an allied LOW faction and sufficient primary cells, request National Industrial Credit Corporation and Central Hardware & Supply Co. individually at the same tick, then test the combined request on a fresh schedule. Verify the exact two trader kinds arrive in 1–2 days and do not share a cooldown.
5. Save/load while both caravans are pending. Each must arrive once, retaining its type, deadline, and four-day request cooldown. A rejected combined request must not partially charge or schedule anything.
6. Check Pioneer Overseas Service Company contracts, contract records, and forward-base services, including returning from world-map targeting to the correct tab.
7. Hire Harrington Protective Services with 500 Sthaler and one primary cell. Verify one elite caravan guard captain and five elite caravan guards enter from a reachable map edge under the LOW faction, assist for one day, and then leave. Check save/load during that day, insufficient funds, blocked entry, and the one-day rehire cooldown.
