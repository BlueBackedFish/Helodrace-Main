using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Color = Waylith.Math.Color;

namespace Helodrace
{
    /// <summary>
    /// Swaps a weapon's graphic while it holds no round, e.g. an M47 whose launch tube has
    /// been fired and only the reusable tracker remains.
    /// </summary>
    public class UnloadedWeaponGraphicExtension : DefModExtension
    {
        public string texPath;
    }

    public static class UnloadedWeaponGraphicUtility
    {
        /// <summary>
        /// Loaded-state sources registered by integrations that replace the vanilla loading comp
        /// (Combat Extended magazines). Return null when the provider does not handle the thing.
        /// </summary>
        public static readonly List<Func<ThingWithComps, bool?>> LoadedStateProviders =
            new List<Func<ThingWithComps, bool?>>();

        private static readonly Dictionary<ThingDef, Graphic> graphics = new Dictionary<ThingDef, Graphic>();

        public static bool TryGetUnloadedGraphic(ThingWithComps thing, out Graphic graphic)
        {
            graphic = null;
            UnloadedWeaponGraphicExtension extension = thing.def.GetModExtension<UnloadedWeaponGraphicExtension>();
            if (extension?.texPath == null || IsLoaded(thing))
            {
                return false;
            }

            if (!graphics.TryGetValue(thing.def, out graphic))
            {
                Vector2 drawSize = thing.def.graphicData?.drawSize ?? Vector2.one;
                graphic = GraphicDatabase.Get<Graphic_Single>(
                    extension.texPath,
                    ShaderDatabase.Cutout,
                    drawSize,
                    Color.white.ToGame());
                graphics[thing.def] = graphic;
            }

            return true;
        }

        private static bool IsLoaded(ThingWithComps thing)
        {
            foreach (Func<ThingWithComps, bool?> provider in LoadedStateProviders)
            {
                bool? state = provider(thing);
                if (state.HasValue)
                {
                    return state.Value;
                }
            }

            CompRecoillessWeapon comp = thing.TryGetComp<CompRecoillessWeapon>();
            return comp == null || comp.ShowsLoadedGraphic;
        }

        /// <summary>
        /// A round is consumed when the cast starts, but it stays in the launcher until the
        /// warm-up ends and the shot leaves, so keep drawing it while this weapon is firing.
        /// </summary>
        public static bool IsFiring(ThingWithComps weapon)
        {
            Pawn wielder = (weapon.ParentHolder as Pawn_EquipmentTracker)?.pawn;
            if (wielder == null)
            {
                return false;
            }

            if (wielder.stances?.curStance is Stance_Warmup warmup && warmup.verb?.EquipmentSource == weapon)
            {
                return true;
            }

            Verb verb = wielder.equipment?.PrimaryEq?.PrimaryVerb;
            return verb != null && verb.EquipmentSource == weapon && verb.Bursting;
        }
    }

    [HarmonyPatch(typeof(Thing), "get_Graphic")]
    public static class Patch_Thing_Graphic_UnloadedWeapon
    {
        public static bool Prefix(Thing __instance, ref Graphic __result)
        {
            if (__instance is ThingWithComps thing
                && UnloadedWeaponGraphicUtility.TryGetUnloadedGraphic(thing, out Graphic graphic))
            {
                __result = graphic;
                return false;
            }

            return true;
        }
    }
}
