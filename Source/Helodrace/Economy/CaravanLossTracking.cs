using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI.Group;

namespace Helodrace.Economy
{
    public sealed class CaravanLossFrame : IDisposable
    {
        [ThreadStatic] public static CaravanLossFrame Current;
        private readonly CaravanLossFrame previous;
        private readonly GameComponent_CaravanLosses ledger;
        public readonly Pawn Pawn;
        public readonly CaravanPawnLoss Victim;
        private readonly float beforeHealth;
        private readonly double injuryBefore;
        private readonly Dictionary<Thing, int> remaining = new Dictionary<Thing, int>();
        private readonly Dictionary<Hediff, float> wounds = new Dictionary<Hediff, float>();
        private bool completed, disposed;

        private CaravanLossFrame(Pawn pawn, CaravanPawnLoss victim, GameComponent_CaravanLosses ledger)
        {
            Pawn = pawn; Victim = victim; this.ledger = ledger;
            beforeHealth = pawn.health.summaryHealth.SummaryHealthPercent;
            injuryBefore = victim.InjuryValue;
            foreach (Thing thing in OwnedThings(pawn)) if (!(thing is Pawn)) remaining[thing] = thing.stackCount;
            foreach (Hediff hediff in pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>()) wounds[hediff] = hediff.Severity;
            previous = Current; Current = this;
        }

        public static CaravanLossFrame Begin(Pawn pawn, DamageInfo? damage, Hediff culprit = null)
        {
            var ledger = GameComponent_CaravanLosses.Current;
            if (pawn == null || pawn.Dead || pawn.MapHeld == null || ledger == null || !HelodMoneyTrading.IsHelodFaction(pawn.Faction)) return null;
            bool playerAttack = damage.HasValue && damage.Value.Amount > 0f
                && damage.Value.Instigator?.Faction == Faction.OfPlayer && damage.Value.Def?.ExternalViolenceFor(pawn) == true;
            CaravanPawnLoss victim = null;
            if (!playerAttack)
            {
                if (damage.HasValue) return null;
                victim = ledger.LatestVictim(pawn.GetUniqueLoadID());
                if (victim == null || victim.Dead || victim.Departed || victim.MapId != pawn.MapHeld.uniqueID || culprit == null) return null;
                bool playerWound = victim.PlayerWounds.Contains(culprit.GetUniqueLoadID());
                bool playerBleeding = culprit.def == HediffDefOf.BloodLoss
                    && pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>()
                        .Any(h => h.Bleeding && victim.PlayerWounds.Contains(h.GetUniqueLoadID()));
                if (!playerWound && !playerBleeding) return null;
            }
            else
            {
                Lord lord = pawn.GetLord();
                if (lord?.LordJob is LordJob_TradeWithColony)
                    victim = ledger.GetOrCreate(pawn.Faction, "Lord_" + lord.loadID, pawn.MapHeld.Parent.LabelCap, pawn.MapHeld.uniqueID,
                        pawn.GetUniqueLoadID(), pawn.LabelShortCap, pawn.MarketValue, pawn.health.summaryHealth.SummaryHealthPercent);
                else
                {
                    // Downing can remove a pawn from its lord before a later finishing blow.
                    victim = ledger.LatestVictim(pawn.GetUniqueLoadID());
                    if (victim == null || victim.Dead || victim.Departed || victim.Faction != pawn.Faction || victim.MapId != pawn.MapHeld.uniqueID) return null;
                }
            }
            return new CaravanLossFrame(pawn, victim, ledger);
        }

        private static IEnumerable<Thing> OwnedThings(Pawn pawn)
        {
            if (pawn.inventory != null) foreach (Thing thing in pawn.inventory.innerContainer) yield return thing;
            if (pawn.equipment != null) foreach (Thing thing in pawn.equipment.AllEquipmentListForReading) yield return thing;
            if (pawn.apparel != null) foreach (Thing thing in pawn.apparel.WornApparel) yield return thing;
            if (pawn.carryTracker?.CarriedThing != null) yield return pawn.carryTracker.CarriedThing;
        }

