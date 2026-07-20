using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public sealed class Dialog_HelodCasControl : Window
    {
        private readonly Map map;
        private readonly HelodForwardBase forwardBase;
        private readonly Pawn caller;
        private readonly Action beginBombing;
        private readonly Action beginStrafing;

        public override Vector2 InitialSize => new Vector2(680f, 470f);

        public Dialog_HelodCasControl(Map map, HelodForwardBase forwardBase,
            Pawn caller, Action beginBombing, Action beginStrafing)
        {
            this.map = map;
            this.forwardBase = forwardBase;
            this.caller = caller;
            this.beginBombing = beginBombing;
            this.beginStrafing = beginStrafing;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            MapComponent_HelodCasSupport support = map?
                .GetComponent<MapComponent_HelodCasSupport>();
            bool flightRequested = false;
            int remainingPlaytime = 0;
            int reservedAircraftCount = 0;
            support?.GetPlaytimeStatus(forwardBase, out flightRequested,
                out remainingPlaytime, out reservedAircraftCount);

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f),
                "HD_CAS_Control_Title".Translate());
            Text.Font = GameFont.Small;
            float y = inRect.y + 44f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f),
                "HD_CAS_Control_Base".Translate(
                    forwardBase?.LabelCap ?? "HD_SCR300_None".Translate()));
            y += 32f;

            Rect statusRect = new Rect(inRect.x, y, inRect.width, 132f);
            Widgets.DrawMenuSection(statusRect);
            Rect statusInner = statusRect.ContractedBy(14f);
            bool activeFlight = support != null && remainingPlaytime > 0;
            string statusKey = activeFlight ? "HD_CAS_Control_StatusActive"
                : flightRequested ? "HD_CAS_Control_StatusExhausted"
                : "HD_CAS_Control_StatusNotRequested";
            Widgets.Label(new Rect(statusInner.x, statusInner.y,
                statusInner.width, 24f), statusKey.Translate());
            Widgets.Label(new Rect(statusInner.x, statusInner.y + 34f,
                statusInner.width, 22f), "HD_CAS_Control_Playtime".Translate());
            Rect barRect = new Rect(statusInner.x, statusInner.y + 62f,
                statusInner.width, 24f);
            DrawPlaytimeBar(barRect, remainingPlaytime
                / (float)HelodCasSupportUtility.P47Playtime);
            if (reservedAircraftCount > 0)
            {
                Widgets.Label(new Rect(statusInner.x, statusInner.y + 94f,
                    statusInner.width, 22f),
                    "HD_CAS_Control_ReservedAircraft".Translate(
                        reservedAircraftCount));
            }
            y = statusRect.yMax + 18f;

            bool blackout = SCR300RadioUtility.IsBlackout(map);
            bool hasServiceCapacity = forwardBase?.HasServiceCapacity(
                HelodForwardBaseService.CloseAirSupport) == true;
            bool canRequest = support?.CanRequestFlight(forwardBase) == true
                && hasServiceCapacity && !blackout;
            GUI.color = canRequest ? Color.white : Color.gray;
            if (Widgets.ButtonText(new Rect(inRect.x, y, inRect.width, 38f),
                "HD_CAS_Control_RequestFlight".Translate()) && canRequest)
            {
                if (support.TryRequestFlight(forwardBase,
                    Find.TickManager.TicksGame))
                {
                    Messages.Message("HD_CAS_FlightRequested".Translate(
                        forwardBase.LabelCap), caller,
                        MessageTypeDefOf.PositiveEvent);
                }
            }
            GUI.color = Color.white;
            y += 54f;

            bool activeStrike = support?.HasActiveStrike(caller) == true;
            bool canAttack = activeFlight && !activeStrike && hasServiceCapacity
                && !blackout;
            float gap = 12f;
            float cardWidth = (inRect.width - gap) * 0.5f;
            DrawAttackCard(new Rect(inRect.x, y, cardWidth, 112f),
                "HD_CAS_Attack_Bombing".Translate(),
                "HD_CAS_Control_BombingDesc".Translate(), canAttack, beginBombing);
            DrawAttackCard(new Rect(inRect.x + cardWidth + gap, y,
                cardWidth, 112f), "HD_CAS_Attack_Strafing".Translate(),
                "HD_CAS_Control_StrafingDesc".Translate(), canAttack, beginStrafing);

            if (blackout)
            {
                Widgets.Label(new Rect(inRect.x, inRect.yMax - 24f,
                    inRect.width, 22f), "HD_SCR300_SolarFlare".Translate());
            }
            else if (activeStrike)
            {
                Widgets.Label(new Rect(inRect.x, inRect.yMax - 24f,
                    inRect.width, 22f), "HD_CAS_Control_MissionActive".Translate());
            }
            else if (!hasServiceCapacity)
            {
                Widgets.Label(new Rect(inRect.x, inRect.yMax - 24f,
                    inRect.width, 22f), "HD_CAS_Control_NoCapacity".Translate());
            }
        }

        private void DrawAttackCard(Rect rect, string title, string description,
            bool enabled, Action action)
        {
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(10f);
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 26f), title);
            Text.Font = GameFont.Small;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(inner.x, inner.y + 30f, inner.width, 32f),
                description);
            GUI.color = enabled ? Color.white : Color.gray;
            if (Widgets.ButtonText(new Rect(inner.x, inner.yMax - 30f,
                inner.width, 30f), "HD_CAS_Control_Select".Translate()) && enabled)
            {
                Close(false);
                action?.Invoke();
            }
            GUI.color = Color.white;
        }

        private static void DrawPlaytimeBar(Rect rect, float fraction)
        {
            fraction = Mathf.Clamp01(fraction);
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.12f));
            Rect fill = rect.ContractedBy(2f);
            fill.width *= fraction;
            Color fillColor = Color.Lerp(new Color(0.75f, 0.18f, 0.12f),
                new Color(0.18f, 0.72f, 0.26f), fraction);
            Widgets.DrawBoxSolid(fill, fillColor);
            Widgets.DrawBox(rect);
            TooltipHandler.TipRegion(rect, "HD_CAS_Control_PlaytimeTooltip".Translate());
        }
    }
}
