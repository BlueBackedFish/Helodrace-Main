using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace.ResearchUI
{
    public sealed class ResearchQueue : GameComponent
    {
        public List<ResearchProjectDef> Goals = new List<ResearchProjectDef>();
        public bool Paused;
        private ResearchProjectDef ownedProject;
        private bool warnedCycle;
        public ResearchQueue(Game game) { }
        public static ResearchQueue Current => Verse.Current.Game?.GetComponent<ResearchQueue>();

        public static IEnumerable<ResearchProjectDef> Prerequisites(ResearchProjectDef project) =>
            (project.prerequisites ?? Enumerable.Empty<ResearchProjectDef>())
            .Concat(project.hiddenPrerequisites ?? Enumerable.Empty<ResearchProjectDef>());

        public List<ResearchProjectDef> Plan
        {
            get
            {
                try { return ResearchPlan.Build(Goals, p => p.IsFinished, Prerequisites); }
                catch (System.InvalidOperationException)
                {
                    Paused = true;
                    if (!warnedCycle) Log.Warning("Helodrace: research queue paused because a mod defines cyclic prerequisites.");
                    warnedCycle = true;
                    return new List<ResearchProjectDef>();
                }
            }
        }

        public void Add(ResearchProjectDef project)
        {
            if (project == null || project.IsFinished || Goals.Contains(project)) return;
            // Validate before changing the saved queue, including mod-supplied definitions.
            try { ResearchPlan.Build(new[] { project }, p => p.IsFinished, Prerequisites); }
            catch (System.InvalidOperationException)
            {
                Log.Warning("Helodrace: cannot queue cyclic research " + project.defName);
                return;
            }
            Goals.Add(project);
            Advance();
        }

        public void Remove(ResearchProjectDef project) { Goals.Remove(project); StopOrphanedProject(); Advance(); }
        public void Move(int index, int delta)
        {
            int target = index + delta;
            if (index < 0 || index >= Goals.Count || target < 0 || target >= Goals.Count) return;
            var project = Goals[index]; Goals.RemoveAt(index); Goals.Insert(target, project);
        }
        public void TogglePause()
        {
            Paused = !Paused;
            if (Paused) StopOwnedProject(); else Advance();
        }
        private void StopOwnedProject()
        {
            if (!ResearchWindowCompatibility.ExternalQueueOwnsResearch && ownedProject != null
                && Find.ResearchManager?.IsCurrentProject(ownedProject) == true)
                Find.ResearchManager.StopProject(ownedProject);
            ownedProject = null;
        }
        private void StopOrphanedProject()
        {
            if (ownedProject != null && !Plan.Contains(ownedProject)) StopOwnedProject();
        }

        public void Advance()
        {
            Goals.RemoveAll(p => p == null || p.IsFinished);
            var manager = Find.ResearchManager;
            if (manager == null || Paused || ResearchWindowCompatibility.ExternalQueueOwnsResearch) return;
            StopOrphanedProject();
            // An explicit manual selection outside this queue remains in charge until it finishes.
            var active = manager.GetProject(null);
            if (active != null && !active.IsFinished) return;
            var next = Plan.FirstOrDefault();
            if (next == null || !next.CanStartNow) return;
            ownedProject = next;
            manager.SetCurrentProject(next);
        }

        public override void GameComponentTick()
        {
            if (Find.TickManager.TicksGame % 60 == 0) Advance();
        }
        public override void StartedNewGame() => ResearchWorkspaceRevision.Invalidate();
        public override void LoadedGame() => ResearchWorkspaceRevision.Invalidate();
        public override void ExposeData()
        {
            Scribe_Collections.Look(ref Goals, "HD_researchQueueGoals", LookMode.Def);
            Scribe_Values.Look(ref Paused, "HD_researchQueuePaused");
            Scribe_Defs.Look(ref ownedProject, "HD_researchQueueOwnedProject");
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                Goals = (Goals ?? new List<ResearchProjectDef>()).Where(p => p != null && !p.IsFinished).Distinct().ToList();
            }
        }
    }
}
