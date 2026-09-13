using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace
{
    /// <summary>
    /// Makes two sets of research projects equivalent.
    /// When every project on either side is finished, every project on the
    /// opposite side is finished as well.
    /// </summary>
    public class ResearchLinkDef : Def
    {
        public List<ResearchProjectDef> leftProjects;
        public List<ResearchProjectDef> rightProjects;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (leftProjects == null || leftProjects.Count == 0)
            {
                yield return "leftProjects must contain at least one research project";
            }

            if (rightProjects == null || rightProjects.Count == 0)
            {
                yield return "rightProjects must contain at least one research project";
            }

            if (leftProjects != null)
            {
                foreach (ResearchProjectDef project in leftProjects)
                {
                    if (project == null)
                    {
                        yield return "leftProjects contains a null research project";
                    }
                }
            }

            if (rightProjects != null)
            {
                foreach (ResearchProjectDef project in rightProjects)
                {
                    if (project == null)
                    {
                        yield return "rightProjects contains a null research project";
                    }
                }
            }
        }
    }

    public static class LinkedResearchUtility
    {
        private static bool synchronizing;

        public static void Synchronize(ResearchManager manager)
        {
            if (manager == null || synchronizing)
            {
                return;
            }

            synchronizing = true;
            try
            {
                bool changed;
                do
                {
                    changed = false;
                    List<ResearchLinkDef> links = DefDatabase<ResearchLinkDef>.AllDefsListForReading;
                    for (int i = 0; i < links.Count; i++)
                    {
                        ResearchLinkDef link = links[i];
                        if (SideIsFinished(link.leftProjects))
                        {
                            changed |= FinishSide(manager, link.rightProjects);
                        }

                        if (SideIsFinished(link.rightProjects))
                        {
                            changed |= FinishSide(manager, link.leftProjects);
                        }
                    }
                }
                while (changed);
            }
            finally
            {
                synchronizing = false;
            }
        }

        private static bool SideIsFinished(List<ResearchProjectDef> projects)
        {
            if (projects == null || projects.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < projects.Count; i++)
            {
                ResearchProjectDef project = projects[i];
                if (project == null || !project.IsFinished)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool FinishSide(ResearchManager manager, List<ResearchProjectDef> projects)
        {
            if (projects == null)
            {
                return false;
            }

            bool changed = false;
            for (int i = 0; i < projects.Count; i++)
            {
                ResearchProjectDef project = projects[i];
                if (project == null || project.IsFinished)
                {
                    continue;
                }

                manager.FinishProject(project, false, null, false);
                changed |= project.IsFinished;
            }

            return changed;
        }
    }

    [HarmonyPatch(typeof(ResearchManager), nameof(ResearchManager.FinishProject))]
    public static class ResearchManagerFinishProjectLinkedResearchPatch
    {
        [HarmonyPostfix]
        public static void Postfix(ResearchManager __instance)
        {
            LinkedResearchUtility.Synchronize(__instance);
        }
    }

    /// <summary>
    /// Synchronizes research completed before this mod's FinishProject patch
    /// could observe it, including research already present in an old save.
    /// </summary>
    public class LinkedResearchGameComponent : GameComponent
    {
        public LinkedResearchGameComponent(Game game)
        {
        }

        public override void StartedNewGame()
        {
            LinkedResearchUtility.Synchronize(Find.ResearchManager);
        }

        public override void LoadedGame()
        {
            LinkedResearchUtility.Synchronize(Find.ResearchManager);
        }
    }
}
