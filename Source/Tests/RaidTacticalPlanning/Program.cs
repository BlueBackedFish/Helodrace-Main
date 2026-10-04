using System;
using System.Linq;
using System.Collections.Generic;
using Helodrace;

internal static class Program
{
    private static int checks;

    private static void Check(bool condition, string message)
    {
        checks++;
        if (!condition) throw new Exception(message);
    }

    private static void CheckFormationSlots()
    {
        var slots = new RaidFormationSlots<int>();
        slots.Claim(4, 12); slots.Claim(4, 8); slots.Claim(5, 20);
        Check(slots.Available(4, 8) && !slots.Available(4, 12), "Duplicate slots have one stable owner across squads.");
        slots.Release(4, 12);
        Check(!slots.Available(4, 12), "A losing claimant cannot remove another pawn's slot.");
        Check(!RaidFormationSlots<int>.Ready(3, 4, false), "A neighboring safe cell is not the assigned formation slot.");
        Check(!RaidFormationSlots<int>.Ready(4, 4, true), "An overlapping arrival cannot count as ready.");
        Check(RaidFormationSlots<int>.Ready(4, 4, false), "An unoccupied assigned slot is ready.");
        int searches = 0;
        Check(slots.TryAssign(new[] { 4, 5, 6, 7, 8 }, 12, cell => cell != 6,
            cell => { searches++; return cell != 7; }, out int assigned) && assigned == 8 && searches == 2,
            "Replacement excludes other claims and occupied tiles before checking reachability.");
        Check(!slots.Available(assigned, 30), "The replacement is immediately claimed before selecting the next member.");
        searches = 0;
        Check(!slots.TryAssign(Enumerable.Range(100, 100), 40, _ => true,
            _ => { searches++; return false; }, out _) && searches == 32,
            "Unreachable formation candidates have a bounded pathfinding budget.");
        Check(!slots.TryAssign(new[] { 4, 5, 8 }, 30, _ => true, _ => true, out _),
            "Insufficient safe cells never reuse an occupied or claimed formation slot.");
        slots.Release(8, 12);
        Check(slots.Available(8, 30) && !slots.Available(4, 12), "Releasing a slot does not discard unrelated owners.");
    }

    private static void CheckCommunications()
    {
        var voice = RaidCommunicationPolicy.Delays(new[] { 1, 2, 3, 4 }, 1,
            (a, b) => Math.Abs(a - b) == 1 && b != 4 && a != 4 ? 40 : -1);
        Check(voice[1] == 0 && voice[2] == 40 && voice[3] == 80 && !voice.ContainsKey(4),
            "LOW oral reports require each contact link; isolated soldiers remain unknown to command.");
        var radio = RaidCommunicationPolicy.Delays(new[] { 1, 2, 3 }, 1,
            (a, b) => a + b == 4 ? 20 : Math.Abs(a - b) == 1 ? 40 : -1);
        Check(radio[3] == 20 && radio[2] == 40, "A usable radio link can bypass distant voice relays.");
        Check(RaidCommunicationPolicy.Delays(new[] { 1, 2 }, 7, (_, _) => 20).Count == 0,
            "Absent command does not silently connect all soldiers.");
        Check(RaidCommunicationPolicy.RadioCompatible(true, true, true, true, false, "net", "net", 90000, 300),
            "Compatible active equipment connects at the configured range boundary without a LOS input.");
        Check(!RaidCommunicationPolicy.RadioCompatible(true, true, true, true, true, "net", "net", 10, 300),
            "Radio blackout disables equipment connectivity.");
        Check(!RaidCommunicationPolicy.RadioCompatible(true, true, false, true, false, "net", "net", 10, 300),
            "Downed/unavailable equipment operator cannot transmit.");
        Check(!RaidCommunicationPolicy.RadioCompatible(true, true, true, true, false, "net", "other", 10, 300),
            "Different radio networks do not create a shared knowledge pool.");
        Check(!RaidCommunicationPolicy.RadioCompatible(false, true, true, true, false, "net", "net", 10, 300),
            "LOW doctrine does not gain automatic distant sharing from faction identity or a support radio.");
        Check(!RaidCommunicationPolicy.RadioCompatible(true, true, true, true, false, "net", "net", 90001, 300),
            "Equipment range is a real connectivity constraint.");
        Check(RaidCommunicationPolicy.Fresh(100, 1299) && !RaidCommunicationPolicy.Fresh(100, 1300)
            && !RaidCommunicationPolicy.Fresh(100, 99), "Memory age uses the original non-future observation timestamp.");
        Check(RaidCommunicationPolicy.CanRelay(new[] { "A", "B" }, "B", true)
            && !RaidCommunicationPolicy.CanRelay(new[] { "A", "B" }, "A", false),
            "Receiving soldier-to-command forwarding preserves the route while external cycles are forbidden.");
        Check(!RaidCommunicationPolicy.CanRelay(Enumerable.Range(0, 8).Select(i => i.ToString()).ToList(), "next", false),
            "Relay paths have a finite hop limit.");
    }

