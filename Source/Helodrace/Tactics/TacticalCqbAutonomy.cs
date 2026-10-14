using System;
using System.Diagnostics;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tactics
{
    public static class TacticalCqbAutonomyPolicy
    {
        public const int NoShotTicks=180;
        public static bool Refresh(bool initial,bool coverLost,int blockedSince,int tick) => initial || coverLost
            || blockedSince>=0 && tick-blockedSince>=NoShotTicks;
        public static bool LocalCell(IntVec3 anchor,IntVec3 cell,bool sameRoom,bool door) => sameRoom && !door
            && Math.Abs(cell.x-anchor.x)+Math.Abs(cell.z-anchor.z)<=2;
        public static IntVec3 FindReturn(IntVec3 origin,Func<IntVec3,bool> floor,Func<IntVec3,bool> safe,Func<IntVec3,int> cost)
        {
            var cells=new IntVec3[TacticalEntryCombatPolicy.CoverCells]; cells[0]=origin; int count=1;
            IntVec3 best=IntVec3.Invalid; int bestCost=int.MaxValue;
            for(int cursor=0;cursor<count;cursor++)
            {
                IntVec3 cell=cells[cursor];
                if(safe(cell)) { int value=cost(cell); if(value<bestCost) { best=cell; bestCost=value; } }
                foreach(var direction in GenAdj.CardinalDirections)
                {
                    IntVec3 next=cell+direction;
                    if(Math.Abs(next.x-origin.x)+Math.Abs(next.z-origin.z)>2) continue;
                    bool seen=false; for(int i=0;i<count;i++) if(cells[i]==next) { seen=true; break; }
                    if(!seen && count<cells.Length && floor(next)) cells[count++]=next;
                }
            }
            return best;
        }
        // Both ends of the actual firing line stay on the approach side. Never
        // shoot across the selected opening while its support action owns it.
        public static bool RearLine(TacticalLocalPlan plan,IntVec3 from,IntVec3 to,bool charge)
        {
            if(plan==null || plan.Direct || !TacticalPostFirePolicy.CanStop(plan,from)) return false;
            IntVec3 a=from-plan.Opening,b=to-plan.Opening;
            int da=a.x*plan.Inward.x+a.z*plan.Inward.z,db=b.x*plan.Inward.x+b.z*plan.Inward.z;
            if(da>=0 || db>=0) return false;
            if(!charge) return true;
            int wa=a.x*-plan.Inward.z+a.z*plan.Inward.x,wb=b.x*-plan.Inward.z+b.z*plan.Inward.x;
            return da<=-6 && db<=-6 || wa>=2 && wb>=2 || wa<=-2 && wb<=-2;
        }
    }
    public sealed partial class TacticalMemberCommand
    {
        public string EntryCoverOwner;
        public int EntryCoverVersion=-1,EntryCoverCheckAt,EntryNoShotSince=-1,EntryNoShotId=-1;
        public IntVec3 EntryCoverAnchor=IntVec3.Invalid;
        public Thing EntryCoverObject;
        public bool EntryHadCover;
    }
    public sealed partial class MapComponent_TacticalCommands
    {
        // No region rebuilds are triggered by optional local autonomy. A dirty
        // native room means defer, not run a new map survey in this actor's tick.
        private Room CurrentRoom(IntVec3 cell) => cell.InBounds(map)
            ? map.regionGrid.GetValidRegionAt_NoRebuild(cell)?.District?.Room : null;
        private bool TryLocalEntryCover(TacticalSquadCommand command,TacticalMemberCommand member,int index,int tick)
        {
            if(member.EntryCoverOwner!=command.Id || member.EntryCoverVersion!=command.PlanVersion)
            {
                member.EntryCoverOwner=command.Id; member.EntryCoverVersion=command.PlanVersion;
                member.EntryCoverAnchor=command.Plan.Positions[index]; member.EntryCoverCheckAt=0;
                member.EntryHadCover=false; member.EntryCoverObject=null; member.EntryNoShotSince=-1;
            }
            if(tick<member.EntryCoverCheckAt || !command.EntryCombatFocus.IsValid
                || !TacticalEntryCombatPolicy.Engaged(command.EntryCombatUntil,tick)) return false;
            var plan=command.Plan;
            bool initial=!plan.EntryCoverChecked.Contains(index);
            if(!plan.Direct && !TacticalCqbProgressPolicy.Physical(member,command.PlanVersion,command.Id)) return false;
            bool lost=member.EntryHadCover && (member.EntryCoverObject==null || !member.EntryCoverObject.Spawned
                || member.EntryCoverObject.Destroyed || member.EntryCoverObject is Building_Door door && door.Open);
            Pawn enemy=command.EntryCombatFocus.GetFirstPawn(map);
            if(enemy?.HostileTo(member.Pawn)!=true || enemy.Dead || enemy.Downed) enemy=null;
            Verb gun=member.Pawn.equipment?.PrimaryEq?.PrimaryVerb;
            // A stable post checks LOS only on its staggered cadence. Initial
            // selection/cover loss must not repeat LOS for every denied budget.
            if(!initial && !lost)
            {
                member.EntryCoverCheckAt=tick+90+member.Pawn.thingIDNumber%30;
                if(enemy==null || gun==null || gun.CanHitTargetFrom(member.Pawn.Position,enemy)) member.EntryNoShotSince=-1;
                else if(member.EntryNoShotSince<0 || member.EntryNoShotId!=enemy.thingIDNumber) member.EntryNoShotSince=tick;
                member.EntryNoShotId=enemy?.thingIDNumber??-1;
            }
            if(!TacticalCqbAutonomyPolicy.Refresh(initial,lost,member.EntryNoShotSince,tick))
            { member.EntryCoverCheckAt=tick+90+member.Pawn.thingIDNumber%30; return false; }
            IntVec3 old=plan.Positions[index];
            Room room=CurrentRoom(old);
            if(room==null) { member.EntryCoverCheckAt=tick+10; return false; }
            var budget=Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if(!budget.TryPlan(tick)) return false;
            long started=Stopwatch.GetTimestamp();
            try
            {
                bool Floor(IntVec3 cell) => cell.InBounds(map) && cell.Walkable(map)
                    && TacticalCqbAutonomyPolicy.LocalCell(member.EntryCoverAnchor,cell,CurrentRoom(cell)==room,cell.GetEdifice(map) is Building_Door)
                    && TacticalPostFirePolicy.CanStop(plan,cell)
                    && (plan.Direct || (cell.x-plan.Opening.x)*plan.Inward.x+(cell.z-plan.Opening.z)*plan.Inward.z>=plan.Depth);
                bool Free(IntVec3 cell) => cell.Standable(map) && !claims.ContainsKey(cell)
                    && !plan.Positions.Contains(cell) && cell.GetFirstPawn(map)==null;
                float Score(IntVec3 cell)
                {
                    float cover=CoverUtility.CalculateOverallBlockChance(cell,command.EntryCombatFocus,map);
                    if(enemy!=null && gun!=null) return gun.CanHitTargetFrom(cell,enemy)?cover+.25f:0f;
                    return cover;
                }
                IntVec3 selected=TacticalEntryCombatPolicy.FindCover(old,Floor,Free,Score);
                plan.EntryCoverChecked.Add(index);
                member.EntryCoverCheckAt=tick+90+member.Pawn.thingIDNumber%30;
                member.EntryNoShotSince=-1;
                member.EntryCoverObject=null; float best=0;
                foreach(var side in GenAdj.AdjacentCells)
                {
                    IntVec3 cell=selected+side; if(!cell.InBounds(map)) continue;
                    Thing cover=cell.GetCover(map);
                    float value=cover?.BaseBlockChance()??0;
                    if(cover is Building_Door open && open.Open) value=0;
                    if(value>best) { best=value; member.EntryCoverObject=cover; }
                }
                member.EntryHadCover=member.EntryCoverObject!=null;
                if(selected!=old)
                {
                    plan.Positions[index]=selected; plan.Interior.Add(selected); claims[selected]=command;
                    if(!OriginalClaim(command,old) && claims.TryGetValue(old,out var owner) && owner==command) claims.Remove(old);
                    member.EntryAssignmentDone=member.PostReady=false;
                }
                return true;
            }
            finally { budget.Account(tick,Stopwatch.GetTimestamp()-started); }
        }
        private bool RearFireRole(TacticalSquadCommand command,Pawn pawn)
        {
            if(command.Terminal || command.Plan==null || !TacticalPostFirePolicy.CanStop(command.Plan,pawn.Position)) return false;
            return command.OpeningAction?.Observer!=pawn && command.OpeningAction?.Thrower!=pawn
                && command.ChargeAction?.Installer!=pawn && command.ChargeAction?.EffectOperator!=pawn;
        }
        private bool CqbShotAllowed(TacticalSquadCommand command,Pawn pawn,IntVec3 target,IntVec3? leanSource=null)
        {
            if(TacticalPostFirePolicy.SafeToFire(command)) return true;
            IntVec3 source=leanSource??pawn.Position;
            if(!RearFireRole(command,pawn) || !TacticalCqbAutonomyPolicy.RearLine(command.Plan,source,target,
                command.ChargeAction!=null && !command.ChargeAction.EffectsCleared)) return false;
            var action=command.OpeningAction;
            if(action?.Launched==true && !action.EffectsCleared && action.ProjectileDef!=null)
            {
                float radius=TacticalGrenadePolicy.HazardRadius(action.ProjectileDef,!action.Outdoors)+.75f;
                if(source.DistanceToSquared(action.Target)<=radius*radius
                    && (TacticalGrenadePolicy.DangerousThroughCover(action.ProjectileDef)
                        || GenSight.LineOfSight(action.Target,source,map,true))) return false;
            }
            return true;
        }
        private bool CqbShotLineAllowed(TacticalSquadCommand command,Pawn pawn,Verb gun,Pawn enemy) =>
            TacticalPostFirePolicy.SafeToFire(command) || gun!=null
                && gun.TryFindShootLineFromTo(pawn.Position,enemy,out ShootLine line)
                && CqbShotAllowed(command,pawn,line.Dest,line.Source);
        internal bool CqbThrowOwned(Pawn pawn,Job job) => byPawn.TryGetValue(pawn,out var c)
            && c.Members.Find(m=>m.Pawn==pawn)?.Job==job && c.OpeningAction?.Thrower==pawn
            && c.OpeningAction.Target==job.targetA.Cell;
        internal bool EnsureSupportReturn(Pawn pawn,Job job,IntVec3 avoid)
        {
            if(!CqbThrowOwned(pawn,job)) return true; // Field smoke keeps its own action/return ownership.
            var c=byPawn[pawn]; var action=c.OpeningAction; var plan=c.Plan;
            bool Clear(IntVec3 cell)
            {
                if(!cell.InBounds(map) || !cell.Standable(map) || cell.GetEdifice(map) is Building_Door) return false;
                foreach(Thing thing in cell.GetThingList(map)) if(thing is Pawn other && other!=pawn) return false;
                return true;
            }
            IntVec3 current=action.ReturnPosition;
            ThingDef projectile=action.ProjectileDef??job.targetB.Thing?.def.projectileWhenLoaded;
            if(projectile?.projectile==null) return false;
            float radius=TacticalGrenadePolicy.HazardRadius(projectile,!action.Outdoors)
                +InventoryGrenadeUtility.ThrowMissRadius(pawn,action.Source.DistanceTo(action.Target))+.75f;
            bool LiveSafe(IntVec3 cell) => RaidSmokeUtility.IsScreeningProjectile(projectile)
                || !action.SupportRoomCells.Contains(cell) && (cell.DistanceToSquared(action.Target)>radius*radius
                    || !TacticalGrenadePolicy.DangerousThroughCover(projectile) && !GenSight.LineOfSight(action.Target,cell,map,true));
            if(current!=avoid && Clear(current) && (!action.Launched || LiveSafe(current))) return true;
            int tick=GenTicks.TicksGame;
            var budget=Current.Game.GetComponent<GameComponent_TacticalCommands>().WorkBudget;
            if(!budget.TryPlan(tick)) return false;
            long started=Stopwatch.GetTimestamp();
            try
            {
                Room room=CurrentRoom(pawn.Position); if(room==null) return false;
                int self=c.Members.FindIndex(m=>m.Pawn==pawn);
                bool Floor(IntVec3 cell) => Clear(cell) && CurrentRoom(cell)==room
                    && TacticalCqbAutonomyPolicy.LocalCell(current,cell,true,false)
                    && (cell.x-plan.Opening.x)*plan.Inward.x+(cell.z-plan.Opening.z)*plan.Inward.z<plan.Depth;
                bool Free(IntVec3 cell) => cell!=avoid && Floor(cell) && TacticalPostFirePolicy.CanStop(plan,cell)
                    && (!claims.ContainsKey(cell) || cell==current)
                    && (plan.Stack.IndexOf(cell)<0 || plan.Stack.IndexOf(cell)==self);
                bool Safe(IntVec3 cell)
                {
                    if(!Free(cell)) return false;
                    if(!action.Launched) return SafeOpeningThrow(pawn,job.targetB.Thing,pawn.Position,action.Target,cell);
                    return LiveSafe(cell);
                }
                IntVec3 selected=TacticalCqbAutonomyPolicy.FindReturn(pawn.Position,Floor,Safe,
                    cell=>cell.DistanceToSquared(current)+cell.DistanceToSquared(pawn.Position));
                if(!selected.IsValid) return false;
                action.ReturnPosition=selected; action.Returned=false; job.targetQueueA[0]=selected;
                plan.Stack[self]=selected; claims[selected]=c;
                if(!OriginalClaim(c,current) && claims.TryGetValue(current,out var owner) && owner==c) claims.Remove(current);
                return true;
            }
            finally { budget.Account(tick,Stopwatch.GetTimestamp()-started); }
        }
    }
}
