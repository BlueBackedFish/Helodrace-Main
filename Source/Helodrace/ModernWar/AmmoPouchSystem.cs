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
        // Armor pouches follow the weapon; mounted spare magazines use their own count.
        public int fixedCapacity;
        public ThingDef emptyEjectMoteDef;

        public CompProperties_AmmoPouch()
        {
            compClass = typeof(CompAmmoPouch);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;
            if (fixedCapacity < 0)
                yield return parentDef.defName + " has a negative fixed ammo pouch capacity.";
            if (emptyEjectMoteDef != null && (fixedCapacity <= 0
                || emptyEjectMoteDef.thingClass == null
                || !typeof(MoteThrown).IsAssignableFrom(emptyEjectMoteDef.thingClass)))
                yield return parentDef.defName + " has an invalid empty magazine ejection mote.";
        }
    }

    // The physical module owns its ammunition state. A different armor can use
    // this same comp without changing combat or persistence code.
    public sealed class CompAmmoPouch : ThingComp
    {
        private float remainingFraction = 1f;

        public float RemainingFraction => Mathf.Clamp01(remainingFraction);
        public int FixedCapacity => ((CompProperties_AmmoPouch)props).fixedCapacity;
        public ThingDef EmptyEjectMoteDef => ((CompProperties_AmmoPouch)props).emptyEjectMoteDef;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref remainingFraction, "ammoPouchRemainingFraction", 1f);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
                remainingFraction = Mathf.Clamp01(remainingFraction);
        }

        public int RoundsFor(int capacity) =>
            AmmoPouchRules.RoundsFor(RemainingFraction,
                AmmoPouchRules.Capacity(FixedCapacity, capacity));

        public bool ConsumeShot(int capacity)
        {
            capacity = AmmoPouchRules.Capacity(FixedCapacity, capacity);
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
            if (FixedCapacity > 0)
                return "HD_AmmoPouch_Rounds".Translate(RoundsFor(0), FixedCapacity);
            return "HD_AmmoPouch_StoredPercent".Translate(
                RemainingFraction.ToStringPercent());
        }
    }

    public static class AmmoPouchUtility
    {
        private const string FrontMagazineSocket = "front_magazine";

        private static ModularRenderNode FrontMagazine(ThingWithComps weapon)
        {
            CompModularWeaponNode root = weapon?.TryGetComp<CompModularWeaponNode>();
            if (root?.Props.isAssemblyRoot != true) return null;
            foreach (ModularRenderNode node in root.RenderSnapshot())
                if (node.parentSocketId == FrontMagazineSocket
                    && (node.thing as ThingWithComps)?.TryGetComp<CompAmmoPouch>()
                        ?.FixedCapacity > 0)
                    return node;
            return null;
        }

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
            ModularRenderNode front = FrontMagazine(weapon);
            if ((front?.thing as ThingWithComps)?.TryGetComp<CompAmmoPouch>()
                ?.RoundsFor(0) > 0) return true;
            int capacity = CapacityFor(weapon);
            if (capacity <= 0) return false;
            foreach (CompAmmoPouch pouch in WornPouches(pawn))
                if (pouch.RoundsFor(capacity) > 0) return true;
            return false;
        }

        public static void ConsumeShot(Pawn pawn, ThingWithComps weapon, Verb verb = null)
        {
            ModularRenderNode front = FrontMagazine(weapon);
            CompAmmoPouch frontPouch = (front?.thing as ThingWithComps)
                ?.TryGetComp<CompAmmoPouch>();
            if (frontPouch != null && frontPouch.ConsumeShot(0))
            {
                if (frontPouch.RoundsFor(0) == 0)
                {
                    TryAnimateEmptyMagazine(pawn, weapon, verb, front, frontPouch.EmptyEjectMoteDef);
                    Thing spent = front.parentComp?.DetachFromSocket(FrontMagazineSocket);
                    spent?.Destroy();
                }
                return;
            }
            int capacity = CapacityFor(weapon);
            if (capacity <= 0) return;
            foreach (CompAmmoPouch pouch in WornPouches(pawn))
                if (pouch.ConsumeShot(capacity)) return;
        }

        private static void TryAnimateEmptyMagazine(Pawn pawn, ThingWithComps weapon,
            Verb verb, ModularRenderNode node, ThingDef moteDef)
        {
            if (moteDef == null || pawn?.Map == null || !pawn.Spawned || verb == null)
                return;

            Vector3 direction = verb.CurrentTarget.CenterVector3 - pawn.DrawPos;
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.0001f)
                direction = pawn.Rotation.FacingCell.ToVector3();
            direction.Normalize();
            float aimAngle = direction.AngleFlat();

            Vector3 gunCenter;
            float bodyAngle;
            bool flipped;
            ModularWeaponAimingPoseUtility.Resolve(weapon, pawn, aimAngle,
                out gunCenter, out bodyAngle, out flipped);
            Vector2 local = node.transform.TransformPoint(node.Props.graphicOffset);
            if (flipped) local.x = -local.x;
            Vector3 origin = gunCenter + Quaternion.AngleAxis(bodyAngle, Vector3.up)
                * new Vector3(local.x, 0f, local.y);
            if (!origin.ToIntVec3().InBounds(pawn.Map)) return;

            MoteThrown mote = ThingMaker.MakeThing(moteDef) as MoteThrown;
            if (mote == null) return;
            GenSpawn.Spawn(mote, origin.ToIntVec3(), pawn.Map);
            mote.exactPosition = origin;
            mote.exactRotation = bodyAngle
                + (flipped ? node.transform.angle : -node.transform.angle);
            mote.rotationRate = Rand.Range(-250f, 250f);
            mote.SetVelocity(aimAngle + (flipped ? -115f : 115f),
                Rand.Range(0.6f, 0.9f));
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
                AmmoPouchUtility.ConsumeShot(pawn, weapon, __instance);
        }
    }
}
