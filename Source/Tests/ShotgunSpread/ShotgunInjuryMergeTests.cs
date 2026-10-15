using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using HarmonyLib;
using Helodrace;
using RimWorld;
using Verse;

internal static class ShotgunInjuryMergeTests
{
    private static readonly HashSet<Hediff> tended = new HashSet<Hediff>();
    private static T Bare<T>() => (T)FormatterServices.GetUninitializedObject(typeof(T));
    private static bool SkipEngineSideEffects() => false;
    private static bool PartMaxHealth(ref float __result) { __result = 100f; return false; }
    private static bool TendingFixture(Hediff hd, ref bool __result)
    {
        if (!tended.Contains(hd)) return true;
        __result = true;
        return false;
    }

    private sealed class MergeObserver : HediffComp
    {
        internal int merges;
        public override void CompPostMerged(Hediff other) { merges++; }
    }

    private static Hediff_Injury Injury(Pawn pawn, BodyPartRecord part, HediffDef def, float severity)
    {
        return new Hediff_Injury { pawn = pawn, def = def, Part = part, Severity = severity };
    }

    internal static void Run(Harmony harmony, Action<bool, string> check)
    {
        // Exercise the real AddDirect and TryMergeWith loops. Rendering, cache
        // refresh and pawn stats need a running game, so stub those boundaries.
        foreach (var method in new[]
        {
            AccessTools.Method(typeof(HediffSet), nameof(HediffSet.DirtyCache)),
            AccessTools.Method(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.Notify_HediffChanged)),
            AccessTools.Method(typeof(Hediff_Injury), nameof(Hediff_Injury.PostAdd))
        })
            harmony.Patch(method, prefix: new HarmonyMethod(typeof(ShotgunInjuryMergeTests), nameof(SkipEngineSideEffects)));
        harmony.Patch(AccessTools.Method(typeof(BodyPartDef), nameof(BodyPartDef.GetMaxHealth)),
            prefix: new HarmonyMethod(typeof(ShotgunInjuryMergeTests), nameof(PartMaxHealth)));
        harmony.Patch(AccessTools.Method(typeof(HediffUtility), nameof(HediffUtility.IsTended)),
            prefix: new HarmonyMethod(typeof(ShotgunInjuryMergeTests), nameof(TendingFixture)));

        var core = new BodyPartRecord { def = new BodyPartDef(), coverageAbs = 0.5f };
        var part = new BodyPartRecord { def = new BodyPartDef(), parent = core, coverageAbs = 0.2f };
        var sibling = new BodyPartRecord { def = new BodyPartDef(), parent = core, coverageAbs = 0.2f };
        core.parts.Add(part);
        core.parts.Add(sibling);
        var body = new BodyDef { corePart = core };
        body.AllParts.AddRange(new[] { core, part, sibling });
        var pawn = Bare<Pawn>();
        pawn.def = Bare<ThingDef>();
        pawn.def.race = new RaceProperties { body = body };
        pawn.health = Bare<Pawn_HealthTracker>();
        pawn.health.hediffSet = new HediffSet(pawn);
        pawn.needs = new Pawn_NeedsTracker();
        var set = pawn.health.hediffSet;
        var gunshot = new HediffDef { defName = "TestGunshot", injuryProps = new InjuryProps { canMerge = false } };
        var bruise = new HediffDef { defName = "TestBruise", injuryProps = new InjuryProps { canMerge = false } };
        var damageDef = new DamageDef { defName = "TestBullet" };
        var damage = new DamageInfo(damageDef, 6f, angle: 0);
        var first = Injury(pawn, part, gunshot, 6f);
        var observer = new MergeObserver { parent = first };
        first.comps.Add(observer);

