using System;
using System.Linq;
using Helodrace;

internal static class Program
{
    private static int checks;

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }

    private static int Main()
    {
        try
        {
            Check(RaidSmokePolicy.CarrierCount(0) == 0, "An empty formation does not create smoke carriers");
            Check(RaidSmokePolicy.CarrierCount(1) == 1, "A one-person formation still has smoke");
            Check(RaidSmokePolicy.CarrierCount(6) == 2, "A six-person formation guarantees two smoke carriers");
            Check(RaidSmokePolicy.CarrierCount(7) == 3, "Incomplete thirds still receive a smoke carrier");
            Check(RaidSmokePolicy.GrenadesPerCarrier >= 3, "A carrier has smoke for approach and external entry");
            var strip = Enumerable.Range(-4, 9)
                .SelectMany(x => new[] { (x, 1), (x, 2) }).ToList();
            static (int x, int z)[] Neighbors((int x, int z) cell) => new[] {
                (cell.x - 1, cell.z), (cell.x + 1, cell.z),
                (cell.x, cell.z - 1), (cell.x, cell.z + 1) };
            var connected = RaidFormationTopology.Connected(strip, (0, 1), Neighbors, _ => true);
            Check(connected.Count == 18, "Both flanks connect through the outside approach strip");
            connected = RaidFormationTopology.Connected(strip.Where(cell => cell.Item1 != 2),
                (0, 1), Neighbors, _ => true);
            Check(!connected.Contains((3, 1)), "A dividing wall excludes the far side despite global reachability");
            Check(connected.Contains((-4, 2)), "A connected second rank remains available");
            connected = RaidFormationTopology.Connected(strip, (0, 1), Neighbors, cell => cell.Item1 <= 1);
            Check(!connected.Contains((2, 2)), "A corner hidden from the entry cannot host the formation");
            connected = RaidFormationTopology.Connected(strip.Where(cell => cell != (0, 1)),
                (0, 1), Neighbors, _ => true);
            Check(connected.Count == 0, "A blocked anchor rejects the formation instead of choosing another pocket");
            Check(!RaidOrderPolicy.Refresh(false, true, false, false),
                "Repeating an unchanged directive keeps the current controlled job");
            Check(RaidOrderPolicy.Refresh(true, true, false, false),
                "A changed destination is applied even when the old job is controlled");
            Check(!RaidOrderPolicy.Refresh(true, true, false, true),
                "A changed directive waits for aiming or burst cooldown");
            Check(!RaidOrderPolicy.Refresh(true, false, true, false),
                "A tactical directive cannot recall a pawn during explosion evasion or an equipment action");
            Check(RaidOrderPolicy.Refresh(true, true, false, false),
                "A pending directive is applied after the busy stance ends");
            Check(RaidOrderPolicy.Refresh(false, false, false, false),
                "An interrupted directive resumes after the emergency job finishes");
            Check(RaidOrderPolicy.ContinueMove(true, true),
                "The same destination preserves path progress");
            Check(!RaidOrderPolicy.ContinueMove(false, true),
                "A new destination does not inherit vanilla's unconditional Goto continuation");
            Check(!RaidOrderPolicy.ContinueMove(true, false),
                "A required sprint change is not lost as a continuation");
            Check(RaidBreachTraversal.IsClearance(1, false),
                "A lateral standing cell in a shallow room can clear the doorway");
            Check(!RaidBreachTraversal.IsClearance(1, true),
                "The inside mouth itself cannot be reserved as a final standing cell");
            Check(RaidBreachTraversal.IsClearance(1, true, true),
                "A one-cell closet admits one pawn while the rest remain outside");
            Check(!RaidBreachTraversal.IsClearance(0, false),
                "An outside or wall-plane cell cannot complete entry");
            Check(RaidBreachTraversal.AdmissionLimit(8, 3) == 3,
                "A three-cell room keeps excess entrants outside as reserve security");
            Check(RaidBreachTraversal.AdmissionLimit(4, 12) == 4,
                "A large room retains the full entry team");
            Check(RaidBreachTraversal.AdmissionLimit(8, 0) == 0,
                "A blocked inside mouth does not release any entrants");
            Check(RaidBreachTraversal.PlacementDepthAllowed(3, false),
                "An uncovered entrant can stand within three cells of the breached wall");
            Check(!RaidBreachTraversal.PlacementDepthAllowed(4, false),
                "An empty room does not send an entrant four cells forward");
            Check(RaidBreachTraversal.PlacementDepthAllowed(6, true),
                "Actual forward cover can justify a somewhat deeper entry position");
            Check(!RaidBreachTraversal.PlacementDepthAllowed(7, true),
                "Cover does not justify running far into the room on entry");
            Check(RaidBreachTraversal.PlacementScore(1, 1, -1, -1, 0)
                < RaidBreachTraversal.PlacementScore(3, 3, -1, -1, 0),
                "Without cover, the entry-side wall wins over a deeper matching slot");
            Check(RaidBreachTraversal.PlacementScore(5, 4, -1, -1, 0.5f)
                < RaidBreachTraversal.PlacementScore(1, 1, -1, -1, 0),
                "Real forward cover wins over an exposed wall-side position");
            Check(RaidBreachTraversal.PlacementScore(1, 1, 1, 1, 0)
                < RaidBreachTraversal.PlacementScore(2, 4, 0, 1, 0),
                "A lateral wall-side slot keeps the middle of the entry lane clear");
            Check(RaidBreachTraversal.CanUsePortal(true, true, false),
                "The selected demolished wall remains passable in the frozen structure snapshot");
            Check(!RaidBreachTraversal.CanUsePortal(false, true, false),
                "Simultaneous entrants cannot take another cached door or demolished wall");
            Check(!RaidBreachTraversal.CanUsePortal(false, false, true),
                "Another open exterior entrance cannot bypass the selected breach");
            Check(RaidBreachTraversal.CanUsePortal(false, false, false),
                "Ordinary floor cells remain available without exact route checkpoints");
            Check(!RaidOrderPolicy.ReadyToEnter(true, true, true),
                "Finishing the throw job does not permit entry while the grenade remains live");
            Check(!RaidOrderPolicy.ReadyToEnter(false, false, true),
                "An elapsed timer cannot override incomplete stack-up");
            Check(!RaidOrderPolicy.ReadyToEnter(true, false, false),
                "Effect completion still respects the coordinated entry delay");
            Check(RaidOrderPolicy.ReadyToEnter(true, false, true),
                "A prepared squad enters after the support effect and delay");
            Check(RaidOrderPolicy.SupportPending(false, true, false, true),
                "A throw that outlives the support timeout still blocks entry before launch");
            Check(RaidOrderPolicy.SupportPending(true, false, false, true),
                "A live grenade blocks entry even after all entry timers elapsed");
            Check(RaidOrderPolicy.SupportPending(false, false, true, true),
                "An active explosion blocks entry after its projectile has been destroyed");
            Check(RaidOrderPolicy.SupportPending(false, false, false, false),
                "The squad waits through the effect settling interval");
            Check(!RaidOrderPolicy.SupportPending(false, false, false, true),
                "The squad is released once the throw, grenade and explosion have finished");
            Check(!RaidOrderPolicy.AutoAttack(true, true, false),
                "A deferred movement cannot be starved by repeatedly starting another automatic burst");
            Check(RaidOrderPolicy.AutoAttack(true, true, true),
                "Updating a hold directive does not suppress stationary defensive shooting");
            Check(RaidOrderPolicy.AutoAttack(false, true, false),
                "Tactical pending directives do not suppress unrelated vanilla combat");
            var progress = RaidBreachProgress.Approach;
            progress = RaidBreachTraversal.Advance(progress, false, false, false);
            Check(progress == RaidBreachProgress.Approach,
                "An outside waiting pawn is not admitted before reaching the entrance");
            progress = RaidBreachTraversal.Advance(progress, true, false, false);
            Check(progress == RaidBreachProgress.Crossing,
                "Arrival at the outside entrance releases a pawn toward the inside");
            progress = RaidBreachTraversal.Advance(progress, false, false, false);
            Check(progress == RaidBreachProgress.Crossing,
                "A diagonal step away from the exact entrance cannot reverse the crossing");
            progress = RaidBreachTraversal.Advance(progress, false, true, false);
            Check(progress == RaidBreachProgress.Clearing,
                "Passing the opening continues to the interior destination instead of stopping in the mouth");
            progress = RaidBreachTraversal.Advance(progress, true, false, false);
            Check(progress == RaidBreachProgress.Clearing,
                "Knockback from the inside does not send a pawn to outside assembly");
            progress = RaidBreachTraversal.Advance(progress, false, true, true);
            Check(progress == RaidBreachProgress.Complete,
                "Crossing completes only after reaching an interior clearance position");
            Check(RaidBreachTraversal.Advance(progress, false, false, false)
                == RaidBreachProgress.Complete, "Completed entry remains latched after displacement");
            Check(RaidBreachTraversal.Advance(RaidBreachProgress.Approach,
                false, true, false) == RaidBreachProgress.Clearing,
                "A pawn already inside when the crossing resumes continues inward");
            Check(RaidBreachTraversal.Advance(RaidBreachProgress.Crossing,
                false, false, true) == RaidBreachProgress.Crossing,
                "Proximity to a destination cannot count as entry while still outside");

            var openField = new RaidTacticalSituation { DirectThreat = 5f };
            var openOptions = RaidTacticalDecision.Rank(openField);
            Check(openOptions.Count == 3, "Three viable baseline options remain without special equipment");
            Check(openOptions[0].Maneuver == RaidTacticalManeuver.DirectAssault,
                "Open low-threat ground permits a direct assault");

            var threatened = new RaidTacticalSituation
            {
                DirectThreat = 80f, FlankThreat = 8f, FlankAvailable = true,
                SmokeAvailable = true, CasualtyFraction = 0.05f
            };
            var options = RaidTacticalDecision.Rank(threatened);
            Check(options.Count == 3, "Three ranked maneuvers are retained");
            Check(options[0].Maneuver == RaidTacticalManeuver.FlankAttack,
                "A safer flank outranks a defended frontal approach");
            Check(options.All(option => option.Score >= options.Last().Score),
                "Ranked options are sorted by score");
            Check(RaidTacticalDecision.Select(options, 1f) == options[0],
                "Full command efficiency selects the best plan");
            Check(RaidTacticalDecision.Select(options, 0.7f) == options[1],
                "An acting commander's reduced efficiency selects the second plan");
            Check(RaidTacticalDecision.Select(options, 0.3f) == options[2],
                "Severely disrupted command selects the third plan");

            var smokeOnly = new RaidTacticalSituation { DirectThreat = 80f, SmokeAvailable = true };
            Check(RaidTacticalDecision.Rank(smokeOnly)[0].Maneuver
                == RaidTacticalManeuver.SmokeAdvance,
                "Available smoke mitigates an exposed field approach");

            var fieldGrenade = new RaidTacticalSituation
            {
                DirectThreat = 25f, FieldGrenadeAvailable = true
            };
            Check(RaidTacticalDecision.Rank(fieldGrenade)[0].Maneuver
                == RaidTacticalManeuver.FieldGrenade,
                "A nearby field target allows inventory grenade support");

            var defense = new RaidTacticalSituation
            {
                Defending = true, DirectThreat = 60f, CasualtyFraction = 0.3f
            };
            Check(RaidTacticalDecision.Rank(defense)[0].Maneuver
                == RaidTacticalManeuver.HoldAndCounterattack,
                "Defensive posture can reserve a response group");

            var occupied = new RaidTacticalSituation
            {
                Doctrine = RaidTacticalDoctrine.Low, IndoorObjective = true,
                EntryAvailable = true, LethalGrenadeAvailable = true,
                FriendlyInsideObjective = true, DirectThreat = 20f
            };
            float occupiedScore = RaidTacticalDecision.Rank(occupied)
                .Single(option => option.Maneuver == RaidTacticalManeuver.CoordinatedEntry).Score;
            occupied.FriendlyInsideObjective = false;
            float emptyScore = RaidTacticalDecision.Rank(occupied)
                .Single(option => option.Maneuver == RaidTacticalManeuver.CoordinatedEntry).Score;
            Check(emptyScore > occupiedScore,
                "Friendly occupants suppress the low-doctrine lethal grenade bonus");
            occupied.BreachToolAvailable = true;
            float equippedScore = RaidTacticalDecision.Rank(occupied)
                .Single(option => option.Maneuver == RaidTacticalManeuver.CoordinatedEntry).Score;
            Check(equippedScore > emptyScore,
                "Actual breach equipment raises the coordinated entry rating");
            occupied.FriendlyInsideObjective = true;
            Check(RaidTacticalDecision.EntrySupport(RaidTacticalManeuver.CoordinatedEntry, occupied)
                == "No lethal grenade: friendly inside",
                "Low doctrine will not grenade a room containing its own members");
            occupied.Doctrine = RaidTacticalDoctrine.High;
            occupied.NonlethalGrenadeAvailable = true;
            Check(RaidTacticalDecision.EntrySupport(RaidTacticalManeuver.CoordinatedEntry, occupied)
                == "Nonlethal grenade after identification",
                "High doctrine prefers available nonlethal entry support");
            occupied.NonlethalGrenadeAvailable = false;
            Check(RaidTacticalDecision.EntrySupport(RaidTacticalManeuver.CoordinatedEntry, occupied)
                == "Hold grenades until occupants are identified",
                "High doctrine does not substitute a lethal grenade for missing nonlethal support");

            Console.WriteLine($"PASS: {checks} tactical decision and breach traversal assertions.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }
}
