using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace.Tactics;
using RimWorld;
using Verse;
using Verse.AI;

internal static class TacticalFieldDoctrineTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
        var anchor = new IntVec3(80, 0, 80);
        foreach (var forward in GenAdj.CardinalDirections)
        {
            var side = new IntVec3(-forward.z, 0, forward.x);
            Check(TacticalFieldDoctrine.WithinEllipse(anchor, anchor + side * 25, forward, 25, 12)
                && !TacticalFieldDoctrine.WithinEllipse(anchor, anchor + forward * 13, forward, 25, 12),
                "The LOW anvil retains its bounded activity area.");
            var left = TacticalFieldManeuverPolicy.Candidate(anchor, anchor + forward * 35, -1, 5, false);
            var right = TacticalFieldManeuverPolicy.Candidate(anchor, anchor + forward * 35, 1, 5, false);
            Check(left.DistanceToSquared(anchor) == right.DistanceToSquared(anchor)
                && TacticalFieldManeuverPolicy.Angle(anchor, anchor + forward * 35, left) > 20,
                "Observed enemy geometry, rather than a fixed lateral target, creates alternative fire angles.");
            Check(TacticalFieldDoctrine.Offset(forward, 25, 0) == side * 25,
                "Vector scale must not change the requested physical distance.");
        }
        Check(TacticalFieldDoctrine.FlankSize(5) == 0 && TacticalFieldDoctrine.FlankSize(6) == 3
            && TacticalFieldDoctrine.FlankSize(40) == 4, "LOW sorties must leave a viable main body.");
        Check(TacticalFieldPolicy.CoverWeight(IntVec3.East, IntVec3.North * 20) == 0
            && TacticalFieldPolicy.CoverWeight(IntVec3.North, IntVec3.North * 20) == 1,
            "Side objects must not masquerade as cover against the incoming direction.");
        var focus = anchor + IntVec3.North * 40;
        Check(TacticalFieldSpacingPolicy.Penalty(anchor, anchor + IntVec3.North * 8, focus)
            > TacticalFieldSpacingPolicy.Penalty(anchor, anchor + IntVec3.East * 8, focus),
            "Incoming miss paths must penalize front/back alignment more than separated lateral covers.");
        Check(TacticalFieldSpacingPolicy.Close(anchor, anchor + IntVec3.East)
            && !TacticalFieldSpacingPolicy.Close(anchor, anchor + IntVec3.East * 2), "Adjacent clustering must be excluded while two-cell spacing remains possible.");
        Check(TacticalFieldPolicy.FormationAllowed(new TacticalContact { SeenTick = 100, PlayerControlled = true,
            Position = focus }, anchor, 100, engaged: true), "Actual player combat does not require an invented approach sample.");
        var search = new TacticalLowDefenseSearch { Origin = anchor, Forward = IntVec3.North, Needed = 2 };
        int calls = 0, worst = 0;
        while (!search.Scanned)
        {
            int before = calls;
            search.Advance(cell => { calls++; return cell.x == anchor.x + 20 ? 4 : 0; });
            worst = Math.Max(worst, calls - before);
        }
        Check(worst <= 8 && search.Candidates.Count <= 3, "Initial defense relocation must remain sliced and bounded.");

        TacticalFieldFormation Solo(string id) => new TacticalFieldFormation { Coordinator = id, Origin = anchor,
            Forward = IntVec3.North, Members = new List<string> { id }, Seen = new Dictionary<string,int> { [id] = 100 } };
        var a = Solo("A"); var b = Solo("B"); var c = Solo("C");
        Check(a.Members.Count == 1 && b.Coordinator == "B", "Uncommunicated squads must remain independent.");
        Check(b.Receive(a.Copy(), "B", "A", 100, 120) && b.Coordinator == "A", "Delivered voice or radio reports establish the sortie coordinator.");
        b.Seen["B"] = 120;
        Check(a.Receive(b.Copy(), "A", "B", 120, 140) && a.Members.SequenceEqual(new[] { "A", "B" }),
            "The reply must inform the sender of its peer.");
        a.Seen["A"] = 140;
        Check(c.Receive(a.Copy(), "C", "A", 140, 160) && c.Coordinator == "A" && c.Members.Count == 3,
            "A newly contacted squad must join the existing single-coordinator group.");
        Check(b.Expire("B", 800), "An expired coordinator must release isolated squads to local decisions.");
        var copied = a.Copy(); copied.Members.Clear();
        Check(a.Members.Count == 2 && !b.Receive(a.Copy(), "B", "A", 100, 800), "Reports must be value copies and reject stale deliveries.");

        foreach (Type patch in new[] { typeof(Patch_NewTactical_FieldJobs), typeof(Patch_NewTactical_AdoptFieldJob),
            typeof(Patch_NewTactical_NativeFieldShot) })
        {
            HarmonyMethod info = HarmonyMethod.Merge(HarmonyMethodExtensions.GetFromType(patch));
            var target = AccessTools.DeclaredMethod(info.declaringType, info.methodName, info.argumentTypes);
            Check(target != null, "Native field patch target missing.");
            foreach (var hook in patch.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "Prefix" || m.Name == "Postfix"))
                foreach (var parameter in hook.GetParameters())
                {
                    Type actual = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType() : parameter.ParameterType;
                    Type expected = parameter.Name == "__instance" ? target.DeclaringType : parameter.Name == "__result" ? target.ReturnType
                        : parameter.Name.StartsWith("___") ? AccessTools.Field(target.DeclaringType, parameter.Name.Substring(3))?.FieldType
                        : target.GetParameters().FirstOrDefault(p => p.Name == parameter.Name)?.ParameterType;
                    if (expected?.IsByRef == true) expected = expected.GetElementType();
                    Check(expected == actual, "Native field hook parameter mismatch: " + parameter.Name);
                }
        }
        var service = new MapComponent_TacticalCommands(null);
        var pawn = new Pawn { jobs = (Pawn_JobTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_JobTracker)) };
        var pending = new Dictionary<Pawn, Job>(ReferenceEqualityComparer.Instance); var candidate = new Job();
        AccessTools.Field(service.GetType(), "pendingFieldJobs").SetValue(service, pending); pending[pawn] = candidate;
        AccessTools.Method(service.GetType(), "AdoptFieldNative").Invoke(service, new object[] { pawn, candidate });
        Check(pending.Count == 0 && pawn.CurJob == null, "An unsuccessful native job start must not adopt another job.");
        Console.WriteLine("PASS: " + checks + " bounded anvil/hammer geometry, contact-only cooperation and native field bindings.");
    }
}