    private static int Main()
    {
        try
        {
            CheckFormationSlots();
            CheckCommunications();
            CheckLocalCqb();
            var sideClearance = RaidFormationTopology.Connected(new[] { 3, 4 }, 3,
                cell => new[] { cell - 3, cell + 3, cell - 1, cell + 1 }, _ => true);
            Check(sideClearance.Contains(4) && RaidBreachTraversal.IsClearance(1, false),
                "The clearance beside a mouth is connected by cardinal movement despite a diagonal wall shoulder");
            Check(!RaidBreachTraversal.IsClearance(1, true),
                "An ordinary joining goal cannot end in the entrance mouth");
            Check(RaidOrderPolicy.MovementReservationAllowed(true, false),
                "A through-opening waypoint is not an exclusive arrival slot serializing the followers");
            Check(!RaidOrderPolicy.MovementReservationAllowed(false, false),
                "Interior clearance and final formation still respect destination reservations");
            Check(RaidOrderPolicy.MovementReservationAllowed(false, true),
                "A free ordinary destination starts movement normally");
            Check(RaidBreachTraversal.AllowsCommittedIngressStep(true, false, 0, 9, 1, true, true),
                "The committed opening remains usable even with its own doorway room ID");
            Check(!RaidBreachTraversal.AllowsCommittedIngressStep(false, false, 0, 1, 1, false, false),
                "An unexpected gap cannot bypass the established opening");
            Check(!RaidBreachTraversal.AllowsCommittedIngressStep(false, false, 0, 0, 1, true, true),
                "An old exterior door cannot replace the established opening");
            Check(RaidBreachTraversal.AllowsCommittedIngressStep(false, false, 0, 0, 1, false, false),
                "Ordinary exterior detours remain usable while approaching the opening");
            Check(RaidBreachTraversal.AllowsCommittedIngressStep(false, true, 0, 1, 1, false, false),
                "A member touching the selected opening can continue inward");
            Check(!RaidBreachTraversal.AllowsCommittedIngressStep(false, true, 1, 2, 1, false, false),
                "Joining cannot stray into an unrelated room");
            CheckCqbIntent();
            Check(RaidSmokePolicy.CarrierCount(0) == 0, "An empty formation does not create smoke carriers");
            Check(RaidSmokePolicy.CarrierCount(1) == 1, "A one-person formation still has smoke");
            Check(RaidSmokePolicy.CarrierCount(6) == 2, "A six-person formation guarantees two smoke carriers");
            Check(RaidSmokePolicy.CarrierCount(7) == 3, "Incomplete thirds still receive a smoke carrier");
            Check(RaidSmokePolicy.GrenadesPerCarrier >= 3, "A carrier has smoke for approach and external entry");
            Check(RaidEntryObservationPolicy.Support(true, 0) == RaidEntrySupportKind.Smoke,
                "Outdoor space beyond the opening requires smoke");
            Check(RaidEntryObservationPolicy.Support(false, 17) == RaidEntrySupportKind.Grenade,
                "A 17-cell indoor room permits grenade support, including exterior-to-interior entry");
            Check(RaidEntryObservationPolicy.Support(false, 16) == RaidEntrySupportKind.None,
                "Exactly 16 floor cells must save the grenade");
            Check(RaidEntryObservationPolicy.Support(false, 1) == RaidEntrySupportKind.None,
                "A tiny room does not spend a grenade");
            Check(RaidEntryObservationPolicy.Support(false, 0) == RaidEntrySupportKind.None,
                "Unknown indoor area must not be guessed large enough for grenade use");
            Check(RaidEntryObservationPolicy.Support(true, 16) == RaidEntrySupportKind.Smoke,
                "The small-room threshold must not suppress an outdoor smoke screen");
            Check(RaidEntryObservationPolicy.Support(false, 16, true) == RaidEntrySupportKind.Grenade,
                "An actual enemy sighting overrides the 16-cell grenade conservation threshold");
            Check(RaidEntryObservationPolicy.Support(false, 1, true) == RaidEntrySupportKind.Grenade,
                "Even a one-cell room permits a safe throw at an observed enemy");
            Check(RaidEntryObservationPolicy.Support(true, 0, true) == RaidEntrySupportKind.Smoke,
                "Outdoor contact still uses the established safe smoke type at the observed position");
            Check(RaidEntryObservationPolicy.ObservationTicks >= 60 && RaidEntryObservationPolicy.ObservationTicks <= 120,
                "Observation itself lasts between one and two seconds");
            Check(RaidSmokePolicy.NeedsScreen(true, 50, 40, false),
                "A visible ranged defender covering the approach triggers smoke before the first hit");
            Check(!RaidSmokePolicy.NeedsScreen(false, 50, 40, false),
                "Unobserved defenders do not trigger smoke using hidden map knowledge");
            Check(!RaidSmokePolicy.NeedsScreen(true, 15, 10, false),
                "A short-range weapon does not trigger the long-range approach screen");
            Check(!RaidSmokePolicy.NeedsScreen(true, 30, 40, false),
                "A defender unable to reach the advancing pawn does not consume smoke");
            Check(!RaidSmokePolicy.NeedsScreen(true, 50, 40, true),
                "An existing smoke screen avoids repeated smoke expenditure on the same sightline");
            Check(!RaidSmokePolicy.ScreenComplete(true, true, false, true, true, true),
                "A live screening grenade is not abandoned on a timeout");
            Check(!RaidSmokePolicy.ScreenComplete(false, false, true, false, false, true),
                "A throw still in preparation keeps the team providing covering fire");
            Check(RaidSmokePolicy.ScreenComplete(true, false, false, true, false, false),
                "The advancing team resumes when the smoke screen forms");
            Check(RaidSmokePolicy.ScreenComplete(false, false, false, false, false, true),
                "A failed smoke action does not permanently stall the approach");
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
            var detour = RaidFormationTopology.Distances(new[] {
                (0, 0), (0, 1), (0, 2), (1, 2), (2, 2), (2, 1), (2, 0), (5, 5)
            }, (0, 0), Neighbors, _ => true);
            Check(detour[(2, 0)] == 6, "Cover beyond a wall is scored by the six-step detour rather than two-cell direct distance");
            Check(detour[(0, 1)] == 1, "Nearby connected cover retains low travel cost");
            Check(!detour.ContainsKey((5, 5)), "A separate cover pocket cannot become a local reaction destination");
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
            int defenseUntil = 600;
            Check(RaidReactivePolicy.DefenseActive(10, ref defenseUntil, false, false), "Support event starts a preparation window before an enemy is visible");
            Check(RaidReactivePolicy.DefenseActive(700, ref defenseUntil, true, false) && defenseUntil == 1000,
                "Delayed support holds defense until after delivery");
            Check(RaidReactivePolicy.DefenseActive(1100, ref defenseUntil, false, true) && defenseUntil == 1400,
                "An observed enemy approaching after the strike keeps the defensive formation");
            Check(!RaidReactivePolicy.DefenseActive(1400, ref defenseUntil, false, false),
                "A quiet battlefield eventually resumes the committed breach plan");
            Console.WriteLine("PASS: 4 support defense timing assertions.");
            Check(RaidReactivePolicy.Outranged(true, true, false, 45f, 25f, 35f), "Observed enemy aiming outside own range requires immediate cover");
            Check(!RaidReactivePolicy.Outranged(false, true, false, 45f, 25f, 35f), "An unobserved sniper must not reveal hidden enemy information");
            Check(!RaidReactivePolicy.Outranged(true, false, false, 45f, 25f, 35f), "An idle distant rifle does not interrupt a stack");
            Check(!RaidReactivePolicy.Outranged(true, true, true, 45f, 25f, 35f), "Smoke shielding the sightline permits protected movement");
            Check(!RaidReactivePolicy.Outranged(true, true, false, 45f, 25f, 50f), "A shooter outside its own effective range is not a sniper threat");
            Check(!RaidReactivePolicy.Outranged(true, true, false, 45f, 25f, 20f), "When the pawn can return fire it retains ordinary combat selection");
            Console.WriteLine("PASS: 6 sniper observation and range assertions.");
            float smokeAnchor = RaidSmokePlanning.DenseAnchor(new[] { 0f, 1f, 2f, 3f, 4f, -30f }, (a, b) => Math.Abs(a - b));
            Check(smokeAnchor >= 0 && smokeAnchor <= 4, "A rear carrier thirty cells away must not drag the smoke plan out of the squad core");
            float relocatedCarrierAnchor = RaidSmokePlanning.DenseAnchor(new[] { 0f, 1f, 2f, 3f, 4f, -50f }, (a, b) => Math.Abs(a - b));
            Check(smokeAnchor == relocatedCarrierAnchor, "Further carrier retreat leaves the planned squad anchor unchanged");
            var bentRoute = new[] { (0, 0), (1, 0), (2, 0), (2, 1), (2, 2), (2, 3) };
            float RouteDistance((int x, int z) a, (int x, int z) b) => Math.Abs(a.x - b.x) + Math.Abs(a.z - b.z);
            Check(RaidSmokePlanning.ForwardAlong(bentRoute, (0, 0), 4f, RouteDistance) == (2, 2),
                "A common smoke waypoint follows the bent approach route instead of a carrier's forward vector");
            Check(RaidSmokePlanning.ForwardAlong(bentRoute, (2, 1), 2f, RouteDistance) == (2, 3),
                "The next smoke step advances from the squad's current route progress");
            Check(RaidSmokePlanning.ForwardAlong(bentRoute, (2, 2), 8f, RouteDistance) == (2, 3),
                "A short remaining route clamps the planned smoke at its end");
            Console.WriteLine("PASS: 5 common squad smoke planning assertions.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static void CheckLocalCqb()
    {
        int before = checks;
        int width = 7, height = 3;
        int[] rooms = Enumerable.Range(0, width * height).Select(i => i % width < 3 ? 1 : i % width > 3 ? 2 : 0).ToArray();
        bool[] usable = rooms.Select(room => room > 0).ToArray();
        bool[] portals = rooms.Select(room => room == 0).ToArray();
        var closed = new CqbLocalTopology(width, height, rooms, usable, portals);
        Check(closed.Path(8, 12).Count == 0, "An intact partition is not an existing passage.");
        Check(closed.NeighborTargets(8, new HashSet<int> { 1 }).Any(i => rooms[i] == 2),
            "An adjoining closed room remains a local breach candidate.");
        Check(!closed.NeighborTargets(8, new HashSet<int> { 1, 2 }).Any(),
            "Cleared rooms are excluded even across a closed partition.");
        bool[] openedCells = (bool[])usable.Clone(); openedCells[10] = true;
        var opened = new CqbLocalTopology(width, height, rooms, openedCells, portals);
        Check(opened.Path(8, 12).SequenceEqual(new[] { 8, 9, 10, 11, 12 }),
            "An unexpected wall hole creates a local walking passage without demolition.");
        Check(opened.Rooms[8] == 1 && opened.Rooms[12] == 2,
            "Opening a wall does not merge room progress identities.");
        Check(opened.NeighborTargets(8, new HashSet<int> { 1 }).Any(i => rooms[i] == 2),
            "A reachable adjoining room is still uncleared until it is secured.");
        Check(!opened.NeighborTargets(8, new HashSet<int> { 1, 2 }).Any(),
            "An already cleared reachable room is not selected again.");
        int[] chainRooms = new[] { 1, 0, 2, 0, 3 };
        var chain = new CqbLocalTopology(5, 1, chainRooms, Enumerable.Repeat(true, 5).ToArray(),
            new[] { false, true, false, true, false });
        Check(chain.NeighborTargets(0, new HashSet<int> { 1 }).SequenceEqual(new[] { 2 }),
            "An open passage must not skip the first uncleared room to target a deeper room.");
        Check(chain.NeighborTargets(0, new HashSet<int> { 1, 2 }).SequenceEqual(new[] { 4 }),
            "The next uncleared room becomes eligible through the secured room network.");
        Check(!closed.SameAs(opened), "Opening a wall changes the topology revision input.");
        Check(closed.SameAs(new CqbLocalTopology(width, height, rooms, (bool[])usable.Clone(), portals)),
            "An unchanged local map does not trigger re-planning.");
        Check(closed.Path(8, 12).Count == 0, "A rebuilt wall closes the previously used path.");
        Check(opened.Path(-1, 12).Count == 0 && opened.Path(8, 100).Count == 0,
            "Local queries outside the captured window cannot invent routes.");
        Check(!opened.NeighborTargets(-1, new HashSet<int>()).Any(), "An outside observer produces no local room candidates.");
        int[] twoRooms = Enumerable.Range(0, 24).Select(i => i % 8 < 3 ? 1 : i % 8 > 4 ? 2 : 0).ToArray();
        bool[] twoUsable = twoRooms.Select(room => room > 0).ToArray();
        bool[] twoPortals = twoRooms.Select(room => room == 0).ToArray();
        twoUsable[11] = true;
        Check(new CqbLocalTopology(8, 3, twoRooms, twoUsable, twoPortals).Path(9, 14).Count == 0,
            "One destroyed cell in a double wall does not create a route.");
        twoUsable[12] = true;
        Check(new CqbLocalTopology(8, 3, twoRooms, twoUsable, twoPortals).Path(9, 14).Count > 0,
            "Both destroyed cells in a double wall create a route.");
        Console.WriteLine($"PASS: {checks - before} local CQB passage, stable room progress and topology refresh assertions.");
    }

    private static void CheckCqbIntent()
    {
        int before = checks;
        Check(RaidCqbPolicy.Intent(2, 2, true) == RaidCqbIntent.ClearCurrentRoom, "A reachable occupied-room goal does not require entry.");
        Check(RaidCqbPolicy.Intent(2, 2, false) == RaidCqbIntent.RemoveInteriorObstacle, "A new wall can split one static room and still require demolition.");
        Check(RaidCqbPolicy.Intent(2, 3, true) == RaidCqbIntent.EnterRoom, "A reachable new room remains an entry action.");
        Check(RaidCqbPolicy.Intent(0, 3, false) == RaidCqbIntent.None, "Exterior approach retains its existing planning.");
        var cleared = new HashSet<int> { 2, 4 };
        Check(!RaidCqbPolicy.BreachDestination(2, 2, true, true, cleared), "Do not breach back into the occupied reachable room.");
        Check(RaidCqbPolicy.BreachDestination(2, 2, true, false, cleared), "Remove a rebuilt internal barrier even if the original room was secured.");
        Check(!RaidCqbPolicy.BreachDestination(2, 4, true, false, cleared), "Another secured room is not a demolition destination.");
        Check(!RaidCqbPolicy.BreachDestination(2, 3, false, false, cleared), "A candidate with an unreachable staging side is rejected.");
        Check(!RaidCqbPolicy.BreachDestination(2, 0, true, false, cleared), "Interior breach must have an indoor destination.");
        Check(RaidCqbPolicy.BreachDestination(2, 3, true, false, cleared), "A blocked adjacent uncleared room remains a breach destination.");
        var topology = new CqbLocalTopology(5, 1, new[] { 1, 99, 2, 0, 3 }, Enumerable.Repeat(true, 5).ToArray(),
            new[] { false, true, false, true, false });
        Check(topology.NeighborTargets(0, new HashSet<int> { 1 }).SequenceEqual(new[] { 2 }),
            "A door's own static room ID cannot become a false room-clearance goal.");
        Check(topology.Path(0, 3, new HashSet<int> { 1 }).Count == 0,
            "Staging cannot silently cross an uncleared room to reach a deeper wall.");
        Check(topology.Path(0, 3, new HashSet<int> { 1, 2 }).Count == 4,
            "Staging can use secured rooms and open doorway cells.");
        int[] known = topology.Distances(0, out _, new HashSet<int> { 1, 2 });
        Check(known[2] == 2 && known[4] == -1, "Contact rechecking can revisit a cleared room without crossing an unknown room.");
        var blocked = new CqbLocalTopology(5, 1, new[] { 1, 99, 2, 0, 3 },
            new[] { true, false, true, true, true }, new[] { false, true, false, true, false });
        Check(blocked.Distances(0, out _, new HashSet<int> { 1, 2 })[2] == -1,
            "A closed/rebuilt passage cannot be treated as a known recheck route or trigger repeat demolition.");
        Console.WriteLine($"PASS: {checks - before} CQB action intent and valid breach boundary assertions.");
    }
}
