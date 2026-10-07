using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.Squads
{
    public sealed class GameComponent_CombatOrganizations : GameComponent
    {
        private List<CombatOrganization> organizations = new List<CombatOrganization>();
        private int nextOrganizationId = 1;
        private readonly Dictionary<string, CombatOrganization> byId = new Dictionary<string, CombatOrganization>();
        private readonly Dictionary<Pawn, CombatGroup> byPawn = new Dictionary<Pawn, CombatGroup>();
        public GameComponent_CombatOrganizations(Game game) { }
        public IReadOnlyList<CombatOrganization> Organizations => organizations;
        public string AllocateId() => "HD_Raid_" + nextOrganizationId++;
        public CombatOrganization GetOrganization(string id) => id != null && byId.TryGetValue(id, out var value)
            ? value : null;
        public CombatGroup GetGroup(Pawn pawn)
        {
            if (pawn == null || !byPawn.TryGetValue(pawn, out CombatGroup group)) return null;
            if (!IsDepartedWorldPawn(pawn)) return group;
            Detach(pawn);
            return null;
        }

        private static bool IsDepartedWorldPawn(Pawn pawn) => pawn != null && !pawn.Spawned
            && Find.WorldPawns?.Contains(pawn) == true;

        public void Detach(Pawn pawn)
        {
            MapComponent_RaidTacticalOrders.ForgetPawn(pawn);
            if (pawn == null || !byPawn.TryGetValue(pawn, out CombatGroup group)) return;
            CombatOrganization organization = group.Organization;
            organization.RemoveMember(pawn);
            byPawn.Remove(pawn);
            pawn.TryGetComp<PawnOrganizationComponent>()?.Clear();
            if (!organization.AllMembers.Any())
            {
                organizations.Remove(organization);
                byId.Remove(organization.id);
            }
        }

        public void Register(CombatOrganization organization)
        {
            foreach (Pawn pawn in byPawn.Keys.Where(IsDepartedWorldPawn).ToList()) Detach(pawn);
            if (byId.ContainsKey(organization.id)) throw new InvalidOperationException("Duplicate organization ID.");
            if (organization.AllMembers.Any(pawn => byPawn.ContainsKey(pawn)))
                throw new InvalidOperationException("Pawn is already in a combat organization.");
            organizations.Add(organization);
            Index(organization);
        }

        private void Index(CombatOrganization organization)
        {
            organization.RestoreTreeLinks();
            byId[organization.id] = organization;
            foreach (CombatGroup group in organization.AllGroups)
                foreach (Pawn pawn in group.Members)
                {
                    byPawn[pawn] = group;
                    pawn.TryGetComp<PawnOrganizationComponent>()?.Bind(organization.id, group.id, group.parentGroupId);
                }
        }

        private void RebuildLookup()
        {
            byId.Clear();
            byPawn.Clear();
            foreach (CombatOrganization organization in organizations.ToList())
            {
                foreach (Pawn pawn in organization.AllMembers.Where(IsDepartedWorldPawn).ToList())
                {
                    organization.RemoveMember(pawn);
                    pawn.TryGetComp<PawnOrganizationComponent>()?.Clear();
                }
                if (organization.AllMembers.Any()) Index(organization);
                else organizations.Remove(organization);
            }
        }

        public override void FinalizeInit()
        {
            RebuildLookup();
        }

        public override void GameComponentTick()
        {
            int tick = Find.TickManager.TicksGame;
            if (tick % 15 != 0) return;
            foreach (CombatOrganization organization in organizations)
            {
                // Off-map historical records do not require a tactical or command tick.
                if (organization.doctrine == null || !organization.AllMembers.Any(pawn => pawn.Spawned)) continue;
                ReevaluateCommand(organization, tick);
            }
        }

        public void ReevaluateCommand(CombatOrganization organization, int tick)
        {
            if (organization?.doctrine == null) return;
            var previous = organization.AllGroups.ToDictionary(group => group,
                group => group.actingCommander);
            foreach (CombatGroup root in organization.rootGroups) root.UpdateCommand(tick);
            foreach (CombatGroup group in organization.AllGroups)
                if (group.commandState == CommandState.ActingCommander
                    && group.actingCommander != previous[group])
                    RaidTacticalSpeech.Say(group.actingCommander,
                        "HD_RaidTactical_CommandAssumed");
        }

        public override void ExposeData()
        {
            Scribe_Collections.Look(ref organizations, "helodCombatOrganizations", LookMode.Deep);
            Scribe_Values.Look(ref nextOrganizationId, "helodNextOrganizationId", 1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                organizations = organizations ?? new List<CombatOrganization>();
                RebuildLookup();
            }
        }
    }

    public class CompProperties_PawnOrganization : CompProperties
    {
        public CompProperties_PawnOrganization() { compClass = typeof(PawnOrganizationComponent); }
    }

    public class PawnOrganizationComponent : ThingComp
    {
        internal int overlayRefreshFrame = -1;
        internal string overlayGroupLabel;
        internal string overlayRoleLabel;
        internal string overlayCommandLabel;
        public string organizationId;
        public string groupId;
        public string parentGroupId;
        public CombatGroup Group => OrganizationAPI.GetGroup(parent as Pawn);
        public CombatOrganization Organization => Group?.Organization;
        public RoleAssignment Assignment => Group?.roleAssignments.FirstOrDefault(value => value.pawn == parent);
        public RoleDef CombatRole => Assignment?.combatRole;
        public RoleDef CommandRole => Assignment?.commandRole;
        // A child commander can also act at multiple higher levels without losing any original role.
        public IEnumerable<RoleDef> ActingRoles => Organization?.AllGroups
            .Where(group => group.actingCommander == parent && group.EffectiveCommander == parent)
            .Select(group => group.formation.commanderRole) ?? Enumerable.Empty<RoleDef>();
        public RoleDef ActingRole => ActingRoles.OrderByDescending(role => role.commandAuthority).FirstOrDefault();
        public IEnumerable<RoleDef> CommandQualifications => Assignment?.commandQualifications
            ?? Enumerable.Empty<RoleDef>();

        public void Bind(string organization, string group, string parentGroup)
        {
            if (organizationId != organization || groupId != group || parentGroupId != parentGroup)
                MapComponent_RaidTacticalOrders.ForgetPawn(parent as Pawn);
            organizationId = organization;
            groupId = group;
            parentGroupId = parentGroup;
            overlayRefreshFrame = -1;
        }

        public void Clear()
        {
            MapComponent_RaidTacticalOrders.ForgetPawn(parent as Pawn);
            organizationId = groupId = parentGroupId = null;
            overlayGroupLabel = overlayRoleLabel = overlayCommandLabel = null;
            overlayRefreshFrame = -1;
        }

        public override void Notify_Killed(Map prevMap, DamageInfo? dinfo = null)
        {
            base.Notify_Killed(prevMap, dinfo);
            prevMap?.GetComponent<MapComponent_RaidTacticalExecution>()
                ?.RequestCasualtyReevaluation(RaidTacticalUnit.ForGroup(Group)?.Id, parent as Pawn);
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            // Capture on-map deaths before corpse disposal can pass the pawn to
            // WorldPawns and detach its organization. Notify_Killed coalesces.
            if (parent is Pawn pawn && pawn.Dead)
                map?.GetComponent<MapComponent_RaidTacticalExecution>()
                    ?.RequestCasualtyReevaluation(RaidTacticalUnit.ForGroup(Group)?.Id, pawn);
            MapComponent_RaidTacticalOrders.ForgetPawn(parent as Pawn);
        }

        public override void Notify_Downed()
        {
            base.Notify_Downed();
            parent.MapHeld?.GetComponent<MapComponent_RaidTacticalExecution>()
                ?.RequestCasualtyReevaluation(RaidTacticalUnit.ForGroup(Group)?.Id, parent as Pawn);
        }

        public override void DrawGUIOverlay()
        {
            OrganizationOverlay.Draw(this);
        }

        public override void PostExposeData()
        {
            Scribe_Values.Look(ref organizationId, "helodOrganizationId");
            Scribe_Values.Look(ref groupId, "helodGroupId");
            Scribe_Values.Look(ref parentGroupId, "helodParentGroupId");
        }

        public override string CompInspectStringExtra()
        {
            return Prefs.DevMode && Group != null ? OrganizationDebug.DescribePawn(parent as Pawn) : null;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (Prefs.DevMode && Organization != null)
                yield return new Command_Action
                {
                    defaultLabel = "HD_Squads_Inspect".Translate(),
                    defaultDesc = "HD_Squads_InspectDesc".Translate(),
                    action = () => Find.WindowStack.Add(new Dialog_MessageBox(OrganizationDebug.Describe(Organization)))
                };
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.ExitMap))]
    public static class Patch_PawnExitMap_ClearOrganization
    {
        public static void Postfix(Pawn __instance)
        {
            OrganizationAPI.Registry?.Detach(__instance);
        }
    }

    [HarmonyPatch(typeof(RimWorld.Planet.WorldPawns), nameof(RimWorld.Planet.WorldPawns.PassToWorld))]
    public static class Patch_WorldPawns_ClearOrganization
    {
        public static void Postfix(Pawn pawn)
        {
            OrganizationAPI.Registry?.Detach(pawn);
        }
    }

    public static class OrganizationAPI
    {
        public static GameComponent_CombatOrganizations Registry => Current.Game
            ?.GetComponent<GameComponent_CombatOrganizations>();
        public static CombatOrganization GetOrganization(Pawn pawn) => GetGroup(pawn)?.Organization;
        public static CombatGroup GetGroup(Pawn pawn) => Registry?.GetGroup(pawn);
        public static Pawn GetCommander(CombatGroup group) => group?.commander;
        public static Pawn GetEffectiveCommander(CombatGroup group) => group?.EffectiveCommander;
        public static CombatGroup GetParentGroup(CombatGroup group) => group?.Parent;
        public static IEnumerable<CombatGroup> GetChildGroups(CombatGroup group) => group?.children
            ?? Enumerable.Empty<CombatGroup>();
        public static IEnumerable<Pawn> GetSubordinates(Pawn pawn)
        {
            CombatOrganization organization = GetOrganization(pawn);
            return organization?.AllGroups.Where(group => group.EffectiveCommander == pawn)
                .SelectMany(group => group.AllMembers).Where(member => member != pawn).Distinct()
                ?? Enumerable.Empty<Pawn>();
        }
        public static float GetSupportPoints(CombatOrganization organization) => organization?.GetSupportPoints() ?? 0;
    }
}
