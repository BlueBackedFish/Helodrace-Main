using System;
using System.Collections.Generic;
using HarmonyLib;
using Helodrace;
using Verse;

internal static class ShotgunInjuryMergeTests
{
    private static readonly HashSet<Hediff> tended = new HashSet<Hediff>();
    private static bool TendingFixture(Hediff hd, ref bool __result)
    {
        if (!tended.Contains(hd)) return true;
        __result = true; return false;
    }
    private sealed class MergeObserver : HediffComp
    {
        internal int merges;
        public override void CompPostMerged(Hediff other) { merges++; }
    }
    private static Hediff_Injury Injury(Pawn pawn, BodyPartRecord part, HediffDef def, float severity) =>
        new Hediff_Injury { pawn = pawn, def = def, Part = part, Severity = severity };
    private static void Impact(Pawn pawn, int shell, int pellet)
    {
        var projectile = Program.Bare<Projectile_ShotgunPellet>();
        projectile.thingIDNumber = pellet;
        ShotgunSpreadContext.Impact = new ShotgunImpact { pawn = pawn, shell = new ShotgunShell { id = shell }, pellet = projectile };
    }
    internal static void Run(Harmony harmony)
    {
        harmony.Patch(AccessTools.Method(typeof(HediffUtility), nameof(HediffUtility.IsTended)), prefix: new HarmonyMethod(typeof(ShotgunInjuryMergeTests), nameof(TendingFixture)));
        var body = new BodyDef();
        var core = Program.Part(body, "Core", 0.5f);
        var part = Program.Part(body, "Arm", 0.3f, core);
        var sibling = Program.Part(body, "Wing", 0.2f, core);
        var pawn = Program.Pawn(body);
        var set = pawn.health.hediffSet;
        var def = new HediffDef { defName = "FixtureGunshot", injuryProps = new InjuryProps { canMerge = false } };
        var bruise = new HediffDef { defName = "FixtureBruise", injuryProps = new InjuryProps { canMerge = false } };
        var damage = new DamageInfo(new DamageDef(), 6f, angle: 0);
        var ledger = GameComponent_ShotgunWounds.Current;
        var first = Injury(pawn, part, def, 6f);
        var observer = new MergeObserver { parent = first };
        first.comps.Add(observer);
        try
        {
            Impact(pawn, 200, 200);
            set.AddDirect(first, damage);
            for (int i = 1; i < 9; i++)
            {
                Impact(pawn, 200, 200 + i);
                set.AddDirect(Injury(pawn, part, def, 6f), damage);
            }
            Program.Check(set.hediffs.Count == 1 && first.Severity == 54f, "One shell's nine same-part pellets must produce one severity-54 wound.");
            Program.Near(set.GetPartHealth(part), 46f, "Merging must preserve actual part damage.");
            Program.Check(ledger.For(first).pelletCount == 9 && observer.merges == 8, "The wound must retain its pellet count and notify native comps.");
            Program.Check(ShotgunInjuryMerge.Current == null, "Injury addition must restore merge context.");
            // A later shot must remain separate even for a normally mergeable definition.
            def.injuryProps.canMerge = true;
            Impact(pawn, 300, 300);
            var later = Injury(pawn, part, def, 3f);
            set.AddDirect(later, damage);
            Program.Check(set.hediffs.Count == 2 && first.Severity == 54f && later.Severity == 3f, "Separate shells must not merge old wounds.");
            var duplicate = Injury(pawn, part, def, 2f);
            set.hediffs.Add(duplicate);
            ledger.Register(duplicate, 300, 301);
            Impact(pawn, 300, 302);
            set.AddDirect(Injury(pawn, part, def, 5f), damage);
            Program.Check(later.Severity == 8f && duplicate.Severity == 2f && first.Severity == 54f, "A pellet must merge once, only into a wound from its own shell.");
            // Multiple damage paths of one pellet must not count it twice.
            set.AddDirect(Injury(pawn, part, def, 1f), damage);
            Program.Check(ledger.For(later).pelletCount == 2 && later.Severity == 9f, "One pellet contributing twice must count once while retaining its damage.");
            set.AddDirect(Injury(pawn, sibling, def, 4f), damage);
            set.AddDirect(Injury(pawn, part, bruise, 2f), damage);
            Program.Check(set.hediffs.Count == 5, "Different parts and injury types must remain distinct.");
            set.hediffs.Clear();
            var scar = Injury(pawn, part, def, 3f);
            scar.comps.Add(new HediffComp_GetsPermanent { parent = scar, isPermanentInt = true });
            var bandaged = Injury(pawn, part, def, 4f);
            tended.Add(bandaged);
            set.hediffs.AddRange(new Hediff[] { scar, bandaged });
            ledger.Register(scar, 300, 303);
            ledger.Register(bandaged, 300, 304);
            Impact(pawn, 300, 305);
            set.AddDirect(Injury(pawn, part, def, 6f), damage);
            Program.Check(set.hediffs.Count == 3 && scar.Severity == 3f && bandaged.Severity == 4f, "Scars and tended wounds must not absorb fresh pellets.");
            var fresh = (Hediff_Injury)set.hediffs[2];
            Impact(pawn, 300, 306);
            set.AddDirect(Injury(pawn, part, def, 2f), damage);
            Program.Check(set.hediffs.Count == 3 && fresh.Severity == 8f, "Later pellets must find the fresh wound after skipping treated wounds.");
            def.maxSeverity = 8.5f;
            Impact(pawn, 300, 307);
            set.AddDirect(Injury(pawn, part, def, 2f), damage);
            Program.Check(set.hediffs.Count == 4 && fresh.Severity == 8f, "A severity cap must not silently discard pellet damage during merging.");
            def.injuryProps.canMerge = false;
            ShotgunSpreadContext.Impact = null;
            set.AddDirect(Injury(pawn, part, def, 1f), damage);
            Program.Check(set.hediffs.Count == 5, "Ordinary bullets must keep vanilla merge rules.");
            var outer = new ShotgunInjuryMerge();
            ShotgunInjuryMerge.Current = outer;
            Impact(pawn, 400, 400);
            try
            {
                set.AddDirect(new Hediff_Injury { pawn = pawn, Part = part }, damage);
                throw new InvalidOperationException("Expected invalid injury rejection.");
            }
            catch (Exception error) when (error.Message.Contains("null def")) { }
            Program.Check(ShotgunInjuryMerge.Current == outer, "Exceptions must restore the previous wound merge scope.");
        }
        finally { ShotgunSpreadContext.Impact = null; ShotgunInjuryMerge.Current = null; tended.Clear(); }
    }
}
