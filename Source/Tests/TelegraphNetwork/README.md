# Telegraph network checks

Run `dotnet run --project Source/Tests/TelegraphNetwork/TelegraphNetwork.Tests.csproj` from the repository root.

The tests compile the production connection rules and map component against minimal game stubs. They cover payment from accessible notes, insufficient funds, repeated requests, LOW faction restrictions, independent settlement connections, installation deadlines, persisted provider/deadline/notification state, and opening-hour boundaries. The stubs exercise `ExposeData`; they do not replace an in-game save/load check.

In-game checks still required:

1. Build and fuel a telegraph. Verify use is unavailable before installation and the inspect text explains how to connect.
2. Select an undrafted colonist and right-click a visiting LOW caravan trader. Verify ordinary trading and the separate installation request both appear. HIGH traders must not offer installation.
3. Request installation, cancel once, then confirm with 500 accessible Sthaler. Verify only confirmation charges the fee and repeating the request is unavailable.
4. Save/load during the 2–3 day wait. Verify completion time persists, the activation letter appears once, and another telegraph in the same settlement shares the connection.
5. At 08:00–17:59 local time, verify the directory shows caravan dispatch, forward base contracting, and military support. At 18:00–07:59, or without primary cells, verify all station names and buttons disappear.
6. Open caravan dispatch with one remaining primary cell. Verify the vanilla dialogue remains usable for that connected call, with normal goodwill and trader-request cooldowns, and the cell is consumed once.
7. Verify contracts and support show only the contracted LOW faction. Recheck a support target after the relay closes; the request must fail without spending service capacity or ammunition. SCR-300 calls must retain their existing behavior.
