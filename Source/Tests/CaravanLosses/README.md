# Caravan loss ledger checks

From the repository root:

```powershell
dotnet run --project Source/Tests/CaravanLosses/CaravanLosses.Tests.csproj
Source/Tests/Communications/Verify-SafeConductHooks.ps1
```

The standalone suite executes the production ledger and attack/drop/death tracking against minimal native-boundary stubs. It verifies player-only attribution, Helod caravan eligibility, actual merged-drop quantities, callback preservation, duplicate notifications, pre-attack pawn valuation, injury/death separation, nested hits, third-party damage, delayed bleeding deaths, departed/new visits, exception cleanup, faction filtering, deep serialization and translations.

It also verifies the forward-base and loan arrears categories, silver conversion, contract-key deduplication after save/load and saved goodwill deductions. `TelegraphNetwork.Tests` executes the production default handler and financial penalties together with loan repayments.

The native Harmony script registers the actual `TakeDamage`, `Kill`, `MakeDowned` and both full/partial `ThingOwner.TryDrop` overloads, together with the safe-conduct hooks. It does not execute a Unity combat scene.

In-game checks:

1. Attack a visiting LOW or HIGH caravan. Compare the ledger with actual dropped weapons, apparel and carried stock. Check merged stacks use only the amount dropped and that stock retained by the pawn is not recorded. Keep normal and financial goodwill penalties independent.
2. Injure a pawn, save/reload, and finish it off. Injury plus death must equal the pawn's original pre-attack value; dropped goods remain separate. Check delayed player-wound bleeding death and third-party finishing blows.
3. Let a survivor leave and revisit in another caravan. Preserve old entries and start a new visit account. Open the National Industrial Credit Corporation loss ledger and browse faction/category totals and the full scrollable history.
4. Trade with a safe-conduct caravan: only financial goodwill should rise. Ordinary caravans and gifts must retain their existing diplomatic rules.