        public Action<Thing, int> WatchDrop(Thing original, Action<Thing, int> existing)
        {
            if (!remaining.ContainsKey(original)) return existing;
            string name = original.LabelNoCount, defName = original.def.defName;
            double unitValue = original.MarketValue;
            return (placed, count) =>
            {
                int lost = Math.Min(Math.Max(0, count), remaining[original]);
                remaining[original] -= lost;
                ledger.RecordDrop(Victim, defName, name, lost, unitValue);
                existing?.Invoke(placed, count);
            };
        }

        public void Complete()
        {
            if (completed) return;
            completed = true;
            if (Pawn.Dead) ledger.RecordDeath(Victim);
            else
            {
                ledger.RecordInjury(Victim, beforeHealth, Pawn.health.summaryHealth.SummaryHealthPercent, injuryBefore);
                foreach (Hediff hediff in Pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>())
                    if ((!wounds.TryGetValue(hediff, out float severity) || hediff.Severity > severity)
                        && !Victim.PlayerWounds.Contains(hediff.GetUniqueLoadID())) Victim.PlayerWounds.Add(hediff.GetUniqueLoadID());
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Current = previous;
        }
    }

    [HarmonyPatch(typeof(Thing), nameof(Thing.TakeDamage))]
    public static class Patch_CaravanLossDamage
    {
        [HarmonyPrefix]
        public static void Prefix(Thing __instance, [HarmonyArgument(0)] DamageInfo dinfo, out CaravanLossFrame __state)
            => __state = CaravanLossFrame.Begin(__instance as Pawn, dinfo);
        [HarmonyPostfix]
        public static void Postfix(CaravanLossFrame __state)
        {
            try { __state?.Complete(); }
            finally { __state?.Dispose(); }
        }
        [HarmonyFinalizer]
        public static void Finalizer(CaravanLossFrame __state) => __state?.Dispose();
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_CaravanLossDeath
    {
        [HarmonyPrefix]
        public static void Prefix(Pawn __instance, [HarmonyArgument(0)] DamageInfo? dinfo, [HarmonyArgument(1)] Hediff exactCulprit, out CaravanLossFrame __state)
            => __state = CaravanLossFrame.Current?.Pawn == __instance ? null : CaravanLossFrame.Begin(__instance, dinfo, exactCulprit);
        [HarmonyPostfix]
        public static void Postfix(CaravanLossFrame __state) => Patch_CaravanLossDamage.Postfix(__state);
        [HarmonyFinalizer]
        public static void Finalizer(CaravanLossFrame __state) => __state?.Dispose();
    }

    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    public static class Patch_CaravanLossDowned
    {
        [HarmonyPrefix]
        public static void Prefix([HarmonyArgument(0)] DamageInfo? dinfo, [HarmonyArgument(1)] Hediff hediff, Pawn ___pawn, out CaravanLossFrame __state)
            => __state = CaravanLossFrame.Current?.Pawn == ___pawn ? null : CaravanLossFrame.Begin(___pawn, dinfo, hediff);
        [HarmonyPostfix]
        public static void Postfix(CaravanLossFrame __state) => Patch_CaravanLossDamage.Postfix(__state);
        [HarmonyFinalizer]
        public static void Finalizer(CaravanLossFrame __state) => __state?.Dispose();
    }

    [HarmonyPatch]
    public static class Patch_CaravanLossDrops
    {
        public static IEnumerable<MethodBase> TargetMethods()
            => typeof(ThingOwner).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.Name == "TryDrop" && m.GetParameters().Length == 8);
        [HarmonyPrefix]
        public static void Prefix(object[] __args, MethodBase __originalMethod)
        {
            CaravanLossFrame frame = CaravanLossFrame.Current;
            if (frame == null) return;
            int callback = Array.FindIndex(__originalMethod.GetParameters(), p => p.ParameterType == typeof(Action<Thing, int>));
            if (callback < 0) throw new InvalidOperationException("Caravan drop callback no longer matches.");
            __args[callback] = frame.WatchDrop((Thing)__args[0], (Action<Thing, int>)__args[callback]);
        }
    }
}
