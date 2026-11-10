using LudeonTK;
using Verse;

namespace Helodrace.Ballistics
{
    public static class BallisticsDebugActions
    {
        // Developer output only; not player-facing text.
        [DebugAction("Helodrace", "Log recent ballistic impact traces", allowedGameStates = AllowedGameStates.Playing)]
        public static void LogRecentTraces()
        {
            ImpactTraceBuffer traces = HumanoidBallisticImpact.RecentTraces;
            if (traces.Count == 0)
            {
                Log.Message("[Helodrace.Ballistics] No physical impacts recorded yet.");
                return;
            }
            for (int i = traces.Count - 1; i >= 0; i--)
            {
                Log.Message("[Helodrace.Ballistics] " + traces[i].ToText());
            }
        }

        [DebugAction("Helodrace", "Log body orientation of selected pawns", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void LogSelectedOrientation()
        {
            foreach (object selected in Find.Selector.SelectedObjects)
            {
                if (!(selected is Pawn pawn)) continue;
                CompBodyOrientation comp = pawn.TryGetComp<CompBodyOrientation>();
                if (comp == null)
                {
                    Log.Message("[Helodrace.Ballistics] " + pawn.LabelShort + ": no body orientation");
                    continue;
                }
                Log.Message("[Helodrace.Ballistics] " + pawn.LabelShort + ": torso " + comp.TorsoYaw.ToString("0.0")
                    + ", head " + comp.HeadYaw.ToString("0.0") + ", sprite " + pawn.Rotation.AsAngle
                    + ", aiming " + comp.Aiming + (comp.Aiming ? " at " + comp.AimYaw.ToString("0.0") : "")
                    + ", stance " + (comp.Profile?.defName ?? "none"));
            }
        }

        [DebugAction("Helodrace", "Copy last ballistic trace as JSON", allowedGameStates = AllowedGameStates.Playing)]
        public static void CopyLastTraceJson()
        {
            ImpactTraceBuffer traces = HumanoidBallisticImpact.RecentTraces;
            if (traces.Count == 0)
            {
                Log.Message("[Helodrace.Ballistics] No physical impacts recorded yet.");
                return;
            }
            string json = traces[0].ToJson();
            UnityEngine.GUIUtility.systemCopyBuffer = json;
            Log.Message("[Helodrace.Ballistics] " + json);
        }
    }
}
