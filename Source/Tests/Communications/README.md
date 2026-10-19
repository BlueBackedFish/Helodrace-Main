# HAM and company communications checks

From the repository root, run:

```powershell
dotnet run --project Source/Tests/Communications/Communications.Tests.csproj
dotnet run --project Source/Tests/TelegraphNetwork/TelegraphNetwork.Tests.csproj
Source/Tests/Communications/Verify-HamHooks.ps1
```

The communications tests exercise production HAM scheduling, LOW/HIGH scope, dialogue choices and deferred replies, independent company dispatches, atomic combined requests, request/travel boundaries, arrival retries, persisted schedule values, company names, exact trader-kind definitions, elite guard definitions, and matching translations. The telegraph tests cover installation and payments with minimal game stubs. The Harmony script checks real game registration, verifies that HAM only patches the dialogue-content factory, and checks that other factions and orbital ships are excluded without starting Unity.

In-game validation still required:

1. Use a powered vanilla comms console. LOW and HIGH must retain their normal contact option, including faction icon, faction relation and numeric goodwill, and use the vanilla communication job. The standard negotiation window, faction header and radio ambience must open as usual. Only its dialogue text and choices change: chat, ask about missions, and disconnect. Chat and mission results stay inside that window with back and disconnect choices. Other factions, orbital traders and Biotech boss-call components must retain their vanilla menus and behavior. The console must keep its vanilla class, name and description. Power, interference, reservations and leader availability follow the vanilla job and menu rules.
2. Contact LOW and HIGH using multiple consoles in one settlement on one day. Opening the window and chatting must not consume a mission roll. Asking about missions gets one shared 15% daily roll; repeated inquiries and saving/reloading must not reroll it. A successful roll uses the game's eligible random-quest selection and may still fail if no quest is available.
3. Open an installed telegraph during business hours. Verify four separate company tabs. Close or empty the line and verify every company tab disappears.
4. With an allied LOW faction and sufficient primary cells, request National Industrial Credit Corporation and Central Hardware & Supply Co. individually at the same tick, then test the combined request on a fresh schedule. Verify the exact two trader kinds arrive in 1–2 days and do not share a cooldown.
5. Save/load while both caravans are pending. Each must arrive once, retaining its type, deadline, and four-day request cooldown. A rejected combined request must not partially charge or schedule anything.
6. Check Pioneer Overseas Service Company contracts, contract records, and forward-base services, including returning from world-map targeting to the correct tab.
7. Hire Harrington Protective Services with 500 Sthaler and one primary cell. Verify one elite caravan guard captain and five elite caravan guards enter from a reachable map edge under the LOW faction, assist for one day, and then leave. Check save/load during that day, insufficient funds, blocked entry, and the one-day rehire cooldown.
