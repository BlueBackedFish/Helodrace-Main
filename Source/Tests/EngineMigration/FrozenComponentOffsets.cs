using System;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
namespace Helodrace.Tests.FrozenComponentOffsets
{
public static class Patch_HelodHeadOffset
    {

        public static void Postfix(Pawn ___pawn, ref Vector3 __result)
        {
            if (!HelodRace.IsHelod(___pawn)) return;
            float age = ___pawn.ageTracker.AgeBiologicalYearsFloat;
            HelodHeadOffset entry = OffsetForAge(age);
            if (entry == null) return;
            float value = ___pawn.gender == Gender.Female ? entry.female : entry.male;
            if (entry.absolute)
                __result.z = value;
            else
                __result.z += value * Mathf.Sqrt(___pawn.ageTracker.CurLifeStage.bodySizeFactor);
        }

        internal static HelodHeadOffset OffsetForAge(float age)
        {
            var entries = HelodRace.Settings?.headOffsets;
            HelodHeadOffset selected = null;
            if (entries != null)
                foreach (var entry in entries)
                    if (entry != null && age >= entry.minAge
                        && (selected == null || entry.minAge >= selected.minAge))
                        selected = entry;
            // Missing settings leave the vanilla head offset unchanged.
            return selected;
        }
    }
public static class Patch_HelodBabyBedOffset
    {
        public static void Postfix(Pawn ___pawn, PawnPosture posture, ref Vector3 __result)
        {
            if (HelodRace.IsHelod(___pawn) && ___pawn.DevelopmentalStage.Baby()
                && posture == PawnPosture.LayingInBed)
                __result.z -= 0.3f;
        }
    }
public static class PatchPonpecoHairOffset
    {
        private const string HelodRaceDefName = "Helod";
        private const string PonpecoFemaleDefPrefix = "PPHF";
        private const float NorthSouthOffset = 0.01f;


        public static void Postfix(PawnRenderNode node, PawnDrawParms parms, ref Vector3 __result)
        {
            string hairDefName = parms.pawn?.story?.hairDef?.defName;

            if (node is PawnRenderNode_Hair &&
                parms.pawn?.def?.defName == HelodRaceDefName &&
                hairDefName != null &&
                hairDefName.StartsWith(PonpecoFemaleDefPrefix, StringComparison.Ordinal) &&
                (parms.facing == Rot4.North || parms.facing == Rot4.South))
            {
                __result.z += NorthSouthOffset;
            }
        }
    }
public static class PatchHelodUnspecifiedUtilityOffset
    {
        private const float EastWestOffsetX = 0.24f;
        private const float AllDirectionsOffsetZ = 0.16f;


        public static void Postfix(
            PawnRenderNode node,
            PawnDrawParms parms,
            ref Vector3 __result)
        {
            if (!HelodRace.IsHelod(parms.pawn)
                || !PatchHelodShellApparelLayer.IsUnspecifiedVanillaUtility(
                    node?.apparel?.def))
            {
                return;
            }

            __result.z += AllDirectionsOffsetZ;

            if (parms.facing == Rot4.East)
            {
                __result.x += EastWestOffsetX;
            }
            else if (parms.facing == Rot4.West)
            {
                __result.x -= EastWestOffsetX;
            }
        }
    }
internal static class Patch_DrawEquipmentAiming_HelodraceHeight
    {


        private static void Prefix(Thing eq, ref UnityEngine.Vector3 drawLoc)
        {
            if (eq?.def?.IsRangedWeapon == true
                && eq.def.modContentPack == HelodraceBase.Instance?.Content)
                drawLoc.z += 0.05f;
        }
    }
internal static class PatchHelodShellApparelLayer
{internal static bool IsUnspecifiedVanillaUtility(ThingDef def)=>(bool)typeof(global::Helodrace.PatchHelodShellApparelLayer).GetMethod("IsUnspecifiedVanillaUtility",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{def});}
internal sealed class Photo : ThingComp
{
private void ThrowIgnitionEffect()
        {
            Vector3 loc = parent.DrawPos;
            loc.z += 0.45f;
            FleckMaker.ThrowFireGlow(loc, parent.Map, 2.0f);
            FleckMaker.ThrowMicroSparks(loc, parent.Map);
            FleckMaker.ThrowSmoke(loc, parent.Map, 1.4f);
        }
private void ThrowBurningEffect()
        {
            Vector3 loc = parent.DrawPos;
            loc.z += 0.45f;
            FleckMaker.ThrowFireGlow(loc, parent.Map, 1.25f);
            FleckMaker.ThrowSmoke(loc, parent.Map, 0.7f);
        }
}
internal sealed class Sweet : ThingComp
{
private void ThrowIgnitionEffect()
        {
            Vector3 loc = parent.DrawPos;
            loc.z += 0.45f;
            FleckMaker.ThrowFireGlow(loc, parent.Map, 2.0f);
            FleckMaker.ThrowMicroSparks(loc, parent.Map);
            FleckMaker.ThrowSmoke(loc, parent.Map, 1.4f);
        }
private void ThrowBurningEffect()
        {
            Vector3 loc = parent.DrawPos;
            loc.z += 0.45f;
            FleckMaker.ThrowFireGlow(loc, parent.Map, 1.25f);
            FleckMaker.ThrowSmoke(loc, parent.Map, 0.7f);
        }
}
}
