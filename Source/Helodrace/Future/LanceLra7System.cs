using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.Future
{
    /// <summary>
    /// Persistent lifecycle states for the LRA-7's dedicated system.
    /// Additional states can be introduced when its operation and firing flow
    /// are implemented without changing the weapon definition.
    /// </summary>
    public enum LanceLra7SystemState
    {
        Ready
    }

    public sealed class CompProperties_LanceLra7System : CompProperties
    {
        public CompProperties_LanceLra7System()
        {
            compClass = typeof(CompLanceLra7System);
        }
    }

    /// <summary>
    /// Root component for all LANCE-specific behavior.
    /// </summary>
    public sealed class CompLanceLra7System : ThingComp
    {
        private LanceLra7SystemState state = LanceLra7SystemState.Ready;

        public LanceLra7SystemState State => state;

        public bool IsFireControlAvailable => state == LanceLra7SystemState.Ready;

        private Pawn Wielder => (parent.ParentHolder as Pawn_EquipmentTracker)?.pawn;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref state, "lanceLra7SystemState", LanceLra7SystemState.Ready);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            if (Wielder?.Faction != Faction.OfPlayer)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "HD_LanceLra7_System_Label".Translate().ToString(),
                defaultDesc = "HD_LanceLra7_System_Desc".Translate().ToString(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/LaunchReport", false) ?? BaseContent.BadTex,
                action = () => Messages.Message(
                    "HD_LanceLra7_System_Ready".Translate(),
                    parent,
                    MessageTypeDefOf.RejectInput,
                    false)
            };

        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.GetGizmos))]
    public static class Patch_Pawn_GetGizmos_LanceLra7
    {
        public static void Postfix(Pawn __instance, ref IEnumerable<Gizmo> __result)
        {
            CompLanceLra7System system = __instance?.equipment?.Primary?.TryGetComp<CompLanceLra7System>();
            if (system != null)
            {
                __result = __result.Concat(system.CompGetGizmosExtra());
            }
        }
    }
}
