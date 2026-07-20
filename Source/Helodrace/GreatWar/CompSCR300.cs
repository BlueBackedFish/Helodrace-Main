using System.Collections.Generic;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public static class SCR300RadioUtility
    {
        public static bool IsBlackout(Map map)
        {
            GameConditionDef solarFlare = DefDatabase<GameConditionDef>.GetNamedSilentFail("SolarFlare");
            return solarFlare != null
                && map?.gameConditionManager?.ConditionIsActive(solarFlare) == true;
        }

        public static bool HasLineOfSight(Pawn radioOperator, Map map, IntVec3 cell)
        {
            return radioOperator == null || (radioOperator.Spawned && radioOperator.Map == map
                && cell.InBounds(map) && GenSight.LineOfSight(radioOperator.Position, cell, map, true));
        }
    }

    public sealed class CompProperties_SCR300 : CompProperties
    {
        public CompProperties_SCR300()
        {
            compClass = typeof(CompSCR300);
        }
    }

    public sealed class CompSCR300 : ThingComp
    {
        private HelodForwardBase selectedBase;

        private Pawn Wearer => (parent.ParentHolder as Pawn_ApparelTracker)?.pawn;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref selectedBase, "scr300SelectedForwardBase");
        }

        public override string CompInspectStringExtra()
        {
            return selectedBase == null
                ? "HD_SCR300_SelectedBase_None".Translate().ToString()
                : "HD_SCR300_SelectedBase".Translate(
                    selectedBase.LabelCap, selectedBase.Tile.ToString()).ToString();
        }

        public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetWornGizmosExtra())
            {
                yield return gizmo;
            }

            Pawn wearer = Wearer;
            if (wearer == null || wearer.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            ValidateSelectedBase();

            Command_Action selectBase = new Command_Action
            {
                defaultLabel = "HD_SCR300_SelectBase_Label".Translate(),
                defaultDesc = "HD_SCR300_SelectBase_Desc".Translate(),
                icon = ContentFinder<Texture2D>.Get(parent.def.graphicData.texPath, true),
                action = BeginBaseSelection
            };
            if (SCR300RadioUtility.IsBlackout(wearer.Map))
            {
                selectBase.Disable("HD_SCR300_SolarFlare".Translate());
            }
            else if (!AvailableBases(wearer.Map).Any())
            {
                selectBase.Disable("HD_SCR300_NoBase".Translate());
            }
            yield return selectBase;

            Command_Action requestSupport = new Command_Action
            {
                defaultLabel = "HD_SCR300_RequestSupport_Label".Translate(),
                defaultDesc = "HD_SCR300_RequestSupport_Desc".Translate(
                    selectedBase?.LabelCap ?? "HD_SCR300_None".Translate()),
                icon = TexCommand.Attack,
                action = OpenServiceMenu
            };
            string rejection = BaseSelectionRejection(wearer.Map);
            if (rejection != null)
            {
                requestSupport.Disable(rejection);
            }
            yield return requestSupport;

            MapComponent_HelodCasSupport casComponent = wearer.Map?
                .GetComponent<MapComponent_HelodCasSupport>();
            if (casComponent?.HasActiveStrike(wearer) == true)
            {
                Command_Action cancelCas = new Command_Action
                {
                    defaultLabel = "HD_CAS_Cancel_Label".Translate(),
                    defaultDesc = "HD_CAS_Cancel_Desc".Translate(),
                    icon = TexCommand.ClearPrioritizedWork,
                    action = () => casComponent.TryCancelStrike(wearer)
                };
                if (!casComponent.CanCancelStrike(wearer, out string cancelRejection))
                {
                    cancelCas.Disable(cancelRejection);
                }
                yield return cancelCas;
            }
        }

        private void ValidateSelectedBase()
        {
            if (selectedBase == null)
            {
                return;
            }

            if (selectedBase.Destroyed || !Find.WorldObjects.AllWorldObjects.Contains(selectedBase)
                || selectedBase.ContractServices == null || selectedBase.ContractServices.Count == 0)
            {
                selectedBase = null;
            }
        }

        private IEnumerable<HelodForwardBase> AvailableBases(Map map)
        {
            if (map == null || Find.WorldObjects?.AllWorldObjects == null)
            {
                return Enumerable.Empty<HelodForwardBase>();
            }

            return Find.WorldObjects.AllWorldObjects.OfType<HelodForwardBase>()
                .Where(forwardBase => forwardBase.ContractServices != null
                    && forwardBase.ContractServices.Any(service => ServiceInRange(map, forwardBase, service)));
        }

        private void BeginBaseSelection()
        {
            Pawn wearer = Wearer;
            Map map = wearer?.Map;
            List<HelodForwardBase> available = AvailableBases(map).ToList();
            if (wearer == null || map == null || available.Count == 0)
            {
                Messages.Message("HD_SCR300_NoBase".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            if (SCR300RadioUtility.IsBlackout(map))
            {
                Messages.Message("HD_SCR300_SolarFlare".Translate(), MessageTypeDefOf.RejectInput);
                return;
            }

            CameraJumper.TryJump(new GlobalTargetInfo(selectedBase?.Tile ?? available[0].Tile));
            Find.WorldTargeter.BeginTargeting(target =>
            {
                if (SCR300RadioUtility.IsBlackout(map))
                {
                    Messages.Message("HD_SCR300_SolarFlare".Translate(), MessageTypeDefOf.RejectInput);
                    return false;
                }

                HelodForwardBase chosen = available.FirstOrDefault(forwardBase => forwardBase.Tile == target.Tile);
                if (chosen == null)
                {
                    Messages.Message("HD_SCR300_InvalidBase".Translate(), MessageTypeDefOf.RejectInput);
                    return false;
                }

                selectedBase = chosen;
                Messages.Message("HD_SCR300_BaseSelected".Translate(chosen.LabelCap), MessageTypeDefOf.PositiveEvent);
                Current.Game.CurrentMap = map;
                CameraJumper.TryJump(new TargetInfo(wearer.Position, map));
                return true;
            }, true);
        }

        private void OpenServiceMenu()
        {
            Pawn wearer = Wearer;
            Map map = wearer?.Map;
            string rejection = BaseSelectionRejection(map);
            if (rejection != null)
            {
                Messages.Message(rejection, MessageTypeDefOf.RejectInput);
                return;
            }

            List<FloatMenuOption> options = new List<FloatMenuOption>();
            foreach (HelodForwardBaseService service in selectedBase.ContractServices.Distinct())
            {
                HelodForwardBaseService localService = service;
                string label = ServiceLabel(localService);
                string serviceRejection = ServiceRejection(map, localService);
                options.Add(serviceRejection == null
                    ? new FloatMenuOption(label, () => RequestService(localService))
                    : new FloatMenuOption(label + " (" + serviceRejection + ")", null));
            }

            if (options.Count == 0)
            {
                options.Add(new FloatMenuOption("HD_SCR300_NoContractedServices".Translate(), null));
            }
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private void RequestService(HelodForwardBaseService service)
        {
            Pawn wearer = Wearer;
            Map map = wearer?.Map;
            string rejection = ServiceRejection(map, service);
            if (rejection != null)
            {
                Messages.Message(rejection, MessageTypeDefOf.RejectInput);
                return;
            }

            switch (service)
            {
                case HelodForwardBaseService.InfantrySniperSupport:
                    Find.WindowStack.Add(new Dialog_MessageBox(
                        "HD_SniperSupport_ModePrompt".Translate(),
                        "HD_SniperSupport_Suppress".Translate(),
                        () => HelodSniperSupportUtility.BeginTargeting(map, HelodSniperSupportMode.Suppress, selectedBase),
                        "HD_SniperSupport_Kill".Translate(),
                        () => HelodSniperSupportUtility.BeginTargeting(map, HelodSniperSupportMode.Kill, selectedBase)));
                    break;
                case HelodForwardBaseService.InfantryMortarSupport:
                    Find.WindowStack.Add(new Dialog_MortarAmmoSelection(map, selectedBase, null, wearer));
                    break;
                case HelodForwardBaseService.CloseAirSupport:
                    OpenCasAttackMenu(map, wearer);
                    break;
                default:
                    HelodForwardBaseDispatchUtility.TryDispatch(service, map, selectedBase, wearer);
                    break;
            }
        }

        private void OpenCasAttackMenu(Map map, Pawn wearer)
        {
            Find.WindowStack.Add(new Dialog_HelodCasControl(map, selectedBase, wearer,
                () => OpenCasGuidanceMenu(map, wearer, HelodCasAttackKind.Bombing),
                () => OpenCasGuidanceMenu(map, wearer, HelodCasAttackKind.Strafing)));
        }

        private void OpenCasGuidanceMenu(Map map, Pawn wearer,
            HelodCasAttackKind attackKind)
        {
            List<FloatMenuOption> options = new List<FloatMenuOption>
            {
                new FloatMenuOption("HD_CAS_Guidance_TalkOn".Translate(),
                    () => HelodCasSupportUtility.BeginTalkOnTargeting(map, selectedBase,
                        wearer, attackKind))
            };

            bool hasFlare = CasFlareTargetUtility.ActiveFlares(map).Any();
            options.Add(hasFlare
                ? new FloatMenuOption("HD_CAS_Guidance_Flare".Translate(),
                    () => HelodCasSupportUtility.BeginFlareTargeting(map, selectedBase,
                        wearer, attackKind))
                : new FloatMenuOption("HD_CAS_Guidance_FlareUnavailable".Translate(), null));

            // Laser guidance is intentionally implemented as a reserved enum value,
            // but remains hidden until a designator item is introduced.
            Find.WindowStack.Add(new FloatMenu(options));
        }

        private string BaseSelectionRejection(Map map)
        {
            if (SCR300RadioUtility.IsBlackout(map))
            {
                return "HD_SCR300_SolarFlare".Translate().ToString();
            }

            if (selectedBase == null)
            {
                return "HD_SCR300_SelectBase_First".Translate().ToString();
            }

            if (selectedBase.ContractServices == null || selectedBase.ContractServices.Count == 0)
            {
                return "HD_SCR300_NoContractedServices".Translate().ToString();
            }

            if (!selectedBase.ContractServices.Any(service => ServiceInRange(map, selectedBase, service)))
            {
                return "HD_SCR300_BaseOutOfRange".Translate().ToString();
            }

            return null;
        }

        private string ServiceRejection(Map map, HelodForwardBaseService service)
        {
            if (SCR300RadioUtility.IsBlackout(map))
            {
                return "HD_SCR300_SolarFlare".Translate().ToString();
            }

            if (!selectedBase.HasService(service))
            {
                return "HD_SCR300_ServiceUnavailable".Translate().ToString();
            }

            if (!ServiceInRange(map, selectedBase, service))
            {
                return "HD_SCR300_ServiceOutOfRange".Translate().ToString();
            }

            // Keep the CAS operations panel available as a status display even
            // when the base cannot accept another attack request.
            if (service != HelodForwardBaseService.CloseAirSupport
                && !selectedBase.HasServiceCapacity(service))
            {
                return "HD_SCR300_ServiceUnavailable".Translate().ToString();
            }

            return null;
        }

        private static bool ServiceInRange(Map map, HelodForwardBase forwardBase,
            HelodForwardBaseService service)
        {
            if (map == null || forwardBase == null || forwardBase.Tile < 0)
            {
                return false;
            }

            int mapTile = map.Tile >= 0 ? map.Tile : map.Parent?.Tile ?? -1;
            return mapTile >= 0 && Find.WorldGrid.ApproxDistanceInTiles(forwardBase.Tile, mapTile)
                <= HelodForwardBaseServiceUtility.SupportRange(service);
        }

        private static string ServiceLabel(HelodForwardBaseService service)
        {
            return ("HD_TelegraphTable_ForwardBase_Service_" + service).Translate().ToString();
        }
    }
}