        ShotgunSpreadContext.Impact = new ShotgunImpact { pawn = pawn, shell = new ShotgunShell(1), damageDef = damageDef };
        try
        {
            set.AddDirect(first, damage);
            for (int pellet = 1; pellet < 9; pellet++)
                set.AddDirect(Injury(pawn, part, gunshot, 6f), damage);
            check(set.hediffs.Count == 1 && first.Severity == 54f, "Nine same-part pellets must become one injury with summed severity.");
            check(set.GetPartHealth(part) == 46f, "Merged wounds must retain the full pellet damage.");
            check(observer.merges == 8, "Existing injury comps must receive merge notifications.");
            check(ShotgunInjuryMerge.Current == null, "Injury addition must restore its merge context.");

            // A later shell can continue the same untreated wound.
            ShotgunSpreadContext.Impact = new ShotgunImpact { pawn = pawn, shell = new ShotgunShell(1), damageDef = damageDef };
            first.ageTicks = 500;
            set.AddDirect(Injury(pawn, part, gunshot, 3f), new DamageInfo(damageDef, 3f, angle: 0));
            check(set.hediffs.Count == 1 && first.Severity == 57f && first.ageTicks == 0, "A later shotgun hit must enlarge and refresh the untreated wound.");

            set.AddDirect(Injury(pawn, sibling, gunshot, 4f), damage);
            set.AddDirect(Injury(pawn, part, bruise, 2f), damage);
            check(set.hediffs.Count == 3 && first.Severity == 57f, "Different parts and injury types must stay separate.");

            var duplicate = Injury(pawn, part, gunshot, 2f);
            set.hediffs.Add(duplicate);
            set.AddDirect(Injury(pawn, part, gunshot, 5f), damage);
            check(first.Severity == 62f && duplicate.Severity == 2f && set.GetPartHealth(part) == 34f,
                "An incoming pellet must be added once even when several matching wounds exist.");

            set.hediffs.Clear();
            var scar = Injury(pawn, part, gunshot, 3f);
            scar.comps.Add(new HediffComp_GetsPermanent { parent = scar, isPermanentInt = true });
            var bandaged = Injury(pawn, part, gunshot, 4f);
            tended.Add(bandaged);
            set.hediffs.AddRange(new Hediff[] { scar, bandaged });
            set.AddDirect(Injury(pawn, part, gunshot, 6f), damage);
            check(set.hediffs.Count == 3 && scar.Severity == 3f && bandaged.Severity == 4f,
                "Existing scars and tended wounds must not absorb fresh pellets.");
            var fresh = (Hediff_Injury)set.hediffs[2];
            check(!ShotgunInjuryMerge.CanMerge(fresh, scar) && !ShotgunInjuryMerge.CanMerge(fresh, bandaged),
                "Incoming permanent or tended injuries must not be merged either.");
            set.AddDirect(Injury(pawn, part, gunshot, 2f), damage);
            check(set.hediffs.Count == 3 && fresh.Severity == 8f, "Later pellets must find the fresh wound after skipping treated and permanent wounds.");

            ShotgunSpreadContext.Impact = null;
            set.AddDirect(Injury(pawn, part, gunshot, 1f), damage);
            check(set.hediffs.Count == 4 && fresh.Severity == 8f, "Ordinary bullets must keep vanilla injury merging behavior.");
            ShotgunSpreadContext.Impact = new ShotgunImpact { pawn = pawn, shell = new ShotgunShell(1), damageDef = damageDef };
            set.AddDirect(Injury(pawn, part, gunshot, 1f));
            check(set.hediffs.Count == 5, "Injuries added without damage info must not inherit a pellet merge.");

            var outer = new ShotgunInjuryMerge { incoming = fresh };
            ShotgunInjuryMerge.Current = outer;
            ShotgunSpreadContext.Impact = new ShotgunImpact { pawn = Bare<Pawn>() };
            Patch_HediffSet_ShotgunInjuryMerge.Prefix(set, fresh, damage, out ShotgunInjuryMerge state);
            check(ShotgunInjuryMerge.Current == null, "Another pawn's injury must suspend the merge context.");
            Patch_HediffSet_ShotgunInjuryMerge.Finalizer(state);
            check(ShotgunInjuryMerge.Current == outer, "Nested injury additions must restore the previous merge context.");
            ShotgunSpreadContext.Impact = new ShotgunImpact { pawn = pawn };
            try
            {
                set.AddDirect(new Hediff_Injury { pawn = pawn, Part = part }, damage);
                throw new InvalidOperationException("Expected invalid injury rejection.");
            }
            catch (Exception error) when (error.Message.Contains("null def")) { }
            check(ShotgunInjuryMerge.Current == outer, "Rejected injury additions must restore the merge context after an exception.");
        }
        finally
        {
            ShotgunSpreadContext.Impact = null;
            ShotgunInjuryMerge.Current = null;
            tended.Clear();
        }
    }
}
