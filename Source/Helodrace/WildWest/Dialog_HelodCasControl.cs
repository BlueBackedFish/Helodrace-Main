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
        private readonly Action<HelodCasAircraftKind, HelodCasAttackKind> beginAttack;
        private HelodCasAircraftKind selectedAircraft = HelodCasAircraftKind.P47;

        public override Vector2 InitialSize => new Vector2(720f, 650f);

        public Dialog_HelodCasControl(Map map, HelodForwardBase forwardBase,
            Pawn caller, Action<HelodCasAircraftKind, HelodCasAttackKind> beginAttack)
        {
            this.map = map;
            this.forwardBase = forwardBase;
            this.caller = caller;
            this.beginAttack = beginAttack;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = false;
        }

        public override void DoWindowContents(Rect inRect)
        {
            MapComponent_HelodCasSupport support = map?
                .GetComponent<MapComponent_HelodCasSupport>();
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f),
                "HD_CAS_Control_Title".Translate());
            Text.Font = GameFont.Small;
            float y = inRect.y + 42f;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f),
                "HD_CAS_Control_Base".Translate(
                    forwardBase?.LabelCap ?? "HD_SCR300_None".Translate()));
            y += 32f;

            float tabGap = 10f;
            float tabWidth = (inRect.width - tabGap) * 0.5f;
            DrawAircraftTab(new Rect(inRect.x, y, tabWidth, 36f),
                HelodCasAircraftKind.P47);
            DrawAircraftTab(new Rect(inRect.x + tabWidth + tabGap, y,
                tabWidth, 36f), HelodCasAircraftKind.A10C);
            y += 48f;

            bool flightRequested = false;
            int remainingPlaytime = 0;
            int reservedAircraftCount = 0;
            support?.GetPlaytimeStatus(forwardBase, selectedAircraft,
                out flightRequested, out remainingPlaytime, out reservedAircraftCount);
            Rect statusRect = new Rect(inRect.x, y, inRect.width, 122f);
            Widgets.DrawMenuSection(statusRect);
            Rect statusInner = statusRect.ContractedBy(14f);
            bool activeFlight = support != null && remainingPlaytime > 0;
            string aircraftLabel = HelodCasSupportUtility.AircraftLabel(selectedAircraft);
            string statusKey = activeFlight ? "HD_CAS_Control_StatusActiveAircraft"
                : flightRequested ? "HD_CAS_Control_StatusExhaustedAircraft"
                : "HD_CAS_Control_StatusNotRequestedAircraft";
            Widgets.Label(new Rect(statusInner.x, statusInner.y,
                statusInner.width, 24f), statusKey.Translate(aircraftLabel));
            Widgets.Label(new Rect(statusInner.x, statusInner.y + 31f,
                statusInner.width, 22f), "HD_CAS_Control_Playtime".Translate());
            Rect barRect = new Rect(statusInner.x, statusInner.y + 57f,
                statusInner.width, 22f);
            DrawPlaytimeBar(barRect, remainingPlaytime
                / (float)HelodCasSupportUtility.Playtime(selectedAircraft), aircraftLabel);
            if (reservedAircraftCount > 0)
            {
                Widgets.Label(new Rect(statusInner.x, statusInner.y + 86f,
                    statusInner.width, 22f),
                    "HD_CAS_Control_ReservedAircraft".Translate(reservedAircraftCount));
            }
            y = statusRect.yMax + 12f;

            bool blackout = SCR300RadioUtility.IsBlackout(map);
            bool hasServiceCapacity = forwardBase?.HasServiceCapacity(
                HelodForwardBaseService.CloseAirSupport) == true;
            bool canRequest = support?.CanRequestFlight(forwardBase, selectedAircraft)
                == true && hasServiceCapacity && !blackout;
            GUI.color = canRequest ? Color.white : Color.gray;
            if (Widgets.ButtonText(new Rect(inRect.x, y, inRect.width, 34f),
                "HD_CAS_Control_RequestAircraft".Translate(aircraftLabel)) && canRequest)
            {
                if (support.TryRequestFlight(forwardBase, selectedAircraft,
                    Find.TickManager.TicksGame))
                {
                    Messages.Message("HD_CAS_FlightRequestedAircraft".Translate(
                        forwardBase.LabelCap, aircraftLabel), caller,
                        MessageTypeDefOf.PositiveEvent);
                }
            }
            GUI.color = Color.white;
            y += 46f;

            bool activeStrike = support?.HasActiveStrike(caller) == true;
            bool canAttack = activeFlight && !activeStrike && hasServiceCapacity
                && !blackout;
            DrawLoadouts(new Rect(inRect.x, y, inRect.width,
                inRect.yMax - y - 28f), canAttack);

            string footer = blackout ? "HD_SCR300_SolarFlare".Translate().ToString()
                : activeStrike ? "HD_CAS_Control_MissionActive".Translate().ToString()
                : !hasServiceCapacity ? "HD_CAS_Control_NoCapacity".Translate().ToString()
                : null;
            if (footer != null)
            {
                Widgets.Label(new Rect(inRect.x, inRect.yMax - 22f,
                    inRect.width, 22f), footer);
            }
        }

        private void DrawAircraftTab(Rect rect, HelodCasAircraftKind aircraftKind)
        {
            GUI.color = selectedAircraft == aircraftKind ? Color.white : Color.gray;
            if (Widgets.ButtonText(rect, HelodCasSupportUtility.AircraftLabel(aircraftKind)))
            {
                selectedAircraft = aircraftKind;
            }
            GUI.color = Color.white;
        }

        private void DrawLoadouts(Rect area, bool enabled)
        {
            if (selectedAircraft == HelodCasAircraftKind.P47)
            {
                float gap = 12f;
                float width = (area.width - gap) * 0.5f;
                DrawAttackCard(new Rect(area.x, area.y, width, 110f),
                    HelodCasAttackKind.Bombing, "HD_CAS_Attack_Bombing",
                    "HD_CAS_Control_BombingDesc", enabled);
                DrawAttackCard(new Rect(area.x + width + gap, area.y, width, 110f),
                    HelodCasAttackKind.Strafing, "HD_CAS_Attack_Strafing",
                    "HD_CAS_Control_StrafingDesc", enabled);
                return;
            }
            HelodCasAttackKind[] kinds =
            {
                HelodCasAttackKind.Strafing, HelodCasAttackKind.Hydra70,
                HelodCasAttackKind.AGR20A, HelodCasAttackKind.Maverick,
                HelodCasAttackKind.GBU31, HelodCasAttackKind.GBU54
            };
            const float a10Gap = 10f;
            float a10Width = (area.width - a10Gap) * 0.5f;
            const float height = 82f;
            for (int i = 0; i < kinds.Length; i++)
            {
                int row = i / 2;
                int column = i % 2;
                string suffix = kinds[i].ToString();
                DrawAttackCard(new Rect(area.x + column * (a10Width + a10Gap),
                    area.y + row * (height + a10Gap), a10Width, height), kinds[i],
                    "HD_CAS_Attack_" + suffix, "HD_CAS_Desc_" + suffix, enabled);
            }
        }

        private void DrawAttackCard(Rect rect, HelodCasAttackKind attackKind,
            string titleKey, string descriptionKey, bool enabled)
        {
            MapComponent_HelodCasSupport support = map?
                .GetComponent<MapComponent_HelodCasSupport>();
            int ammoRemaining = support?.GetAmmoRemaining(forwardBase,
                selectedAircraft, attackKind) ?? 0;
            int munitionCount = attackKind == HelodCasAttackKind.Strafing
                || attackKind == HelodCasAttackKind.AGR20A
                ? 1 : HelodCasSupportUtility.MunitionCount(attackKind);
            bool hasAmmo = support?.HasAmmoForAttack(forwardBase, selectedAircraft,
                attackKind, munitionCount, out _) == true;
            bool cardEnabled = enabled && hasAmmo;
            Widgets.DrawMenuSection(rect);
            Rect inner = rect.ContractedBy(8f);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(inner.x, inner.y, inner.width, 22f),
                "HD_CAS_AmmoCount".Translate(titleKey.Translate(), ammoRemaining));
            GUI.color = Color.gray;
            Widgets.Label(new Rect(inner.x, inner.y + 22f, inner.width - 82f,
                inner.height - 22f), descriptionKey.Translate());
            GUI.color = cardEnabled ? Color.white : Color.gray;
            if (Widgets.ButtonText(new Rect(inner.xMax - 76f, inner.yMax - 30f,
                76f, 30f), "HD_CAS_Control_Select".Translate()) && cardEnabled)
            {
                Close(false);
                beginAttack?.Invoke(selectedAircraft, attackKind);
            }
            GUI.color = Color.white;
        }

        private static void DrawPlaytimeBar(Rect rect, float fraction,
            string aircraftLabel)
        {
            fraction = Mathf.Clamp01(fraction);
            Widgets.DrawBoxSolid(rect, new Color(0.12f, 0.12f, 0.12f));
            Rect fill = rect.ContractedBy(2f);
            fill.width *= fraction;
            Color fillColor = Color.Lerp(new Color(0.75f, 0.18f, 0.12f),
                new Color(0.18f, 0.72f, 0.26f), fraction);
            Widgets.DrawBoxSolid(fill, fillColor);
            Widgets.DrawBox(rect);
            TooltipHandler.TipRegion(rect,
                "HD_CAS_Control_PlaytimeTooltipAircraft".Translate(aircraftLabel));
        }
    }
}
