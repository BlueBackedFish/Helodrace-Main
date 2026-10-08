using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Helodrace.Tactics;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Helodrace
{
    public sealed partial class TacticalEngineAuditResult
    {
        [DataMember] public bool r7DefenseRegistered, r7DefenseIdleNative, r7DefenseEarlySight, r7DefenseNoAdvance;
        [DataMember] public bool r7DefenseShots, r7DefenseReturnedNative, r7DefenseNoCqb, r7DefenseComplete;
        [DataMember] public string[] r7DefenseEvents;
        [DataMember] public bool r7DefenseAssaultTransition, r7DefenseAssaultComplete;
    }
    public sealed partial class MapComponent_TacticalEngineAudit
    {
        private bool DefenseFixture => result.fixtureCase == "r7-defense" || result.fixtureCase == "r7-defense-transition";
        private int defenseStep, defenseHiddenAt;
        private List<string> defenseEvents = new List<string>();
        private void DefenseEvent(string message)
        {
            defenseEvents.Add((GenTicks.TicksGame - started) + ":" + message);
            result.r7DefenseEvents = defenseEvents.ToArray();
            Log.Message("R7 defense " + defenseEvents.Last());
        }
        private void InitializeDefenseDrill(Faction faction)
        {
            if (result.units != 2 || TacticalEngineSelection.Kind != TacticalEngineKind.New)
                throw new InvalidOperationException("Defense drill requires exactly two complete squads and the new engine.");
            int size = raiders.Count / 2;
            LordMaker.MakeNewLord(faction, new LordJob_DefendBase(faction, new IntVec3(72, 0, 108),
                result.fixtureCase == "r7-defense-transition" ? 2400 : 1000000), map,
                raiders.Take(size).ToList());
            LordMaker.MakeNewLord(faction, new LordJob_DefendPoint(new IntVec3(90, 0, 108), 8f, 24f, false, false), map,
                raiders.Skip(size).ToList());
            result.r7DefenseNoAdvance = result.r7DefenseNoCqb = true;
        }
        private void ApplyDefenseDrill()
        {
            MapComponent_TacticalCommands service = map.GetComponent<MapComponent_TacticalCommands>();
            TacticalSquadCommand[] commands = service.Commands.OrderBy(c => c.Id).ToArray();
            if (commands.Length != 2) return;
            if (defenseStep >= 4 && result.fixtureCase == "r7-defense-transition")
            {
                TacticalSquadCommand attack = commands[0], guard = commands[1];
                if (!result.r7DefenseAssaultTransition && !attack.Defensive && attack.Plan != null
                    && GameComponent_TacticalCommands.IsAssaultLord(attack.RaidLord))
                {
                    result.r7DefenseAssaultTransition = true;
                    DefenseEvent("native timed graph changed DefendBase to Assault; same command owns named-bed mission");
                }
                if (!result.r7DefenseAssaultComplete && result.r7DefenseAssaultTransition
                    && attack.Phase == TacticalCommandPhase.Complete && attack.Members.All(m => m.EntryAssignmentDone)
                    && guard.Defensive && guard.Phase == TacticalCommandPhase.Defending && guard.Plan == null)
                {
                    result.r7DefenseAssaultComplete = true;
                    DefenseEvent("transitioned squad completed CQB while the other squad remains on native fixed defense");
                }
                return;
            }
            result.r7DefenseRegistered = commands.All(c => c.Defensive && c.Goal == c.DefenseAnchor)
                && commands.Select(c => c.RaidLord.CurLordToil.GetType()).Distinct().Count() == 2;
            result.r7DefenseNoAdvance &= service.FieldBounds == 0 && commands.All(c => c.FieldResponse == null
                || c.FieldResponse.Anchor == c.DefenseAnchor && c.FieldResponse.Stage != TacticalFieldStage.Moving);
            result.r7DefenseNoCqb &= service.PlansAttempted == 0 && service.RoomsSecured == 0
                && commands.All(c => c.Plan == null && c.Phase != TacticalCommandPhase.Breach && c.OpeningAction == null);
            int tick = GenTicks.TicksGame;
            if (defenseStep == 0)
            {
                if (tick - started < 360) return;
                result.r7DefenseIdleNative = commands.All(c => c.Members.All(m => m.Job == null))
                    && service.JobsIssued == 0;
                owner.Position = new IntVec3(80, 0, 163); defenseStep = 1;
                DefenseEvent("native DefendBase and Defend idle; distant actor exposed");
            }
            if (defenseStep == 1)
            {
                if (commands.Any(c => c.FieldResponse == null)) return;
                result.r7DefenseEarlySight = commands.All(c => c.Members.Any(m => m.Pawn.Position.DistanceTo(owner.Position)
                    > (m.Pawn.equipment?.PrimaryEq?.PrimaryVerb?.verbProps.range ?? 0)));
                if (commands.Any(c => c.FieldResponse.Assigned != c.Members.Count)) return;
                owner.Position = new IntVec3(80, 0, 124); defenseStep = 2;
                DefenseEvent("both squads detected outside range and formed bounded defense; actor approached");
            }
            if (defenseStep == 2)
            {
                if (service.FieldShots == 0) return;
                result.r7DefenseShots = true; owner.Position = new IntVec3(180, 0, 180);
                defenseHiddenAt = tick; defenseStep = 3; result.caseTriggered = true;
                DefenseEvent("actual response shot; actor hidden");
            }
            if (defenseStep == 3 && tick - defenseHiddenAt >= 600)
            {
                result.r7DefenseReturnedNative = commands.All(c => c.Phase == TacticalCommandPhase.Defending
                    && c.FieldResponse == null && c.ContactResponse == null && c.Members.All(m => m.Job == null
                        && m.Pawn.Position.DistanceToSquared(c.DefenseAnchor) <= 1600))
                    && service.ClaimCount == 0 && service.LeaseCount == 0;
                result.r7DefenseComplete = result.r7DefenseRegistered && result.r7DefenseIdleNative
                    && result.r7DefenseEarlySight && result.r7DefenseNoAdvance && result.r7DefenseShots
                    && result.r7DefenseReturnedNative && result.r7DefenseNoCqb;
                if (result.r7DefenseComplete)
                { defenseStep = 4; DefenseEvent("native fixed defense restored; no breach plan or forward bound"); }
            }
        }
    }
}
