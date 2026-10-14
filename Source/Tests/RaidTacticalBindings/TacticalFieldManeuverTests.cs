using System;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

internal static class TacticalFieldManeuverTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string reason) { checks++; if (!condition) throw new Exception(reason); }
        Check(TacticalFieldPolicy.Continue(1000, 500, -1), "Short observation gaps must preserve the field mission.");
        Check(TacticalFieldPolicy.Continue(1000, 0, 900), "Actual recent shots must preserve combat after stale scheduled observation.");
        Check(!TacticalFieldPolicy.Continue(900, 0, -1), "A lost mission must terminate after its bounded grace period.");
        Check(!TacticalFieldPolicy.Prepare(false, false, false, 30, 31 * 31), "Attackers must keep approaching a distant passive observation.");
        Check(TacticalFieldPolicy.Prepare(false, false, false, 30, 30 * 30), "Targets at the weapon boundary allow preparation.");
        Check(TacticalFieldPolicy.Prepare(true, false, false, 30, 10000)
            && TacticalFieldPolicy.Prepare(false, true, false, 30, 10000)
            && TacticalFieldPolicy.Prepare(false, false, true, 30, 10000), "Defense, support and incoming fire retain early responses.");
        Check(AccessTools.Method(typeof(Pawn), nameof(Pawn.Notify_UsedVerb), new[] { typeof(Pawn), typeof(Verb) }) != null,
            "Actual successful-shot notifications must bind the installed native game signature.");
        var service = new MapComponent_TacticalCommands(null);
        var carrier = new TacticalMemberCommand { Pawn = new Pawn { thingIDNumber = 21 }, FieldPostState = TacticalFieldPostState.Fixed,
            FieldHadCover = true, FieldPost = IntVec3.Invalid };
        Thing cover = (Thing)RuntimeHelpers.GetUninitializedObject(typeof(Thing));
        var mapIndex = AccessTools.Field(typeof(Thing), "mapIndexOrState");
        mapIndex.SetValue(cover, (sbyte)0); carrier.FieldCover = cover;
        var refresh = AccessTools.Method(service.GetType(), "RefreshFieldPost");
        var field = new TacticalFieldResponse { Focus = IntVec3.North * 30 };
        refresh.Invoke(service, new object[] { carrier, field, 100 });
        Check(carrier.FieldPostState == TacticalFieldPostState.Fixed, "Intact recorded cover keeps its post through periodic review.");
        field.Focus = IntVec3.East * 30;
        refresh.Invoke(service, new object[] { carrier, field, 300 });
        Check(carrier.FieldPostState == TacticalFieldPostState.Fixed, "A target/direction change must not unlock the anvil position.");
        mapIndex.SetValue(cover, (sbyte)-2);
        refresh.Invoke(service, new object[] { carrier, field, 500 });
        Check(carrier.FieldPostState == TacticalFieldPostState.Unplaced && carrier.FieldCover == null,
            "Destroying the actual cover must request a fresh placement for its user.");

        var rejected = new TacticalMemberCommand { FieldPostState = TacticalFieldPostState.Moving,
            FieldPost = new IntVec3(1,0,1), FieldHadCover = true };
        var reject = AccessTools.Method(typeof(MapComponent_TacticalCommands), "RejectFieldPost");
        reject.Invoke(null, new object[] { rejected, 100 });
        Check(rejected.FieldPostState == TacticalFieldPostState.Uncovered && !rejected.FieldPost.IsValid
            && !rejected.FieldHadCover && rejected.FieldDecisionAt == 190, "Failed placement is not fixed or a compulsory return destination.");
        reject.Invoke(null, new object[] { rejected, 200 });
        Check(rejected.FieldDecisionAt == 1100 && rejected.FieldPlacementFailures == 2,
            "Repeated failed placement backs off instead of scanning every decision slice.");
        var fixedJob = AccessTools.Method(typeof(MapComponent_TacticalCommands), "FixedFieldPostJob");
        Check(fixedJob.Invoke(service, new object[] { rejected, new TacticalSquadCommand(), 200 }) == null,
            "Uncovered placement cannot acquire a permanent fixed-position wait.");
        Console.WriteLine("PASS: " + checks + " field feedback, contact grace and attack approach checks.");
    }
}
