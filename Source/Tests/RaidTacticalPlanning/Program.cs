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
