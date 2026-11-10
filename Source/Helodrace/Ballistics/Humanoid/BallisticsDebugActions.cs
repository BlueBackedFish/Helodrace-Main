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
