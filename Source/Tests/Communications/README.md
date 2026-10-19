# HAM and company communications checks

From the repository root, run:

```powershell
dotnet run --project Source/Tests/Communications/Communications.Tests.csproj
dotnet run --project Source/Tests/TelegraphNetwork/TelegraphNetwork.Tests.csproj
Source/Tests/Communications/Verify-HamHooks.ps1
```

The communications tests exercise deterministic daily HAM rumor planning, the production map component's initialization, local-midnight updates and save/load, one-shot rumor consumption across LOW/HIGH, failed quest generation, dialogue choices and deferred replies, independent company dispatches, atomic combined requests, request/travel boundaries, arrival retries, persisted schedule values, company names, exact trader-kind definitions, elite guard definitions, and matching translations. Map/incident and dialogue tests use minimal game stubs. The Harmony script checks real game registration, verifies that HAM only patches the dialogue-content factory, and checks that other factions and orbital ships are excluded without starting Unity.

In-game validation still required:

1. Use a powered vanilla comms console. LOW and HIGH must retain their normal contact option, including faction icon, faction relation and numeric goodwill, and use the vanilla communication job. The standard negotiation window, faction header and radio ambience must open as usual. The root choices are only chat and disconnect. Chat replies, including occasional rumors, stay inside that window with back and disconnect choices. Other factions, orbital traders and Biotech boss-call components must retain their vanilla menus and behavior. The console must keep its vanilla class, name and description. Power, interference, reservations and leader availability follow the vanilla job and menu rules.
2. Each settlement's rumor calendar is fixed by the world seed, map ID and local date at a 15% daily chance, independent of conversations and the global RNG. The map prepares the result at initialization and each local midnight and saves the date, outcome and consumption flag. On a rumor day, the first eligible chat can reveal a mission as a rumor, once per settlement across both LOW/HIGH and all consoles. Subsequent chats stay ordinary. On other days every chat stays ordinary. Reopening the window, repeated clicking, save/load, and reloading before midnight must not reroll the date's outcome. If vanilla random-quest selection cannot generate an eligible quest, that day's attempt is still consumed and the reply stays ordinary.
3. Open an installed telegraph during business hours. Expand the connected LOW faction's full-width row (with its icon and goodwill), then choose a company row to open that company's work tab below. Verify scrolling through the directory, changing companies, closing the work tab while retaining the expanded list, and collapsing the faction to close its work tab. Going offline must hide every faction/company name and clear the selected tab. Verify Korean company names in the directory, arriving caravans, contract provider and notifications. Existing company arrival schedules and cooldowns must survive tab changes.
4. With an allied LOW faction and sufficient primary cells, request National Industrial Credit Corporation and Central Hardware & Supply Co. individually at the same tick, then test the combined request on a fresh schedule. Verify the exact two trader kinds arrive in 1–2 days and do not share a cooldown.
5. Save/load while both caravans are pending. Each must arrive once, retaining its type, deadline, and four-day request cooldown. A rejected combined request must not partially charge or schedule anything.
6. Check Pioneer Overseas Service Company contracts, contract records, and forward-base services, including returning from world-map targeting to the correct tab.
7. Hire Harrington Protective Services with 500 Sthaler and one primary cell. Verify one elite caravan guard captain and five elite caravan guards enter from a reachable map edge under the LOW faction, assist for one day, and then leave. Check save/load during that day, insufficient funds, blocked entry, and the one-day rehire cooldown.
