using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.Tactical
{
    // UI preference only: collapsing commands must not interrupt a slide or
    // silently change an active firing stance. Stored per pawn, per save.
    public sealed class GameComponent_TacticalCqbMode : GameComponent
    {
        private List<Pawn> expandedPawns = new List<Pawn>();

        public GameComponent_TacticalCqbMode(Game game) { }

        public bool IsExpanded(Pawn pawn) => pawn != null
            && expandedPawns.Any(candidate => ReferenceEquals(candidate, pawn));

        public void Toggle(Pawn pawn)
        {
            if (pawn == null) return;
            if (expandedPawns.RemoveAll(candidate => ReferenceEquals(candidate, pawn)) == 0)
                expandedPawns.Add(pawn);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref expandedPawns, "hdCqbExpandedPawns", LookMode.Reference);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (expandedPawns == null) expandedPawns = new List<Pawn>();
                expandedPawns.RemoveAll(pawn => pawn == null || pawn.Destroyed);
            }
        }
    }

    public static class TacticalCqbModeUtility
    {
        private static GameComponent_TacticalCqbMode State =>
            Current.Game?.GetComponent<GameComponent_TacticalCqbMode>();

        public static bool ShowCommands(Pawn pawn) => State?.IsExpanded(pawn) == true;

        public static Command CreateCommand(Pawn pawn)
        {
            return new Command_Toggle
            {
                defaultLabel = "HD_CqbMode_Command".Translate(),
                defaultDesc = "HD_CqbMode_CommandDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Attack", false) ?? BaseContent.BadTex,
                isActive = () => ShowCommands(pawn),
                toggleAction = () => State?.Toggle(pawn)
            };
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_TacticalCqbMode
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            if (__instance?.Faction == Faction.OfPlayer && !__instance.Dead
                && TacticalAimUtility.HasCQBTraining(__instance))
                __result = __result.Concat(new[] { TacticalCqbModeUtility.CreateCommand(__instance) });
        }
    }
}
