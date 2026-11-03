using System;
using System.Collections.Generic;
using Helodrace;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class HarmonyPatch : Attribute
    {
        public HarmonyPatch(Type type, string method) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HarmonyPostfix : Attribute { }
}

namespace Verse
{
    public class Def
    {
        public string defName;
        public virtual IEnumerable<string> ConfigErrors() { yield break; }
    }

    public class ResearchProjectDef : Def
    {
        public bool IsFinished;
        public List<ResearchProjectDef> prerequisites = new List<ResearchProjectDef>();
    }

    public static class DefDatabase<T> where T : Def
    {
        public static List<T> AllDefsListForReading = new List<T>();
    }

    public class Pawn { }
    public class Game { }
    public class GameComponent
    {
        public virtual void StartedNewGame() { }
        public virtual void LoadedGame() { }
    }

    public static class Find
    {
        public static RimWorld.ResearchManager ResearchManager;
    }
}

namespace RimWorld
{
    public class ResearchManager
    {
        public int FinishCalls;
        public int Dialogs;
        public int Letters;
        public string ThrowOn;

        public void FinishProject(Verse.ResearchProjectDef project, bool doCompletionDialog = false,
            Verse.Pawn researcher = null, bool doCompletionLetter = true)
        {
            if (++FinishCalls > 2000) throw new Exception("Research completion did not terminate");
            if (project.defName == ThrowOn) throw new InvalidOperationException("Injected completion failure");
            // RimWorld 1.7 FinishProject recursively finishes all ordinary prerequisites.
            foreach (Verse.ResearchProjectDef prerequisite in project.prerequisites)
                if (!prerequisite.IsFinished)
                    FinishProject(prerequisite, doCompletionDialog, researcher, doCompletionLetter);
            project.IsFinished = true;
            if (doCompletionDialog) Dialogs++;
            if (doCompletionLetter) Letters++;
            ResearchManagerFinishProjectLinkedResearchPatch.Postfix(this);
        }
    }
}
