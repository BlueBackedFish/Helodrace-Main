using System.Linq;
using HarmonyLib;
using LudeonTK;
using Waylith.Math;
using Vector2 = System.Numerics.Vector2;
using Color = Waylith.Math.Color;
using Verse;

namespace Helodrace.Squads
{
    public static class OrganizationOverlaySettings
    {
        // Session-only, initially off, just like the game's other debug View settings.
        public static bool drawRaidOrganizationInfo;
    }

    [HarmonyPatch(typeof(DebugTabMenu_Settings), "InitActions")]
    public static class Patch_DebugSettings_OrganizationOverlay
    {
        public static void Postfix(DebugActionNode __result)
        {
            var field = AccessTools.Field(typeof(OrganizationOverlaySettings),
                nameof(OrganizationOverlaySettings.drawRaidOrganizationInfo));
            if (__result == null || __result.children.Any(child => child.settingsField == field)) return;
            DebugActionNode node = null;
            node = new DebugActionNode("HD_Squads_ShowOverlay".Translate(), DebugActionType.Action,
                () =>
                {
                    OrganizationOverlaySettings.drawRaidOrganizationInfo =
                        !OrganizationOverlaySettings.drawRaidOrganizationInfo;
                    node.DirtyLabelCache();
                })
            {
                category = "View",
                settingsField = field
            };
            __result.AddChild(node);
        }
    }

    public static class OrganizationOverlay
    {
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
            if (comp.overlayRefreshFrame < 0 || Waylith.Time.FrameCount - comp.overlayRefreshFrame >= 30)
            {
                Refresh(comp, group);
                comp.overlayRefreshFrame = Waylith.Time.FrameCount;
            }

            // Begin below the normal pawn-name label. Screen-space line spacing remains
            // legible at different camera zoom levels and UI scales.
            Vector2 position = GenMapUI.LabelDrawPosFor(pawn, -0.6f).ToNative();
            position.Y += 20f;
            GenMapUI.DrawThingLabel(position.ToGame(), comp.overlayGroupLabel, DetailColor.ToGame());
            position.Y += 14f;
            GenMapUI.DrawThingLabel(position.ToGame(), comp.overlayRoleLabel,
                (comp.ActingRole != null ? ActingColor : group.EffectiveCommander == pawn ? LeaderColor : Color.white).ToGame());
            position.Y += 14f;
            GenMapUI.DrawThingLabel(position.ToGame(), comp.overlayCommandLabel,
                (group.EffectiveCommander == null ? LostColor : DetailColor).ToGame());
        }

        private static void Refresh(PawnOrganizationComponent comp, CombatGroup group)
        {
            comp.overlayGroupLabel = group.Organization.id + " / " + group.name;
            RoleAssignment assignment = comp.Assignment;
            string role = assignment?.combatRole?.LabelCap.ToString() ?? "-";
            if (assignment?.commandRole != null) role += " · " + assignment.commandRole.LabelCap;
            RoleDef actingRole = comp.ActingRole;
            if (actingRole != null) role += " · " + "HD_Squads_ActingRole".Translate(actingRole.LabelCap);
            comp.overlayRoleLabel = role;
            string commander = group.EffectiveCommander?.LabelShortCap ?? "-";
            comp.overlayCommandLabel = "HD_Squads_OverlayCommander".Translate(commander,
                ("HD_Squads_State_" + group.commandState).Translate());
        }
    }
}
