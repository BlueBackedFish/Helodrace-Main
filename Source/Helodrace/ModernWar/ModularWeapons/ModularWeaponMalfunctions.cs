using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public enum ModularAmmunitionCompatibilityKind
    {
        Unspecified,
        Compatible,
        FailureToChamber,
        Catastrophic
    }

    public sealed class ModularAmmunitionCompatibilityResult
    {
        public ModularAmmunitionCompatibilityKind kind;
        public CompProperties_ModularWeaponNode barrel;
        public CompProperties_ModularWeaponNode ammunition;

        public string ChamberLabel => ModularWeaponAmmunitionUtility.DisplayCaliber(
            barrel?.chamberCaliber);

        public string AmmunitionLabel
        {
            get
            {
                string label = ammunition?.ammunitionType;
                return !label.NullOrEmpty()
                    ? label
                    : ModularWeaponAmmunitionUtility.DisplayCaliber(
                        ammunition?.ammunitionCaliber);
            }
        }
    }

    public static class ModularWeaponAmmunitionUtility
    {
        private const float DiameterToleranceMm = 0.05f;

        public static ModularAmmunitionCompatibilityResult Resolve(
            CompModularWeaponNode root)
        {
            return Resolve(root?.RenderSnapshot());
        }

        public static ModularAmmunitionCompatibilityResult Resolve(
            List<ModularRenderNode> snapshot)
        {
            ModularAmmunitionCompatibilityResult result =
                new ModularAmmunitionCompatibilityResult
                {
                    kind = ModularAmmunitionCompatibilityKind.Unspecified
                };
            if (snapshot.NullOrEmpty()) return result;

            for (int i = 0; i < snapshot.Count; i++)
            {
                CompProperties_ModularWeaponNode props = snapshot[i].Props;
                if (result.barrel == null && !props.chamberCaliber.NullOrEmpty())
                    result.barrel = props;
                if (result.ammunition == null && !props.ammunitionCaliber.NullOrEmpty())
                    result.ammunition = props;
            }

            if (result.barrel == null || result.ammunition == null) return result;
            if (string.Equals(
                result.barrel.chamberCaliber,
                result.ammunition.ammunitionCaliber,
                StringComparison.OrdinalIgnoreCase))
            {
                result.kind = ModularAmmunitionCompatibilityKind.Compatible;
                return result;
            }

            result.kind = result.ammunition.projectileDiameterMm
                    > result.barrel.boreDiameterMm + DiameterToleranceMm
                ? ModularAmmunitionCompatibilityKind.Catastrophic
                : ModularAmmunitionCompatibilityKind.FailureToChamber;
            return result;
        }

        public static string DisplayCaliber(string caliber)
        {
            if (caliber.NullOrEmpty()) return "?";
            if (string.Equals(caliber, "556x45", StringComparison.OrdinalIgnoreCase))
                return "5.56x45mm";
            if (string.Equals(caliber, "300BLK", StringComparison.OrdinalIgnoreCase))
                return ".300 BLK";
            return caliber;
        }
    }

    [HarmonyPatch(typeof(Verb_LaunchProjectile), "TryCastShot")]
    public static class Patch_Verb_ModularAmmunitionMalfunction
    {
        private const int FailureMessageCooldownTicks = 120;
        private static readonly Dictionary<int, int> nextFailureMessageTick =
            new Dictionary<int, int>();

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static bool Prefix(Verb_LaunchProjectile __instance, ref bool __result)
        {
            Thing weapon = __instance?.EquipmentSource;
            CompModularWeaponNode comp = weapon?.TryGetComp<CompModularWeaponNode>();
            if (comp?.Props.isAssemblyRoot != true) return true;

            ModularAmmunitionCompatibilityResult compatibility =
                ModularWeaponAmmunitionUtility.Resolve(comp);
            if (compatibility.kind == ModularAmmunitionCompatibilityKind.Unspecified
                || compatibility.kind == ModularAmmunitionCompatibilityKind.Compatible)
                return true;

            __result = false;
            Pawn shooter = __instance.caster as Pawn;
            if (compatibility.kind == ModularAmmunitionCompatibilityKind.Catastrophic)
                TriggerCatastrophicFailure(weapon, shooter, compatibility);
            else
                NotifyFailureToChamber(weapon, shooter, compatibility);
            return false;
        }

        private static void NotifyFailureToChamber(
            Thing weapon,
            Pawn shooter,
            ModularAmmunitionCompatibilityResult compatibility)
        {
            int id = weapon?.thingIDNumber ?? -1;
            int now = Find.TickManager?.TicksGame ?? 0;
            int next;
            if (id >= 0 && nextFailureMessageTick.TryGetValue(id, out next) && now < next)
                return;
            if (id >= 0) nextFailureMessageTick[id] = now + FailureMessageCooldownTicks;

            string user = shooter?.LabelShortCap ?? weapon?.LabelCap ?? "Weapon";
            Messages.Message(
                "HD_ModularWeapon_FailureToChamber".Translate(
                    user,
                    compatibility.AmmunitionLabel,
                    compatibility.ChamberLabel),
                shooter ?? weapon,
                MessageTypeDefOf.RejectInput,
                false);
        }

        private static void TriggerCatastrophicFailure(
            Thing weapon,
            Pawn shooter,
            ModularAmmunitionCompatibilityResult compatibility)
        {
            if (shooter != null && !shooter.Dead)
            {
                int stunTicks = Rand.RangeInclusive(180, 300);
                shooter.stances?.stunner?.StunFor(
                    stunTicks,
                    weapon ?? shooter,
                    true,
                    true,
                    false);

                int fragmentCount = Rand.RangeInclusive(2, 4);
                for (int i = 0; i < fragmentCount && !shooter.Dead; i++)
                {
                    BodyPartRecord hitPart = RandomUpperBodyPart(shooter);
                    shooter.TakeDamage(new DamageInfo(
                        DamageDefOf.Cut,
                        Rand.Range(3.5f, 7.5f),
                        0.22f,
                        -1f,
                        weapon,
                        hitPart,
                        weapon?.def));
                }

                if (shooter.Spawned)
                {
                    FleckMaker.ThrowMicroSparks(shooter.DrawPos, shooter.Map);
                    FleckMaker.ThrowDustPuff(shooter.DrawPos, shooter.Map, 1.2f);
                }
            }

            if (weapon != null && !weapon.Destroyed)
            {
                weapon.TakeDamage(new DamageInfo(
                    DamageDefOf.Bomb,
                    Rand.RangeInclusive(35, 55),
                    0.75f,
                    -1f,
                    shooter));
            }

            string user = shooter?.LabelShortCap ?? "Weapon user";
            Messages.Message(
                "HD_ModularWeapon_CatastrophicFailure".Translate(
                    user,
                    compatibility.AmmunitionLabel,
                    compatibility.ChamberLabel),
                shooter ?? weapon,
                MessageTypeDefOf.NegativeEvent,
                true);
        }

        private static BodyPartRecord RandomUpperBodyPart(Pawn pawn)
        {
            List<BodyPartRecord> candidates = pawn.health.hediffSet
                .GetNotMissingParts()
                .Where(IsUpperBodyBlastPart)
                .ToList();
            if (candidates.Count == 0)
            {
                candidates = pawn.health.hediffSet.GetNotMissingParts()
                    .Where(part => part.depth == BodyPartDepth.Outside)
                    .ToList();
            }
            return candidates.Count > 0
                ? candidates.RandomElementByWeight(UpperBodyPartWeight)
                : null;
        }

        private static bool IsUpperBodyBlastPart(BodyPartRecord part)
        {
            string name = part?.def?.defName;
            return name == "Face" || name == "Neck" || name == "Torso"
                || name == "Head";
        }

        private static float UpperBodyPartWeight(BodyPartRecord part)
        {
            switch (part?.def?.defName)
            {
                case "Face": return 4f;
                case "Neck": return 2f;
                case "Torso": return 5f;
                case "Head": return 1f;
                default: return Mathf.Max(0.01f, part?.coverageAbs ?? 0.01f);
            }
        }
    }
}
