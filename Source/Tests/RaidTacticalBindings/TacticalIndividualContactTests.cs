using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace.Tactics;
using Verse;
using Verse.AI;

internal static class TacticalIndividualContactTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception(reason); checks++; }
        IntVec3 opening = new IntVec3(20, 0, 20);
        var command = new TacticalSquadCommand { Phase = TacticalCommandPhase.Stack,
            Plan = new TacticalLocalPlan { Opening = opening, Inward = IntVec3.East, EntryLane = opening + IntVec3.East * 2 } };
        Check(TacticalPostFirePolicy.IndividualContact(command), "An attacking stack must retain individual contact execution.");
        command.Defensive = true;
        Check(!TacticalPostFirePolicy.IndividualContact(command), "Defensive coordinated reactions must remain available.");
        command.Defensive = false; command.Plan.Direct = true;
        Check(!TacticalPostFirePolicy.IndividualContact(command) && TacticalPostFirePolicy.IndividualContact(command, true),
            "Indoor direct transit reacts individually without disabling ordinary field responses.");
        command.Plan.Direct = false;
        foreach (var phase in new[] { TacticalCommandPhase.Stack, TacticalCommandPhase.Breach, TacticalCommandPhase.Observe,
            TacticalCommandPhase.Support, TacticalCommandPhase.Enter, TacticalCommandPhase.Clear })
        {
            command.Phase = phase;
            Check(TacticalPostFirePolicy.SafeToFire(command), "A normal CQB watch must be able to defend itself in " + phase);
        }
        command.Phase = TacticalCommandPhase.BlastWait;
        Check(!TacticalPostFirePolicy.SafeToFire(command), "Blast safety must still own movement and weapons.");
        command.Phase = TacticalCommandPhase.Stack; command.ChargeAction = new TacticalChargeAction();
        Check(!TacticalPostFirePolicy.SafeToFire(command), "C4 placement, retreat and detonation cannot be interrupted by watch fire.");
        command.ChargeAction = null; command.OpeningAction = new TacticalOpeningAction { Launched = true };
        Check(!TacticalPostFirePolicy.SafeToFire(command), "A launched grenade must retain its safety controller.");
        command.OpeningAction.EffectsCleared = true;
        Check(TacticalPostFirePolicy.SafeToFire(command), "Finished grenade effects must release watch fire.");
        foreach (IntVec3 mouth in new[] { command.Plan.Opening, command.Plan.Outside, command.Plan.Inside, command.Plan.EntryLane })
            Check(!TacticalPostFirePolicy.CanStop(command.Plan, mouth), "A shooter cannot stop in the mandatory ingress mouth/lane.");
        Check(TacticalPostFirePolicy.CanStop(command.Plan, opening + IntVec3.East * 3), "An entrant beyond the lane may stop to fight.");
        for (int id = 0; id < 13; id++)
            Check(TacticalPostFirePolicy.CheckDelay(true, id) >= 15 && TacticalPostFirePolicy.CheckDelay(true, id) <= 21
                && TacticalPostFirePolicy.CheckDelay(false, id) <= 5, "Watch checks remain staggered and denied work retries promptly.");

        // Execute the controller branch with real command/job ownership. A
        // discovered contact must neither stop nor replace a follower's job.
        var owner = new MapComponent_TacticalCommands(null);
        var byPawn = new Dictionary<Pawn, TacticalSquadCommand>(ReferenceEqualityComparer.Instance);
        AccessTools.Field(owner.GetType(), "byPawn").SetValue(owner, byPawn);
        var definition = (JobDef)RuntimeHelpers.GetUninitializedObject(typeof(JobDef)); definition.defName = "HD_NewTacticalPost";
        var members = new List<TacticalMemberCommand>();
        for (int i = 0; i < 2; i++)
        {
            var pawn = new Pawn();
            pawn.jobs = (Pawn_JobTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_JobTracker));
            var job = new Job { def = definition, targetA = opening + IntVec3.North * (i + 1), targetB = opening };
            AccessTools.Field(typeof(Pawn_JobTracker), "curJob").SetValue(pawn.jobs, job);
            pawn.jobs.curDriver = new JobDriver_TacticalPost { pawn = pawn, job = job };
            var member = new TacticalMemberCommand { Pawn = pawn, Job = job };
            members.Add(member); command.Members.Add(member); byPawn[pawn] = command;
        }
        command.OpeningAction = null;
        command.ContactResponse = new TacticalContactResponse { Rear = true, DirectAttack = true, Started = 50 };
        IntVec3 obsolete = opening + IntVec3.South * 5;
        command.ContactResponse.Occupied.Add(obsolete);
        var claims = (Dictionary<IntVec3, TacticalSquadCommand>)AccessTools.Field(owner.GetType(), "claims").GetValue(owner);
        claims[obsolete] = command; claims[command.Plan.Outside] = command;
        var plan = command.Plan; var firstJob = members[0].Job; var followerJob = members[1].Job;
        command.Contacts.Memory.Remember(7, opening + IntVec3.North * 4, opening, false, true, 100);
        var respond = AccessTools.Method(owner.GetType(), "RespondToContacts");
        Check(!(bool)respond.Invoke(owner, new object[] { command, members, 100 }), "CQB contact must not consume the squad scheduler.");
        Check(command.ContactResponse == null && !command.ContactRestoring && !claims.ContainsKey(obsolete)
            && claims[plan.Outside] == command, "Only obsolete whole-squad reaction claims are released.");
        Check(command.Plan == plan && command.Phase == TacticalCommandPhase.Stack && command.Contacts.Memory.Entries.Count == 1
            && members[0].Job == firstJob && members[1].Job == followerJob && members[1].Pawn.CurJob == followerJob,
            "The observed contact, destination and unseen follower's live movement job must survive.");
        command.ContactResponse = new TacticalContactResponse { Rear = true };
        Check(!(bool)AccessTools.Method(owner.GetType(), "StackMovementPaused").Invoke(owner,
            new object[] { members[1].Pawn, followerJob }), "An unseen follower must be admitted despite the shooter's contact.");
        command.ContactResponse = null;
        var root = new TacticalSquadCommand(); root.CqbTeams.Add(command);
        Check(!(bool)respond.Invoke(owner, new object[] { root, members, 110 }), "A HIGH root cannot pause its advancing CQB children.");

        // Inspect the actual toil graph, without a Unity map/pathfinder. This
        // catches accidentally opting observation or blast jobs into gunfire.
        var post = (JobDriver_TacticalPost)members[1].Pawn.jobs.curDriver;
        var toils = ((IEnumerable<Toil>)AccessTools.Method(post.GetType(), "MakeNewToils").Invoke(post, null)).ToList();
        Check(toils.Count == 7 && toils[1].preTickActions?.Count == 1 && toils[3].tickAction != null
            && toils.Last().tickAction != null && !post.job.canUseRangedWeapon,
            "Stack travel and holding must install bounded firing, with a same-job contact/resume branch.");
        Check(!post.AtPost, "A local movement contact cannot claim that the stack destination was reached.");
        var hold = AccessTools.Method(typeof(TacticalJobDriver), "Hold");
        post.job.def = (JobDef)RuntimeHelpers.GetUninitializedObject(typeof(JobDef)); post.job.def.defName = "HD_NewTacticalObserve";
        Check(((Toil)hold.Invoke(post, new object[] { opening, false })).tickAction == null,
            "Observation/retraction holds cannot shoot instead of completing their grenade action.");
        Console.WriteLine("PASS: " + checks + " individual CQB watch/transport, follower continuity, HIGH controller, toil graph and explosive ownership checks.");
    }
}
