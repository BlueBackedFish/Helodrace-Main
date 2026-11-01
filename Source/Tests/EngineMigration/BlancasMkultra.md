# Blancas MKultra migration checks

Use the build setup in [BlancasSleep.md](BlancasSleep.md): the shared Blancas16, Blancas17 and Main bridges all include `BlancasMkultraChecks.cs`. Run from the Main repository root with a preserved 1.6 game and the unchanged published Blancas 1.6 tree.

```powershell
Source/Tests/EngineMigration/Run-Migration.ps1 -Case blancas-mkultra-fixture -Legacy16 -GameRoot C:/path/to/preserved/RimWorld16 -BridgeAssembly "$PWD/Source/Tests/EngineMigration/bin/Blancas16/Debug/net48/Helodrace.EngineMigrationChecks.dll" -RuntimeRoot .codex-staging/migration17/mkultra-source16 -Wait
Source/Tests/EngineMigration/Run-Migration.ps1 -Case blancas-mkultra-load -FixtureRoot .codex-staging/migration17/mkultra-source16 -RuntimeRoot .codex-staging/migration17/mkultra-standalone17 -NoWindow -Wait
Source/Tests/EngineMigration/Run-Migration.ps1 -Case blancas-mkultra-main -FixtureRoot .codex-staging/migration17/mkultra-source16 -RuntimeRoot .codex-staging/migration17/mkultra-main17 -NoWindow -Wait
```

Use fresh runtime directories. If observation times out, poll the recorded PID and resume `Wait-Migration.ps1` on that runtime; never start another game while the recorded process remains alive.

The fixture creates healthy adult Baseliner humans and removes random generated scars/diseases before each case. Ingestion and the published surgery worker each run against prisoner, free foreign and colonist subjects with two, one or no intact eyes: 18 cases and 20 doses including two repeat doses. Calls go through actual `Thing.Ingested` and `BD_InjectMKultra.Worker.ApplyOnPawn`. Health changes, guest/faction changes, letters and diplomacy run normally; their methods are not mocked.

Checks cover recruitment eligibility, the module's own recruitment letter, goodwill change, one conditioning effect with its severity, permanent brain/eye injuries, missing-eye handling, and repeated-dose injury/death outcomes. Additional vanilla ideology/death letters are recorded separately; they are not counted as duplicate module recruitment letters. The module uses the host's shared Verse.Rand. Raw left/right eye selections are retained, but only the laterality of fresh randomized eye cuts is canonicalized when comparing different surrounding game contexts. The chosen eye must have been intact initially; fixed anatomy, injury amounts/counts, permanence and all other action fields must match. Saved injury locations are never canonicalized.

Actual 1.6 action records and 18 subjects, including two corpses, are saved. In 1.7, LoadedGame checks exact faction/prisoner/death status and every effect's type, body part, severity, age and permanence before the first simulation tick. New 1.7 actions run in GameComponentTick on the main thread, then all 36 old/new subjects are saved separately and reloaded for another exact check before ticking. Product save keys and types remain unchanged; the migration roundtrip flag belongs only to the test component.

Artifacts include `blancas-mkultra-actions.xml`, `blancas-mkultra-current-actions.xml`, `blancas-mkultra-comparison.txt`, initial/roundtrip state XML, original and new game saves, checks, launcher hashes, log and result. The copied 1.6 save is not overwritten.

Verified 2026-11-01: actual 1.6.4871 rev590 fixture `blancas-mkultra-fixture16c` passed 151 checks; actual 1.7.4995 standalone `blancas-mkultra-load17c` and Main integration `blancas-mkultra-main17` each passed 306. Fresh random eye choices differed in two standalone cases and zero Main cases; exact old and reloaded locations passed in both. The source fixture and both input copies share SHA-256 `39D8987B7841222767EE61F84A0EF63D63C59ADAB246A1F10F1EBB22D68AD6AD`.

This tests the module entry points and save compatibility, not bill/job scheduling, ingredient consumption, surgery work duration, every nonhuman/null-input path, all tale/message side effects, or long-term random mental-state frequency. It does not complete the remaining B2 drug overlap/withdrawal/removal work. No product code, balance or random-selection policy was changed.
