using System;
using RimWorld;
using UnityEngine;
using Verse;
using Rect = Waylith.Math.Rect;
using Color = Waylith.Math.Color;

namespace Helodrace
{
    public sealed class Dialog_HelodSniperControl : Window
    {
        private readonly Map map;
        private readonly HelodForwardBase forwardBase;
        private readonly CompTelegraphTable telegraphComp;
        private readonly Pawn caller;
        private HelodSniperSupportDirection selectedDirection;

        public override Vector2 InitialSize => new Vector2(640f, 470f);

        public Dialog_HelodSniperControl(Map map, HelodForwardBase forwardBase,
            CompTelegraphTable telegraphComp = null, Pawn caller = null)
        {
            this.map = map;
            this.forwardBase = forwardBase;
            this.telegraphComp = telegraphComp;
            this.caller = caller;
            selectedDirection = map.GetComponent<MapComponent_HelodSniperSupport>()
                .GetSession(forwardBase)?.Direction ?? HelodSniperSupportDirection.North;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public override void DoWindowContents(UnityEngine.Rect inRect)
        {
            Rect rect = inRect.ToNative();
            MapComponent_HelodSniperSupport support = map.GetComponent<MapComponent_HelodSniperSupport>();
            HelodSniperSupportSession session = support.GetSession(forwardBase);
            int now = Find.TickManager.TicksGame;
            bool active = session?.CanRequestShot(now) == true;
            bool busy = support.HasActiveStrike(forwardBase);
            string rejection = HelodSniperSupportUtility.RequestRejection(map, forwardBase, telegraphComp, caller);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 32f).ToGame(), "HD_SniperSupport_ControlTitle".Translate());
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x, rect.y + 42f, rect.width, 25f).ToGame(),
                "HD_CAS_Control_Base".Translate(forwardBase.LabelCap));
            Widgets.Label(new Rect(rect.x, rect.y + 76f, rect.width, 46f).ToGame(),
                "HD_SniperSupport_SetupDesc".Translate(HelodSniperSupportSession.DurationTicks / 2500,
                    HelodSniperSupportSession.MaxActions));

            float width = (rect.width - 24f) / 4f;
            foreach (HelodSniperSupportDirection direction in Enum.GetValues(typeof(HelodSniperSupportDirection)))
            {
                bool selected = (active || busy ? session.Direction : selectedDirection) == direction;
                GUI.color = (selected ? Color.white : Color.gray).ToGame();
                if (Widgets.ButtonText(new Rect(rect.x + (int)direction * (width + 8f), rect.y + 132f,
                    width, 34f).ToGame(), HelodSniperSupportUtility.DirectionLabel(direction)) && !active && !busy)
                    selectedDirection = direction;
            }
            GUI.color = Color.white.ToGame();

            Rect status = new Rect(rect.x, rect.y + 180f, rect.width, 78f);
            Widgets.DrawMenuSection(status.ToGame());
            Widgets.Label(UiMath.Inset(status, 10f).ToGame(), session == null
                ? "HD_SniperSupport_NotRequested".Translate()
                : "HD_SniperSupport_Status".Translate(HelodSniperSupportUtility.DirectionLabel(session.Direction),
                    session.RemainingTicks(now).ToStringTicksToPeriod(), session.RemainingActions,
                    HelodSniperSupportSession.MaxActions));

            bool canDeploy = !active && !busy && rejection == null;
            GUI.color = (canDeploy ? Color.white : Color.gray).ToGame();
            if (Widgets.ButtonText(new Rect(rect.x, rect.y + 270f, rect.width, 34f).ToGame(),
                "HD_SniperSupport_Request".Translate()) && canDeploy)
            {
                if (support.TryRequestSupport(forwardBase, selectedDirection, telegraphComp, caller))
                    Messages.Message("HD_SniperSupport_Ready".Translate(
                        HelodSniperSupportUtility.DirectionLabel(selectedDirection)), MessageTypeDefOf.PositiveEvent);
            }
            GUI.color = Color.white.ToGame();

            bool canAttack = active && !busy && rejection == null;
            DrawAttack(new Rect(rect.x, rect.y + 318f, (rect.width - 12f) / 2f, 40f),
                HelodSniperSupportMode.Suppress, "HD_SniperSupport_Suppress", canAttack);
            DrawAttack(new Rect(rect.x + (rect.width + 12f) / 2f, rect.y + 318f,
                (rect.width - 12f) / 2f, 40f), HelodSniperSupportMode.Kill, "HD_SniperSupport_Kill", canAttack);
            Widgets.Label(new Rect(rect.x, rect.y + 374f, rect.width, 50f).ToGame(), rejection
                ?? (busy ? "HD_SniperSupport_Busy".Translate().ToString()
                    : "HD_SniperSupport_TargetHint".Translate().ToString()));
        }

        private void DrawAttack(Rect rect, HelodSniperSupportMode mode, string key, bool enabled)
        {
            GUI.color = (enabled ? Color.white : Color.gray).ToGame();
            if (Widgets.ButtonText(rect.ToGame(), key.Translate()) && enabled)
            {
                Close(false);
                HelodSniperSupportUtility.BeginTargeting(map, mode, forwardBase, telegraphComp, caller);
            }
            GUI.color = Color.white.ToGame();
        }
    }
}
