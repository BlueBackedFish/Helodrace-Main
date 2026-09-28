using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace LGModularWeapons
{
    // A queued modification: which pawn, which weapon, what it should become, what it costs.
    // Held by the bench until the pawn walks over, hauls the materials and does the work.
    public class ModOrder : IExposable
    {
        public Pawn pawn;
        public ThingWithComps weapon;
        public Dictionary<WeaponPartSlotDef, WeaponPartDef> config =
            new Dictionary<WeaponPartSlotDef, WeaponPartDef>();
        public List<ThingDefCountClass> cost = new List<ThingDefCountClass>();

        // Salvage from parts being taken off, spawned at the bench when the work finishes.
        public List<ThingDefCountClass> refund = new List<ThingDefCountClass>();
        public float workAmount = 600f;

        private List<WeaponPartSlotDef> scribeSlots;
        private List<WeaponPartDef> scribeParts;

        public bool StillValid =>
            pawn != null && !pawn.Dead && weapon != null && !weapon.Destroyed
            && pawn.equipment?.Primary == weapon;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_References.Look(ref weapon, "weapon");
            Scribe_Values.Look(ref workAmount, "workAmount", 600f);
            Scribe_Collections.Look(ref cost, "cost", LookMode.Deep);
            Scribe_Collections.Look(ref refund, "refund", LookMode.Deep);
            Scribe_Collections.Look(ref config, "config",
                LookMode.Def, LookMode.Def, ref scribeSlots, ref scribeParts);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (config == null) config = new Dictionary<WeaponPartSlotDef, WeaponPartDef>();
                if (cost == null) cost = new List<ThingDefCountClass>();
                if (refund == null) refund = new List<ThingDefCountClass>();
            }
        }
    }

    public class CompProperties_ModularBench : CompProperties
    {
        // Work per changed part. Multiplied by the number of parts being swapped.
        public float workPerPart = 600f;

        public CompProperties_ModularBench()
        {
            compClass = typeof(CompModularBench);
        }
    }

    // Put this on any workbench ThingDef to turn it into a weapon modification station.
    public class CompModularBench : ThingComp
    {
        private List<ModOrder> orders = new List<ModOrder>();

        public CompProperties_ModularBench Props => (CompProperties_ModularBench)props;

        public List<ModOrder> Orders => orders;

        public ModOrder OrderFor(Pawn pawn)
        {
            for (int i = 0; i < orders.Count; i++)
                if (orders[i].pawn == pawn) return orders[i];
            return null;
        }

        public void AddOrder(ModOrder order)
        {
            // One pending order per pawn - a second one replaces the first rather than
            // queueing, since both would target the same weapon anyway.
            ModOrder existing = OrderFor(order.pawn);
            if (existing != null) orders.Remove(existing);
            orders.Add(order);
        }

        public void RemoveOrder(ModOrder order)
        {
            orders.Remove(order);
        }

        public override void CompTick()
        {
            base.CompTick();
            // Cheap housekeeping: drop orders whose pawn died, dropped the weapon, etc.
            if (parent.IsHashIntervalTick(250))
            {
                for (int i = orders.Count - 1; i >= 0; i--)
                {
                    if (orders[i].StillValid) continue;

                    // Tell the player instead of silently dropping the job.
                    Messages.Message("LGMW_OrderCancelled".Translate(
                        orders[i].pawn?.LabelShortCap ?? "?"),
                        parent, MessageTypeDefOf.NegativeEvent, false);
                    orders.RemoveAt(i);
                }
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra()) yield return g;

            yield return new Command_Action
            {
                defaultLabel = "LGMW_ModifyWeapon".Translate(),
                defaultDesc = "LGMW_ModifyWeaponDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("Icon/LGWeaponModIcon", false),
                action = OpenPawnPicker
            };
        }

        // A pick-from-list window rather than map targeting: only colonists carrying a
        // modular weapon are listed, so an invalid choice is impossible instead of merely
        // being rejected by the cursor.
        private void OpenPawnPicker()
        {
            List<Pawn> candidates = new List<Pawn>();
            foreach (Pawn p in parent.Map.mapPawns.FreeColonistsAndPrisonersSpawned)
                if (IsValidCandidate(p)) candidates.Add(p);

            if (candidates.Count == 0)
            {
                Messages.Message("LGMW_NoCandidates".Translate(),
                    parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            Find.WindowStack.Add(new Window_PickModWorker(this, candidates));
        }

        public void OpenFor(Pawn p)
        {
            string reason;
            if (!CanAcceptOrder(p, out reason))
            {
                Messages.Message(reason, parent, MessageTypeDefOf.RejectInput, false);
                return;
            }

            CompWeaponModular weaponComp = p.equipment.Primary.GetComp<CompWeaponModular>();
            Find.WindowStack.Add(new Window_ModularWeapon(weaponComp, this, p));
        }

        // Everything that can block an order, each with its own message so the player is
        // never left guessing why nothing happened.
        public bool CanAcceptOrder(Pawn p, out string reason)
        {
            reason = null;

            if (p == null || p.Dead)
            {
                reason = "LGMW_Fail_NoPawn".Translate();
                return false;
            }

            ThingWithComps eq = p.equipment?.Primary;
            if (eq == null || eq.GetComp<CompWeaponModular>() == null)
            {
                reason = "LGMW_Fail_NoModularWeapon".Translate(p.LabelShortCap);
                return false;
            }

            if (!PowerOn)
            {
                reason = "LGMW_Fail_NoPower".Translate();
                return false;
            }

            if (parent.IsBrokenDown())
            {
                reason = "LGMW_Fail_BrokenDown".Translate();
                return false;
            }

            if (p.Downed || p.InMentalState)
            {
                reason = "LGMW_Fail_Incapacitated".Translate(p.LabelShortCap);
                return false;
            }

            // The pawn has to be physically able to do bench work at all.
            if (!p.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            {
                reason = "LGMW_Fail_NoManipulation".Translate(p.LabelShortCap);
                return false;
            }

            if (p.WorkTagIsDisabled(WorkTags.ManualSkilled))
            {
                reason = "LGMW_Fail_NoManualSkilled".Translate(p.LabelShortCap);
                return false;
            }

            // Crafting permanently disabled by traits/backstory is different from simply
            // being unassigned - the player can fix the second one, so say which it is.
            if (p.WorkTypeIsDisabled(WorkTypeDefOf.Crafting))
            {
                reason = "LGMW_Fail_CraftingIncapable".Translate(p.LabelShortCap);
                return false;
            }

            if (p.workSettings == null || !p.workSettings.EverWork
                || p.workSettings.GetPriority(WorkTypeDefOf.Crafting) == 0)
            {
                reason = "LGMW_Fail_CraftingUnassigned".Translate(p.LabelShortCap);
                return false;
            }

            if (!p.CanReach(parent, PathEndMode.InteractionCell, Danger.Deadly))
            {
                reason = "LGMW_Fail_Unreachable".Translate(p.LabelShortCap);
                return false;
            }

            return true;
        }

        public bool PowerOn
        {
            get
            {
                CompPowerTrader power = parent.TryGetComp<CompPowerTrader>();
                return power == null || power.PowerOn;   // no power comp = never blocked
            }
        }

        // Only colonists carrying a modular weapon can be picked.
        private bool IsValidCandidate(Pawn p)
        {
            if (p == null || p.Dead || p.Map != parent.Map) return false;
            if (!p.IsFreeColonist && !p.IsSlaveOfColony) return false;

            ThingWithComps eq = p.equipment?.Primary;
            return eq != null && eq.GetComp<CompWeaponModular>() != null;
        }


        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref orders, "orders", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit && orders == null)
                orders = new List<ModOrder>();
        }

        public override string CompInspectStringExtra()
        {
            if (orders.Count == 0) return null;
            return "LGMW_PendingOrders".Translate(orders.Count);
        }
    }
}