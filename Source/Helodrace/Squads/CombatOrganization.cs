using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Squads
{
    public enum CommandState
    {
        Normal, CommanderLost, SuccessionPending, ActingCommander, CommandRestored
    }

    public sealed class RoleAssignment : IExposable
    {
        public Pawn pawn;
        public RoleDef combatRole;
        public RoleDef commandRole;
        public List<RoleDef> commandQualifications = new List<RoleDef>();
        public int explicitSuccessionOrder = -1;
        public int rankPriority;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Defs.Look(ref combatRole, "combatRole");
            Scribe_Defs.Look(ref commandRole, "commandRole");
            Scribe_Collections.Look(ref commandQualifications, "commandQualifications", LookMode.Def);
            Scribe_Values.Look(ref explicitSuccessionOrder, "explicitSuccessionOrder", -1);
            Scribe_Values.Look(ref rankPriority, "rankPriority");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                commandQualifications = commandQualifications ?? new List<RoleDef>();
        }
    }

    public sealed class CombatGroup : IExposable
    {
        public string id;
        public string name;
        public string parentGroupId;
        public int parentSuccessionPriority = 100;
        public FormationDef formation;
        public List<CombatGroup> children = new List<CombatGroup>();
        public List<RoleAssignment> roleAssignments = new List<RoleAssignment>();
        public Pawn commander;
        public Pawn actingCommander;
        public List<Pawn> successionList = new List<Pawn>();
        public CommandState commandState;
        public int commanderLostTick = -1;
        public int actingStartedTick = -1;
        public CombatGroup Parent { get; internal set; }
        public CombatOrganization Organization { get; internal set; }
        public IEnumerable<Pawn> Members => roleAssignments.Select(assignment => assignment.pawn)
            .Where(pawn => pawn != null);
        public IEnumerable<CombatGroup> DescendantsAndSelf
        {
            get
            {
                yield return this;
                foreach (CombatGroup child in children)
                    foreach (CombatGroup descendant in child.DescendantsAndSelf) yield return descendant;
            }
        }
        public IEnumerable<RoleAssignment> AllAssignments => DescendantsAndSelf
            .SelectMany(group => group.roleAssignments);
        public IEnumerable<Pawn> AllMembers => AllAssignments.Select(assignment => assignment.pawn)
            .Where(pawn => pawn != null).Distinct();
        public Pawn EffectiveCommander => Available(commander) ? commander
            : Available(actingCommander) ? actingCommander : null;
        public bool ParentCommandAvailable => Parent == null || Parent.EffectiveCommander != null;
        public DoctrineDef Doctrine => formation?.doctrine ?? Organization?.doctrine;
        public int CommandDelay => commandState == CommandState.CommanderLost
            || commandState == CommandState.SuccessionPending
            ? Doctrine?.commandLossDelayTicks ?? 0 : 0;
        public float CommandEfficiency => EffectiveCommander == null ? 0f
            : commandState == CommandState.ActingCommander
                ? Doctrine?.actingCommandEfficiency ?? 0.7f : 1f;

        public bool Available(Pawn pawn)
        {
            return pawn != null && !pawn.Dead && !pawn.Downed && !pawn.Destroyed
                && !pawn.InMentalState && !pawn.IsPrisoner && !pawn.IsSlave
                && pawn.Faction == Organization?.faction;
        }

        public void InitializeCommand()
        {
            foreach (CombatGroup child in children) child.InitializeCommand();
            commander = roleAssignments.Where(assignment => assignment.commandRole == formation.commanderRole)
                .OrderBy(assignment => assignment.explicitSuccessionOrder < 0 ? int.MaxValue
                    : assignment.explicitSuccessionOrder)
                .Select(assignment => assignment.pawn).FirstOrDefault();
            RebuildSuccession();
            commander = commander ?? successionList.FirstOrDefault(Available);
            commandState = CommandState.Normal;
        }

        private bool Qualified(RoleAssignment assignment)
        {
            RoleDef role = formation.commanderRole;
            if (assignment.pawn == null || role == null) return false;
            bool authorized = assignment.commandQualifications.Contains(role)
                || (assignment.commandRole?.CanCommand(formation.unitLevel) ?? false);
            if (!authorized) return false;
            if (role.preferredSkill != null && (assignment.pawn.skills?.GetSkill(role.preferredSkill)?.Level ?? 0)
                < role.minimumSkillLevel) return false;
            return role.requiredEquipment.All(def => assignment.pawn.equipment?.AllEquipmentListForReading
                    .Any(thing => thing.def == def) == true
                || assignment.pawn.apparel?.WornApparel.Any(thing => thing.def == def) == true
                || assignment.pawn.inventory?.Count(def) > 0);
        }

        private int Tier(RoleAssignment assignment)
        {
            if (assignment.explicitSuccessionOrder >= 0 && roleAssignments.Contains(assignment)) return 0;
            if (roleAssignments.Contains(assignment) && formation.successionRoles.Contains(assignment.commandRole))
                return 0;
            if (formation.allowSubordinateCommanders && Qualified(assignment)
                && children.Any(child => child.EffectiveCommander == assignment.pawn)) return 1;
            if (Qualified(assignment)) return 2;
            return Experience(assignment) > 0 ? 3 : 4;
        }

        private int ExplicitOrder(RoleAssignment assignment)
        {
            if (Tier(assignment) == 1)
                return children.First(child => child.EffectiveCommander == assignment.pawn).parentSuccessionPriority;
            if (!roleAssignments.Contains(assignment)) return int.MaxValue;
            if (assignment.explicitSuccessionOrder >= 0) return assignment.explicitSuccessionOrder;
            int index = formation.successionRoles.IndexOf(assignment.commandRole);
            return index >= 0 ? 10000 + index : int.MaxValue;
        }

        private int Experience(RoleAssignment assignment)
        {
            SkillDef skill = formation.commanderRole?.preferredSkill ?? assignment.combatRole?.preferredSkill;
            return skill == null ? 0 : assignment.pawn?.skills?.GetSkill(skill)?.Level ?? 0;
        }

        public void RebuildSuccession()
        {
            successionList = AllAssignments.Where(assignment => assignment.pawn != null)
                .OrderBy(Tier).ThenBy(ExplicitOrder)
                .ThenByDescending(assignment => assignment.commandRole?.commandAuthority ?? 0)
                .ThenBy(assignment => assignment.commandRole?.successionPriority
                    ?? assignment.combatRole?.successionPriority ?? 100)
                .ThenByDescending(assignment => assignment.rankPriority)
                .ThenByDescending(Experience)
                .ThenBy(assignment => assignment.pawn.thingIDNumber)
                .Select(assignment => assignment.pawn).Distinct().ToList();
        }

        public void UpdateCommand(int tick)
        {
            foreach (CombatGroup child in children) child.UpdateCommand(tick);
            if (Available(commander))
            {
                if (commandState != CommandState.Normal && commandState != CommandState.CommandRestored)
                {
                    actingCommander = null;
                    commanderLostTick = actingStartedTick = -1;
                    commandState = CommandState.CommandRestored;
                }
                else commandState = CommandState.Normal;
                return;
            }
            if (Available(actingCommander))
            {
                int recovery = Doctrine.commandRecoveryTicks;
                if (tick - actingStartedTick >= recovery)
                    commandState = commandState == CommandState.ActingCommander
                        ? CommandState.CommandRestored : CommandState.Normal;
                return;
            }
            if (commanderLostTick < 0 || actingCommander != null)
            {
                actingCommander = null;
                actingStartedTick = -1;
                commanderLostTick = tick;
                commandState = CommandState.CommanderLost;
                return;
            }
            commandState = CommandState.SuccessionPending;
            if (tick - commanderLostTick < Doctrine.commandLossDelayTicks) return;
            RebuildSuccession();
            actingCommander = successionList.FirstOrDefault(Available);
            if (actingCommander == null) return;
            actingStartedTick = tick;
            commandState = CommandState.ActingCommander;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref parentGroupId, "parentGroupId");
            Scribe_Values.Look(ref parentSuccessionPriority, "parentSuccessionPriority", 100);
            Scribe_Defs.Look(ref formation, "formation");
            Scribe_Collections.Look(ref children, "children", LookMode.Deep);
            Scribe_Collections.Look(ref roleAssignments, "roleAssignments", LookMode.Deep);
            Scribe_References.Look(ref commander, "commander");
            Scribe_References.Look(ref actingCommander, "actingCommander");
            Scribe_Collections.Look(ref successionList, "successionList", LookMode.Reference);
            Scribe_Values.Look(ref commandState, "commandState");
            Scribe_Values.Look(ref commanderLostTick, "commanderLostTick", -1);
            Scribe_Values.Look(ref actingStartedTick, "actingStartedTick", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                children = children ?? new List<CombatGroup>();
                roleAssignments = roleAssignments ?? new List<RoleAssignment>();
                successionList = successionList ?? new List<Pawn>();
            }
        }
    }

    public sealed class CombatOrganization : IExposable
    {
        public string id;
        public Faction faction;
        public DoctrineDef doctrine;
        public List<CombatGroup> rootGroups = new List<CombatGroup>();
        public float initialRaidPoints;
        public float formationPointsSpent;
        private float supportPointsRemaining;
        public IEnumerable<CombatGroup> AllGroups => rootGroups.SelectMany(group => group.DescendantsAndSelf);
        public IEnumerable<Pawn> AllMembers => AllGroups.SelectMany(group => group.Members).Distinct();
        public float SupportPointsRemaining => supportPointsRemaining;
        public float PointOverrun => Math.Max(0, formationPointsSpent - initialRaidPoints);
        public CommandState GlobalCommandState
        {
            get
            {
                foreach (CommandState state in new[] { CommandState.CommanderLost, CommandState.SuccessionPending,
                    CommandState.ActingCommander, CommandState.CommandRestored })
                    if (rootGroups.Any(group => group.commandState == state)) return state;
                return CommandState.Normal;
            }
        }

        public void SetBudget(FormationPlan plan)
        {
            initialRaidPoints = plan.initialRaidPoints;
            formationPointsSpent = plan.formationPointsSpent;
            supportPointsRemaining = plan.SupportPointsRemaining;
        }
        public float GetSupportPoints() => supportPointsRemaining;
        public bool CanSpendSupportPoints(float cost) => cost >= 0 && !float.IsNaN(cost)
            && !float.IsInfinity(cost) && cost <= supportPointsRemaining;
        public bool TrySpendSupportPoints(float cost)
        {
            if (!CanSpendSupportPoints(cost)) return false;
            supportPointsRemaining -= cost;
            return true;
        }
        public void RefundSupportPoints(float cost)
        {
            if (cost < 0 || float.IsNaN(cost) || float.IsInfinity(cost)) return;
            supportPointsRemaining = Math.Min(Math.Max(0, initialRaidPoints - formationPointsSpent),
                supportPointsRemaining + cost);
        }
        public void RestoreTreeLinks()
        {
            foreach (CombatGroup root in rootGroups) RestoreGroup(root, null);
        }

        public bool RemoveMember(Pawn pawn)
        {
            if (pawn == null) return false;
            bool removed = false;
            foreach (CombatGroup group in AllGroups)
            {
                removed |= group.roleAssignments.RemoveAll(assignment => assignment.pawn == pawn) > 0;
                if (group.commander == pawn) group.commander = null;
                if (group.actingCommander == pawn) group.actingCommander = null;
                group.successionList.RemoveAll(candidate => candidate == pawn);
            }
            return removed;
        }
        private void RestoreGroup(CombatGroup group, CombatGroup parent)
        {
            group.Organization = this;
            group.Parent = parent;
            group.parentGroupId = parent?.id;
            foreach (CombatGroup child in group.children) RestoreGroup(child, group);
        }
        public void ExposeData()
        {
            Scribe_Values.Look(ref id, "id");
            Scribe_References.Look(ref faction, "faction");
            Scribe_Defs.Look(ref doctrine, "doctrine");
            Scribe_Collections.Look(ref rootGroups, "rootGroups", LookMode.Deep);
            Scribe_Values.Look(ref initialRaidPoints, "initialRaidPoints");
            Scribe_Values.Look(ref formationPointsSpent, "formationPointsSpent");
            Scribe_Values.Look(ref supportPointsRemaining, "supportPointsRemaining");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                rootGroups = rootGroups ?? new List<CombatGroup>();
                RestoreTreeLinks();
            }
        }
    }
}
