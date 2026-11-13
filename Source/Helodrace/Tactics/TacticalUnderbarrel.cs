using System;
using System.Diagnostics;
using System.Linq;
using HarmonyLib;
using Helodrace.ModernWar;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public sealed partial class TacticalMemberCommand { public int UnderbarrelCheckAt,UnderbarrelReloadAt; }
    public sealed partial class MapComponent_TacticalCommands
    {
        private bool UnderbarrelContext(Pawn pawn,out TacticalSquadCommand c,out TacticalMemberCommand member)
        {
            member=null;
            if(!byPawn.TryGetValue(pawn,out c) || pawn.Faction==Faction.OfPlayer || pawn.Dead || pawn.Downed
                || c.Terminal || c.Withdrawing || c.ContactResponse!=null || c.ChargeAction!=null
                || c.OpeningAction?.Launched==true && !c.OpeningAction.EffectsCleared
                || c.Phase!=TacticalCommandPhase.Stack && c.Phase!=TacticalCommandPhase.Clear
                    && c.Phase!=TacticalCommandPhase.Complete && c.FieldResponse==null) return false;
            member=c.Members.Find(m=>m.Pawn==pawn);
            return member?.CombatFunction=="Grenadier" && !CasualtyJob(pawn)
                && (c.CqbParent??c).MedicalCare?.Helper!=member;
        }
        internal bool LauncherSafe(Pawn pawn,Verb_ShootModularUnderbarrel verb,LocalTargetInfo target)
        {
            if(!UnderbarrelContext(pawn,out _,out _) || !target.HasThing || !target.Thing.HostileTo(pawn)
                || !verb.CanHitTarget(target)) return false;
            if(target.Thing is Pawn enemy && (enemy.Dead || enemy.Downed)) return false;
            var launcher=ModularUnderbarrelLauncherUtility.Installed(pawn.equipment?.Primary);
            return launcher?.Loaded==true && SafeExplosiveShot(pawn,launcher.ProjectileDef,pawn.Position,target.Cell,
                scatterOverride:verb.verbProps.ForcedMissRadius+2);
        }
        internal Verb SelectUnderbarrel(Pawn pawn,Thing target,Verb fallback,bool ownsObservation=false)
        {
            if(target==null || fallback is Verb_ShootModularUnderbarrel || !UnderbarrelContext(pawn,out _,out var member)) return fallback;
            int tick=GenTicks.TicksGame;
            if(tick<member.UnderbarrelCheckAt || pawn.pather.Moving || pawn.stances.FullBodyBusy) return fallback;
            member.UnderbarrelCheckAt=tick+180;
            var budget=Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if(!ownsObservation && !budget.TryObserve(tick)) { member.UnderbarrelCheckAt=tick+1; return fallback; }
            long started=Stopwatch.GetTimestamp();
            try
            {
                var verb=pawn.equipment?.PrimaryEq?.AllVerbs.OfType<Verb_ShootModularUnderbarrel>().FirstOrDefault();
                return verb!=null && LauncherSafe(pawn,verb,target)?verb:fallback;
            }
            finally { if(!ownsObservation) budget.Account(tick,Stopwatch.GetTimestamp()-started); }
        }
        private bool AdvanceUnderbarrelReload(TacticalSquadCommand command,System.Collections.Generic.List<TacticalMemberCommand> active,int tick)
        {
            if(active.Any(m=>m.Pawn.CurJobDef?.defName=="HD_ReloadModularUnderbarrel"))
            { command.Due=tick+10; return true; }
            foreach(var member in active)
            {
                Pawn pawn=member.Pawn;
                if(tick<member.UnderbarrelReloadAt || !UnderbarrelContext(pawn,out _,out _) || pawn.pather.Moving
                    || pawn.stances.curStance is Stance_Warmup warmup && warmup.verb is Verb_ShootModularUnderbarrel) continue;
                member.UnderbarrelReloadAt=tick+120;
                var launcher=ModularUnderbarrelLauncherUtility.Installed(pawn.equipment?.Primary);
                if(launcher==null || launcher.Loaded || !launcher.HasAmmo(pawn) || !CanIssue(member)) continue;
                if(Issue(member,JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_ReloadModularUnderbarrel"),pawn.equipment.Primary)))
                { member.UnderbarrelCheckAt=0; command.Due=tick+10; return true; }
            }
            return false;
        }
    }
    [LegacyEngine,HarmonyPatch(typeof(Pawn),nameof(Pawn.TryGetAttackVerb))]
    public static class Patch_TacticalUnderbarrelSelection
    {
        public static void Postfix(Pawn __instance,Thing target,ref Verb __result)
        {
            if(__result==null || __instance.Faction==Faction.OfPlayer) return;
            var owner=__instance.Map?.GetComponent<MapComponent_TacticalCommands>();
            if(owner!=null) __result=owner.SelectUnderbarrel(__instance,target,__result);
        }
    }
}
