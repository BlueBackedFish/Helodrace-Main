# H trade integration checks

Run `Source/Tests/TradeStageH/Start-Checks.ps1 -RuntimeRoot .codex-staging/TradeStageH/fresh-name`, then collect the same process with `Source/Tests/Wait-Game17Host.ps1 -RuntimeRoot .codex-staging/TradeStageH/fresh-name -TimeoutSeconds 45`. A fresh isolated profile uses actual installed 1.7, Harmony 3, Main and Blancas. No host is launched over a running game.

Checks exercise actual incidents, caravan stock on pack animals, weighted random trader selection, buying/selling with native TradeDeal, world caravans at both settlement types, hostile menu refusal and native 30-day stock expiration. Scenario supply state is saved before and after delivery; the first basket uses only the scenario's 150 STL. Entry-search failure is narrowly injected at RCellFinder to test incident retry. Weather is fixed at 21°C and the storyteller is disabled; normal animal simulation is preserved.

Commitment checks install and pay for an actual telegraph connection, request at low financial goodwill, save pending/arrived state, split purchases, exchange currency, give a gift, and wait for native normal caravan departure. They also test actual player dismissal, request cancellation, hostile nonarrival, external/player harm notification, one-time financial settlement and the standard financial request route. Request travel/cooldown deadlines are advanced explicitly to avoid waiting irrelevant days; no native goods transfer or normal departure is faked. Every update pauses before explicit ticks.

Final full run: `.codex-staging/TradeStageH/final-01`, 104 PASS and COMPLETE. Detailed scope and limitations: `Docs/1.7 2차 H 거래 실행 기록.md`.
