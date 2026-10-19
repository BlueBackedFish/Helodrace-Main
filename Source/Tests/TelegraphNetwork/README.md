# Telegraph network checks

Run `dotnet run --project Source/Tests/TelegraphNetwork/TelegraphNetwork.Tests.csproj` from the repository root.

Run `Source/Tests/TelegraphNetwork/Verify-MenuProvider.ps1` in Windows PowerShell to compare the built provider's actual menu eligibility flags against installed RimWorld's vanilla trade provider. This checks drafted/undrafted selection without starting Unity.

The tests compile the production connection rules and map component against minimal game stubs. They cover payment from accessible notes, insufficient funds, repeated requests, LOW faction restrictions, independent settlement connections, installation deadlines, persisted provider/deadline/notification state, and opening-hour boundaries. The stubs exercise `ExposeData`; they do not replace an in-game save/load check.

In-game checks still required:

1. Build and fuel a telegraph. Verify use is unavailable before installation and the inspect text explains how to connect.
2. Select a colonist and right-click a visiting LOW caravan trader, both drafted and undrafted. Verify ordinary trading and the separate installation request both appear. Clicking the same caravan's escort must also offer installation and send the colonist to its merchant. HIGH traders and unrelated LOW visitors must not offer installation. A merchant's temporarily unavailable merchandise trade must not hide installation.
3. Request installation, cancel once, then confirm with 500 accessible Sthaler. Verify only confirmation charges the fee and repeating the request is unavailable.
4. Save/load during the 2–3 day wait. Verify completion time persists, the activation letter appears once, and another telegraph in the same settlement shares the connection.
5. At 08:00–17:59 local time, verify four company tabs appear. At 18:00–07:59, or without primary cells, verify all company names and buttons disappear.
6. Test the separate financial and commercial company requests, the combined request, and Harrington guard hiring as described in `Source/Tests/Communications/README.md`.
7. Verify contracts and support show only the contracted LOW faction. Recheck a support target after the relay closes; the request must fail without spending service capacity or ammunition. SCR-300 calls must retain their existing behavior.
