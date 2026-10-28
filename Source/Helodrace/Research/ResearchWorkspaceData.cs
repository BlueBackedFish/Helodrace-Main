using System.Collections.Generic;
using System.Linq;
using Helodrace.ModernWar;
using UnityEngine;
using Verse;

namespace Helodrace.ResearchUI
{
    public sealed class HelodResearchSectionDef : Def
    {
        public int order;
        public Color color = new Color(0.3f, 0.6f, 0.7f);
        public List<ResearchProjectDef> projects = new List<ResearchProjectDef>();
        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (projects == null || projects.Any(p => p == null)) yield return "Section projects must resolve";
        }
    }

    public static class ResearchWorkspaceData
    {
        private static Dictionary<ResearchProjectDef, List<ThingDef>> parts;
        private static IEnumerable<ResearchLinkDef> ValidLinks => DefDatabase<ResearchLinkDef>.AllDefsListForReading
            .Where(link => link.leftProjects != null && link.rightProjects != null
                && link.leftProjects.All(p => p != null) && link.rightProjects.All(p => p != null));
        public static IEnumerable<ResearchProjectDef> Prerequisites(ResearchProjectDef project) => ResearchQueue.Prerequisites(project);
        public static bool IsHelod(ResearchProjectDef project) => project?.tab?.defName == "HelodTechTab";
        public static List<ResearchLinkDef> LinksFor(ResearchProjectDef project) =>
            ValidLinks.Where(link =>
                link.leftProjects.Contains(project) || link.rightProjects.Contains(project)).ToList();

        public static HashSet<ResearchProjectDef> Predict(ResearchProjectDef project)
        {
            return ResearchPlan.Predict(project,
                DefDatabase<ResearchProjectDef>.AllDefsListForReading.Where(p => p.IsFinished),
                ValidLinks.Select(link =>
                    System.Tuple.Create((IList<ResearchProjectDef>)link.leftProjects, (IList<ResearchProjectDef>)link.rightProjects)),
                p => p.prerequisites ?? Enumerable.Empty<ResearchProjectDef>());
        }

        public static List<ThingDef> PartsFor(ResearchProjectDef project)
        {
            if (parts == null)
            {
                parts = new Dictionary<ResearchProjectDef, List<ThingDef>>();
                foreach (ThingDef thing in DefDatabase<ThingDef>.AllDefsListForReading)
                {
                    var props = thing.GetCompProperties<CompProperties_ModularWeaponNode>();
                    if (props?.requiredResearch == null) continue;
                    foreach (ResearchProjectDef requirement in props.requiredResearch.Where(p => p != null).Distinct())
                    {
                        if (!parts.TryGetValue(requirement, out var list)) parts[requirement] = list = new List<ThingDef>();
                        list.Add(thing);
                    }
                }
                foreach (var list in parts.Values) list.Sort((a, b) => string.Compare(a.label, b.label, System.StringComparison.CurrentCulture));
            }
            return project != null && parts.TryGetValue(project, out var result) ? result : EmptyParts;
        }
        private static readonly List<ThingDef> EmptyParts = new List<ThingDef>();
        public static List<ResearchProjectDef> RequirementsFor(ThingDef part) =>
            part.GetCompProperties<CompProperties_ModularWeaponNode>()?.requiredResearch ?? new List<ResearchProjectDef>();
    }
}
