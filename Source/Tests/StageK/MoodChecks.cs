using System.Linq;
using RimWorld;
using Verse;
namespace Helodrace.Tests.StageK;
public sealed partial class Checks
{
    partial void MoodChecks(Pawn pawn)
    {
                var thought=Def<ThoughtDef>("HD_CigaretteCalm");
                pawn.health.RemoveAllHediffs();Check(!thought.Worker.CurrentState(pawn).Active,"No cigarette mood without nicotine rush");
                pawn.health.AddHediff(Def<HediffDef>("HD_NicotineRush")).Severity=1f;
                Check(thought.Worker.CurrentState(pawn).Active&&thought.stages[0].baseMoodEffect==3,"Actual nicotine rush activates one +3 situational thought");
                pawn.health.AddHediff(Def<HediffDef>("HD_NicotineRush")).Severity=1f;
                Check(pawn.health.hediffSet.hediffs.Count(h=>h.def.defName=="HD_NicotineRush")==1,"Repeated cigarettes do not add duplicate rush hediffs");
                pawn.health.RemoveAllHediffs();Check(!thought.Worker.CurrentState(pawn).Active,"Mood ends when the existing nicotine rush ends");
    }
}
