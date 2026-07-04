using System.Linq;
using Verse;
using Verse.AI;

namespace Helodrace
{
    [StaticConstructorOnStartup]
    public static class BTXThinkTreeInjector
    {
        static BTXThinkTreeInjector()
        {
            LongEventHandler.ExecuteWhenFinished(InjectBTXNeedJobGiver);
        }

        private static void InjectBTXNeedJobGiver()
        {
            ThinkTreeDef humanlikeTree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail("Humanlike");
            ThinkNode root = humanlikeTree?.thinkRoot;
            if (root?.subNodes == null)
            {
                Log.Warning("Helodrace: Could not inject BTX need job giver because the Humanlike think tree was not found.");
                return;
            }

            if (root.subNodes.Any(node => node is JobGiver_SatisfyBTXNeed))
            {
                return;
            }

            JobGiver_SatisfyBTXNeed jobGiver = new JobGiver_SatisfyBTXNeed
            {
                parent = root
            };
            root.subNodes.Insert(0, jobGiver);
        }
    }
}
