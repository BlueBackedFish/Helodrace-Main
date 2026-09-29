using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public sealed class CompProperties_AmmoPouch : CompProperties
    {
        public CompProperties_AmmoPouch()
        {
            compClass = typeof(CompAmmoPouch);
        }
    }

    // The physical module owns its ammunition state. A different armor can use
    // this same comp without changing combat or persistence code.
    public sealed class CompAmmoPouch : ThingComp
    {
        private float remainingFraction = 1f;

        public float RemainingFraction => Mathf.Clamp01(remainingFraction);

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref remainingFraction, "ammoPouchRemainingFraction", 1f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                remainingFraction = Mathf.Clamp01(remainingFraction);
        }

        public int RoundsFor(int capacity) =>
            AmmoPouchRules.RoundsFor(RemainingFraction, capacity);

        public bool ConsumeShot(int capacity)
        {
            int rounds = RoundsFor(capacity);
            if (rounds <= 0) return false;
            remainingFraction = AmmoPouchRules.AfterShot(rounds, capacity);
            return true;
        }

        public int SteelNeeded => AmmoPouchRules.SteelNeeded(RemainingFraction);

        public void AddCarbonSteel(int amount)
        {
            if (amount > 0)
                remainingFraction = AmmoPouchRules.AfterReplenish(RemainingFraction, amount);
        }

        public override string CompInspectStringExtra()
        {
            return "HD_AmmoPouch_StoredPercent".Translate(
                RemainingFraction.ToStringPercent());
        }
    }

    public static class AmmoPouchUtility
    {
        public static int CapacityFor(ThingWithComps weapon)
        {
            if (weapon == null || !weapon.def.IsRangedWeapon) return 0;
            CompModularWeaponNode modular = weapon.TryGetComp<CompModularWeaponNode>();
            if (modular?.Props.isAssemblyRoot == true)
                return Mathf.Max(0, modular.MagazineCapacity);

            VerbProperties verb = weapon.def.Verbs?.Find(candidate =>
                candidate.Ranged && candidate.defaultProjectile?.projectile != null);
            if (verb == null) return 0;
            return AmmoPouchRules.EstimateCapacity(
                verb.burstShotCount,
                verb.defaultProjectile.projectile.GetDamageAmount(weapon));
        }

        public static IEnumerable<CompAmmoPouch> WornPouches(Pawn pawn)
        {
            if (pawn?.apparel?.WornApparel == null) yield break;
            foreach (Apparel apparel in pawn.apparel.WornApparel)
            {
                CompAmmoPouch directPouch = apparel.TryGetComp<CompAmmoPouch>();
                if (directPouch != null) yield return directPouch;
                CompModularArmor armor = apparel.TryGetComp<CompModularArmor>();
                if (armor == null) continue;
                foreach (InstalledModularArmorPart part in armor.InstalledParts)
                {
                    CompAmmoPouch pouch = part?.InstalledItem?.TryGetComp<CompAmmoPouch>();
                    if (pouch != null) yield return pouch;
                }
            }
        }

        public static bool HasRounds(Pawn pawn, ThingWithComps weapon)
        {
            int capacity = CapacityFor(weapon);
            if (capacity <= 0) return false;
            foreach (CompAmmoPouch pouch in WornPouches(pawn))
                if (pouch.RoundsFor(capacity) > 0) return true;
            return false;
        }

        public static void ConsumeShot(Pawn pawn, ThingWithComps weapon)
        {
            int capacity = CapacityFor(weapon);
            if (capacity <= 0) return;
            foreach (CompAmmoPouch pouch in WornPouches(pawn))
                if (pouch.ConsumeShot(capacity)) return;
        }

        public static void ApplyCooldown(Thing weapon, ref float seconds)
        {
            if (!(weapon is ThingWithComps equipped)
                || !(weapon.ParentHolder is Pawn_EquipmentTracker tracker)
                || tracker.pawn?.equipment?.Primary != weapon
                || !HasRounds(tracker.pawn, equipped)) return;
            CompModularWeaponNode modular = equipped.TryGetComp<CompModularWeaponNode>();
            float mechanicalMinimum = modular?.Props.isAssemblyRoot == true
                ? modular.MinimumFireDelaySeconds : 0f;
            seconds = AmmoPouchRules.Cooldown(seconds, mechanicalMinimum);
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class Patch_Verb_AmmoPouchShot
    {
        [HarmonyPostfix]
        public static void Postfix(Verb_LaunchProjectile __instance, bool __result)
        {
            if (__result && __instance?.CasterPawn is Pawn pawn
                && __instance.EquipmentSource is ThingWithComps weapon
                && pawn.equipment?.Primary == weapon)
                AmmoPouchUtility.ConsumeShot(pawn, weapon);
        }
    }
}
