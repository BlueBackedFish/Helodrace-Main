using System;
using System.Collections.Generic;
using System.Linq;
using Helodrace.Tactics;
using Verse;

namespace Helodrace
{
    public sealed partial class MapComponent_TacticalEngineAudit
    {
        private bool FieldFixture => result.fixtureCase == "r6-field-drill" || result.fixtureCase == "r6-smoke-drill";
        private int fieldStep, fieldStepAt;
        private TacticalSquadCommand fieldCommand;
        private TacticalLocalPlan fieldPlan;
        private IntVec3 fieldSeen;
        private readonly List<string> fieldEvents = new List<string>();
        private bool fieldPostsUnique = true, fieldSingleTeam = true, fieldFrozen = true;
        private long fieldBoundBaseline;
        private bool fieldSmokeTargets = true;
        private long fieldSmokeLogged;

        private void FieldEvent(string value)
        {
            string message = (GenTicks.TicksGame - started) + ":" + value;
            if (fieldEvents.Count < 32) fieldEvents.Add(message);
            Log.Message("R6 field drill " + message);
        }
        private void ApplyFieldDrill()
        {
            if (TacticalEngineSelection.Kind != TacticalEngineKind.New || fieldStep >= 5) return;
            int tick = GenTicks.TicksGame;
            MapComponent_TacticalCommands service = map.GetComponent<MapComponent_TacticalCommands>();
            if (fieldStep == 0)
            {
                fieldCommand = service.Commands.FirstOrDefault(c => c.Phase == TacticalCommandPhase.Stack
                    && c.Members.Count(m => c.Plan.Stack.Contains(m.Pawn.Position)) >= 6);
                if (fieldCommand == null) return;
                fieldPlan = fieldCommand.Plan;
                Pawn anchor = fieldCommand.Members[fieldCommand.Members.Count / 2].Pawn;
                fieldSeen = new IntVec3(anchor.Position.x - 12, 0, anchor.Position.z + 55);
                if (!fieldSeen.Standable(map)) throw new InvalidOperationException("Field drill requires an open exterior.");
                owner.Position = fieldSeen; fieldStepAt = tick; fieldStep = 1; result.caseTriggered = true;
                FieldEvent("exterior actor exposed " + fieldSeen); return;
            }
            TacticalContact seen = fieldCommand.Contacts.Memory.Entries.FirstOrDefault(c => c.EnemyId == owner.thingIDNumber);
            TacticalFieldResponse field = fieldCommand.FieldResponse;
            if (field != null)
            {
                var posts = field.Posts.Where(c => c.IsValid).ToArray();
                fieldPostsUnique &= posts.Distinct().Count() == posts.Length && fieldCommand.Plan == fieldPlan;
                if (field.Stage == TacticalFieldStage.Moving)
                {
                    var moving = fieldCommand.Members.Where(m => m.Pawn.Spawned && !m.Pawn.Downed && !m.Pawn.Dead
                        && m.Pawn.pather.Moving && m.Job?.def.defName == "HD_NewTacticalContactGuard")
                        .Select(m => m.Fireteam).Distinct().ToArray();
                    fieldSingleTeam &= moving.All(team => team == field.MovingTeam);
                }
                if (field.Screen != null)
                {
                    fieldSmokeTargets &= field.Screen.Target == field.Anchor + field.Forward * 8;
                    if (field.Screen.Launched && RaidSmokeUtility.SmokeAt(map, field.Screen.Target))
                        result.newFieldSmokeSeen = true;
                }
                if (service.FieldSmokeAdvances > fieldSmokeLogged)
                {
                    fieldSmokeLogged = service.FieldSmokeAdvances;
                    FieldEvent("screen established; movement segments=" + fieldSmokeLogged);
                }
            }
            if (fieldStep == 1)
            {
                if (field == null || seen?.Position != fieldSeen) return;
                float maxRange = fieldCommand.Members.Max(m => m.Pawn.equipment?.PrimaryEq?.PrimaryVerb?.verbProps.range ?? 0);
                result.newFieldEarlySight = field.Anchor.DistanceTo(fieldSeen) > maxRange;
                fieldBoundBaseline = service.FieldBounds; fieldStepAt = tick; fieldStep = 2;
                FieldEvent("field response outside range=" + maxRange + " distance=" + field.Anchor.DistanceTo(fieldSeen)); return;
            }
            if (fieldStep == 2)
            {
                if (field == null || field.Stage != TacticalFieldStage.Defending) return;
                if (field.High && service.FieldBounds <= fieldBoundBaseline) return;
                if (result.fixtureCase == "r6-smoke-drill" && service.FieldSmokeAdvances < 2) return;
                // Real second sighting, without modifying memory/controller.
                fieldSeen += new IntVec3(0,0,-5); owner.Position = fieldSeen;
                fieldStepAt = tick; fieldStep = 3; FieldEvent("actor approached " + fieldSeen); return;
            }
            if (fieldStep == 3)
            {
                if (field == null || field.Motion != TacticalObservedMotion.Approaching || seen?.Position != fieldSeen) return;
                result.newFieldMotionObserved = true;
                owner.Position = new IntVec3(180,0,180); fieldStepAt = tick; fieldStep = 4;
                FieldEvent("motion observed; actor hidden"); return;
            }
            if (fieldStep == 4)
            {
                fieldFrozen &= seen == null || seen.Position == fieldSeen;
                if (field != null || service.FieldResumes == 0) return;
                result.newFieldMissionResumed = true; fieldStep = 5;
                FieldEvent("CQB mission resumed; original opening=" + fieldPlan.Opening);
            }
        }
        private void FinishFieldDrill()
        {
            if (!FieldFixture) return;
            MapComponent_TacticalCommands service = map.GetComponent<MapComponent_TacticalCommands>();
            result.newFieldResponses = service.FieldResponses; result.newFieldResumes = service.FieldResumes;
            result.newFieldBounds = service.FieldBounds; result.newFieldGuardJobs = service.FieldGuardJobs;
            result.newFieldPostCandidates = service.FieldPostCandidates;
            result.newFieldUniquePosts = fieldPostsUnique && fieldStep == 5;
            result.newFieldSingleTeamBounds = fieldSingleTeam && fieldStep == 5
                && (fieldCommand.Link.Unit.Faction.def.defName != "HD_HelodCivilHighFaction" || service.FieldBounds > 0);
            result.newFieldMemoryFrozen = fieldFrozen && fieldStep == 5;
            result.newFieldEvents = fieldEvents.ToArray();
            result.newFieldSmokePlans = service.FieldSmokePlans; result.newFieldSmokeThrows = service.FieldSmokeThrows;
            result.newFieldSmokeAdvances = service.FieldSmokeAdvances;
            result.newFieldSmokeSharedTargets = fieldSmokeTargets && service.FieldSmokeThrows >= 2;
        }
    }
}
