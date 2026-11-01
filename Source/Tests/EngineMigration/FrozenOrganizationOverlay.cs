// Frozen from 6e69db25; field refs expose only the original internal overlay cache to this fixture.
using System.Linq;
using HarmonyLib;
using LudeonTK;
using UnityEngine;
using Verse;

using Helodrace.Squads;
namespace Helodrace.Tests.FrozenOverlays
{
    public static class OrganizationOverlay
    {
        private static readonly AccessTools.FieldRef<PawnOrganizationComponent, int> RefreshFrame = AccessTools.FieldRefAccess<PawnOrganizationComponent, int>("overlayRefreshFrame");
        private static readonly AccessTools.FieldRef<PawnOrganizationComponent, string> GroupLabel = AccessTools.FieldRefAccess<PawnOrganizationComponent, string>("overlayGroupLabel");
        private static readonly AccessTools.FieldRef<PawnOrganizationComponent, string> RoleLabel = AccessTools.FieldRefAccess<PawnOrganizationComponent, string>("overlayRoleLabel");
        private static readonly AccessTools.FieldRef<PawnOrganizationComponent, string> CommandLabel = AccessTools.FieldRefAccess<PawnOrganizationComponent, string>("overlayCommandLabel");
        private static readonly Color LeaderColor = new Color(0.35f, 0.9f, 1f);
        private static readonly Color ActingColor = new Color(1f, 0.8f, 0.3f);
        private static readonly Color LostColor = new Color(1f, 0.4f, 0.4f);
        private static readonly Color DetailColor = new Color(0.85f, 0.85f, 0.85f);

        public static void Draw(PawnOrganizationComponent comp)
        {
            if (!Prefs.DevMode || !OrganizationOverlaySettings.drawRaidOrganizationInfo) return;
            Pawn pawn = comp.parent as Pawn;
            if (pawn == null || !pawn.Spawned || pawn.Map != Find.CurrentMap || pawn.Position.Fogged(pawn.Map)) return;
            CombatGroup group = comp.Group;
            if (group == null || group.Organization == null) return;
            if (RefreshFrame(comp) < 0 || Waylith.Time.FrameCount - RefreshFrame(comp) >= 30)
            {
                Refresh(comp, group);
                RefreshFrame(comp) = Waylith.Time.FrameCount;
            }

            // Begin below the normal pawn-name label. Screen-space line spacing remains
            // legible at different camera zoom levels and UI scales.
            Vector2 position = GenMapUI.LabelDrawPosFor(pawn, -0.6f);
            position.y += 20f;
            GenMapUI.DrawThingLabel(position, GroupLabel(comp), DetailColor);
            position.y += 14f;
            GenMapUI.DrawThingLabel(position, RoleLabel(comp),
                comp.ActingRole != null ? ActingColor : group.EffectiveCommander == pawn ? LeaderColor : Color.white);
            position.y += 14f;
            GenMapUI.DrawThingLabel(position, CommandLabel(comp),
                group.EffectiveCommander == null ? LostColor : DetailColor);
        }

        private static void Refresh(PawnOrganizationComponent comp, CombatGroup group)
        {
            GroupLabel(comp) = group.Organization.id + " / " + group.name;
            RoleAssignment assignment = comp.Assignment;
            string role = assignment?.combatRole?.LabelCap.ToString() ?? "-";
            if (assignment?.commandRole != null) role += " · " + assignment.commandRole.LabelCap;
            RoleDef actingRole = comp.ActingRole;
            if (actingRole != null) role += " · " + "HD_Squads_ActingRole".Translate(actingRole.LabelCap);
            RoleLabel(comp) = role;
            string commander = group.EffectiveCommander?.LabelShortCap ?? "-";
            CommandLabel(comp) = "HD_Squads_OverlayCommander".Translate(commander,
                ("HD_Squads_State_" + group.commandState).Translate());
        }
    }
}
