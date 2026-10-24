using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Helodrace
{
    public class CompProperties_TelegraphTable : CompProperties
    {
        public bool wireless;

        public CompProperties_TelegraphTable()
        {
            compClass = typeof(CompTelegraphTable);
        }
    }

    public class CompTelegraphTable : ThingComp
    {
        private const string UseJobDefName = "HD_UseTelegraphTable";

        private CompRefuelable Refuelable => parent.TryGetComp<CompRefuelable>();

        public bool IsWireless => ((CompProperties_TelegraphTable)props).wireless;
        public int StoredPrimaryCells => Mathf.FloorToInt(Refuelable?.Fuel ?? 0f);
        public MapComponent_TelegraphNetwork Network => parent.Map?.GetComponent<MapComponent_TelegraphNetwork>();
        public Faction Provider => IsWireless
            ? Find.FactionManager.AllFactionsVisibleInViewOrder.FirstOrDefault(f => !f.defeated
                && TelegraphNetworkRules.IsProvider(f.def?.defName))
            : Network?.Provider;

        public bool HasTransmissionCapacity(int count = 1) => IsWireless || StoredPrimaryCells >= count;
        public string TransmissionCostLabel => (IsWireless ? "HD_Radio_TransmissionCost" : "HD_Company_TransmissionCost").Translate();

        public string ConnectionFailure
        {
            get
            {
                if (IsWireless)
                {
                    if (!(parent is Building_Radio radio) || !radio.CanUseNow) return "HD_Radio_NoPower".Translate();
                    return Provider == null ? "HD_Radio_NoCompanyFrequency".Translate().ToString() : null;
                }
                if (!parent.Spawned || parent.Faction != Faction.OfPlayer || Network == null)
                    return "HD_Telegraph_Status_ProviderUnavailable".Translate();
                TelegraphConnectionStatus status = Network.Status(HasTransmissionCapacity());
                return status == TelegraphConnectionStatus.Online ? null : Network.StatusText(status);
            }
        }

        public string TransmissionFailure(Faction target, bool allowHostileTrade = false)
        {
            string reason = ConnectionFailure;
            if (reason != null) return reason;
            if (!allowHostileTrade && target?.HostileTo(Faction.OfPlayer) == true)
                return "HD_Company_HostileServiceUnavailable".Translate();
            return target != Provider ? (IsWireless ? "HD_Radio_WrongProvider" : "HD_Telegraph_WrongProvider").Translate().ToString() : null;
        }

        public override string CompInspectStringExtra()
        {
            return ConnectionFailure ?? (IsWireless ? "HD_Radio_Online" : "HD_Telegraph_Status_Online").Translate().ToString();
        }

        public bool TryTransmit()
        {
            if (IsWireless) return ConnectionFailure == null;
            CompRefuelable refuelable = Refuelable;
            if (ConnectionFailure != null || refuelable == null || refuelable.Fuel < 1f)
            {
                return false;
            }

            refuelable.ConsumeFuel(1f);
            return true;
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn selPawn)
        {
            foreach (FloatMenuOption option in base.CompFloatMenuOptions(selPawn))
            {
                yield return option;
            }

            if (selPawn == null || selPawn.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            string label = (IsWireless ? "HD_Radio_Use" : "HD_TelegraphTable_Use_Label").Translate(parent.LabelShort);
            if (IsWireless ? ConnectionFailure != null
                : Network?.InstallationRequested != true || Network.Status() == TelegraphConnectionStatus.Installing)
            {
                yield return new FloatMenuOption(label + ": " + ConnectionFailure, null);
                yield break;
            }
            if (!selPawn.CanReach(parent, PathEndMode.InteractionCell, Danger.Deadly))
            {
                yield return new FloatMenuOption(label + ": " + "NoPath".Translate(), null);
                yield break;
            }

            if (!selPawn.CanReserve(parent))
            {
                yield return new FloatMenuOption(label + ": " + "Reserved".Translate(), null);
                yield break;
            }

            yield return new FloatMenuOption(label, delegate
            {
                JobDef jobDef = DefDatabase<JobDef>.GetNamedSilentFail(UseJobDefName);
                if (jobDef == null)
                {
                    Log.ErrorOnce("Helodrace: HD_UseTelegraphTable JobDef is missing.", 72851041);
                    return;
                }

                Job job = JobMaker.MakeJob(jobDef, parent);
                selPawn.jobs.TryTakeOrderedJob(job);
            });
        }
    }

}
