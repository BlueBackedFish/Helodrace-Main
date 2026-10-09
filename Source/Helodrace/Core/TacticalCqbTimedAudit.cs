using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using RimWorld;
using Verse;
using Helodrace.Tactics;

namespace Helodrace
{
    public sealed partial class TacticalEngineAuditResult
    {
        [DataMember] public string[] r7CqbEvents;
        [DataMember] public bool r7CqbStimulusComplete;
    }
    public sealed partial class MapComponent_TacticalEngineAudit
    {
        private bool TimedCqbFixture => result.fixtureCase == "r7-cqb-cpu";
        private static readonly int[] CqbEventTicks = { 3000, 4200, 5100, 6500, 7500 };
        private static readonly IntVec3[] CqbEventPositions = {
            new IntVec3(180,0,180), new IntVec3(114,0,108), new IntVec3(180,0,180),
            new IntVec3(99,0,108), new IntVec3(180,0,180) };
        private readonly List<string> cqbEvents = new List<string>();
        private int cqbEvent;

        // Only explicit CLI fixtures enter this path. Same actor, geometry and
        // times in every engine; never inspect an AI phase to choose an input.
        private void ApplyTimedCqbStimulus()
        {
            int age = GenTicks.TicksGame - started;
            if (cqbEvent >= CqbEventTicks.Length || age < CqbEventTicks[cqbEvent]) return;
            owner.Position = CqbEventPositions[cqbEvent];
            if (cqbEvent == 1) (owner.Position.GetEdifice(map) as Building_Door)?.StartManualOpenBy(owner);
            string value = age + ":" + owner.Position;
            cqbEvents.Add(value); cqbEvent++;
            result.caseTriggered = true; result.r7CqbEvents = cqbEvents.ToArray();
            result.r7CqbStimulusComplete = cqbEvent == CqbEventTicks.Length && cqbEvents.SequenceEqual(
                CqbEventTicks.Select((tick, index) => tick + ":" + CqbEventPositions[index]));
            Log.Message("R7 fixed CQB input " + value);
        }
        // Offline audit only: each agreed party must have actually entered and
        // secured its own area; the whole raid must cover every requested room
        // and the named-bed goal. No other squad's state enters AI decisions.
        internal static bool TimedCqbMissionCoverage(IList<TacticalSquadCommand> commands, IList<IntVec3> regions)
        {
            if (commands.Count == 0 || regions.Count == 0 || commands.Select(c => c.Id).Distinct().Count() != commands.Count
                || commands.Any(c => c.Phase != TacticalCommandPhase.Complete)
                || !regions.All(cell => commands.Any(c => c.SecuredCells.Contains(cell)))
                || !commands.Any(c => c.GoalSecured && c.SecuredCells.Contains(c.Goal))) return false;
            return commands.All(command =>
            {
                if (command.SecuredPlans.Count == 0 || command.SecuredPlans.Select(p => p.Opening).Distinct().Count()
                    != command.SecuredPlans.Count) return false;
                if (TacticalRoomProgress.CoversGoal(command, regions)) return true;
                TacticalCooperationState agreement = command.Link.Cooperation;
                TacticalCooperationAgenda agenda = agreement.Agenda;
                if (agreement.Stage != TacticalAgreementStage.Finished || agenda == null || agenda.Goal != command.Goal
                    || command.Id != agenda.First && command.Id != agenda.Second
                    || !command.SecuredCells.Contains(agenda.Area(command.Id))) return false;
                TacticalSquadCommand peer = commands.FirstOrDefault(c => c.Id == agenda.Peer(command.Id));
                TacticalCooperationAgenda other = peer?.Link.Cooperation.Agenda;
                return peer != null && peer.Goal == command.Goal && peer.Link.Cooperation.Stage == TacticalAgreementStage.Finished
                    && other != null && other.Id == agenda.Id && other.First == agenda.First && other.Second == agenda.Second
                    && other.Goal == agenda.Goal && other.Forward == agenda.Forward
                    && other.StartAt == agenda.StartAt && other.Deadline == agenda.Deadline
                    && peer.SecuredCells.Contains(other.Area(peer.Id));
            });
        }
    }
}
