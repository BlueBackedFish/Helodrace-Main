using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using RimWorld;
using Verse;

namespace Helodrace.Squads
{
    // A view of the real organization tree, never a cloned organization/budget.
    // A squad owns its child teams. Headquarters owns only its direct personnel.
    public sealed class RaidTacticalUnit
    {
        private sealed class CachedUnit
        {
            public CachedUnit() { }
            internal CombatOrganization Organization;
            internal int Revision;
            internal RaidTacticalUnit Unit;
        }
        private static readonly ConditionalWeakTable<CombatGroup, CachedUnit> views =
            new ConditionalWeakTable<CombatGroup, CachedUnit>();
        public CombatOrganization Organization { get; }
        public CombatGroup Group { get; }
        public bool IncludesChildren { get; }
        public string OrganizationId => Organization.id;
        public string GroupId => Group.id;
        public string Id { get; }
        public string Label => Group.name ?? GroupId;
        public Faction Faction => Organization.faction;
        public IEnumerable<CombatGroup> Groups => IncludesChildren
            ? Group.DescendantsAndSelf : new[] { Group };
        public IEnumerable<Pawn> Members => IncludesChildren ? Group.AllMembers : Group.Members.Distinct();
        public int StandardPersonnel => IncludesChildren
            ? Group.formation?.StandardPersonnel ?? Group.AllAssignments.Count()
            : Group.formation?.Slots.Sum(slot => slot.count) ?? Group.roleAssignments.Count;
        public float CommandEfficiency => Groups.Min(group => group.CommandEfficiency);
        public Pawn Commander => Group.EffectiveCommander;

        private RaidTacticalUnit(CombatOrganization organization, CombatGroup group, bool includesChildren)
        {
            Organization = organization;
            Group = group;
            IncludesChildren = includesChildren;
            Id = organization.id + "::" + group.id;
        }

        public static IEnumerable<RaidTacticalUnit> ForOrganization(CombatOrganization organization)
        {
            if (organization == null) yield break;
            foreach (CombatGroup root in organization.rootGroups)
                foreach (RaidTacticalUnit unit in Partition(organization, root)) yield return unit;
        }

        private static IEnumerable<RaidTacticalUnit> Partition(CombatOrganization organization, CombatGroup group)
        {
            if (group.formation?.unitLevel == "Squad" || group.children.Count == 0)
            {
                yield return View(organization, group, true);
                yield break;
            }
            if (group.roleAssignments.Count > 0)
                yield return View(organization, group, false);
            foreach (CombatGroup child in group.children)
                foreach (RaidTacticalUnit unit in Partition(organization, child)) yield return unit;
        }

        public static RaidTacticalUnit ForGroup(CombatGroup group)
        {
            if (group?.Organization == null) return null;
            CachedUnit cached = views.GetOrCreateValue(group);
            CombatOrganization organization = group.Organization;
            if (cached.Unit != null && cached.Organization == organization && cached.Revision == organization.StructureRevision)
                return cached.Unit;
            CombatGroup owner = group;
            for (CombatGroup parent = group; parent != null; parent = parent.Parent)
                if (parent.formation?.unitLevel == "Squad") owner = parent;
            RaidTacticalUnit unit = View(organization, owner, owner.formation?.unitLevel == "Squad" || owner.children.Count == 0);
            cached.Organization = organization;
            cached.Revision = organization.StructureRevision;
            cached.Unit = unit;
            return cached.Unit;
        }

        private static RaidTacticalUnit View(CombatOrganization organization, CombatGroup group, bool includesChildren)
        {
            CachedUnit cached = views.GetOrCreateValue(group);
            if (cached.Unit?.Group == group && cached.Organization == organization
                && cached.Revision == organization.StructureRevision && cached.Unit.IncludesChildren == includesChildren)
                return cached.Unit;
            cached.Organization = organization; cached.Revision = organization.StructureRevision;
            cached.Unit = new RaidTacticalUnit(organization, group, includesChildren);
            return cached.Unit;
        }

        public static RaidTacticalUnit ForPawn(Pawn pawn) => ForGroup(OrganizationAPI.GetGroup(pawn));
        public static IEnumerable<RaidTacticalUnit> All => OrganizationAPI.Registry?.Organizations
            .SelectMany(ForOrganization) ?? Enumerable.Empty<RaidTacticalUnit>();
    }
}
