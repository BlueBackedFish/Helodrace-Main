using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Helodrace.Tactical;
using RimWorld;
using Verse;
using GV=UnityEngine.Vector3;
using NV=System.Numerics.Vector3;

namespace Helodrace.Tests
{
    internal static class NativeTacticalGeometryChecks
    {
        private const BindingFlags All=BindingFlags.Instance|BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic;
        private static readonly Type Aim=typeof(TacticalAimUtility),Focus=typeof(Hediff_TacticalAim),Strike=typeof(TacticalCloseQuartersStrikeUtility),Coord=typeof(TacticalCoordinatedAimUtility),Slide=typeof(TacticalHighSpeedMovementUtility);
        private static readonly Type FAim=typeof(FrozenTactical.AimGeometry),FStrike=typeof(FrozenTactical.StrikeGeometry),FCoord=typeof(FrozenTactical.CoordinatedGeometry),FSlide=typeof(FrozenTactical.TacticalHighSpeedMovementUtility);
        private static readonly Dictionary<Pawn,GV> positions=new();
        private static bool enabled,aiming,visualEnabled=true,formation=true,lineOfSight=true,canHit=true,slidePhase;
        private static float visualAngle,range=100,ticksPerCell=20;
        private static IntVec3? walkBlock,standBlock;
        private static List<IntVec3> scan;
        private sealed record Line(GV From,GV To,SimpleColor Color,float Width);
        private static List<Line> lines;
        private static int components,focusCases,targetCases,focusCellCases,visualCells,equipmentCases,strikes,arcs,acceptedArcs,draws,slideProfiles,slideSteps,pathCases,scanCells,bitStates;
        private static float maximumError;
        private static readonly Dictionary<string,float> differences=new();
        private static object Invoke(Type t,string name,params object[] args)=>t.GetMethod(name,All).Invoke(null,args);
        private static IDictionary States(Type t,string name)=>(IDictionary)t.GetField(name,All).GetValue(null);
        private static object Field(object o,string name)=>o.GetType().GetField(name,All).GetValue(o);
        private static void Set(object o,string name,object value)=>o.GetType().GetField(name,All).SetValue(o,value);
        private static void Near(float a,float e,string scope)
        {components++;float error=Math.Abs(a-e);maximumError=Math.Max(maximumError,error);if(error>0)differences[scope]=Math.Max(differences.GetValueOrDefault(scope),error);if(!float.IsFinite(a)||!float.IsFinite(e)||error>2e-5f*Math.Max(1,Math.Abs(e)))throw new InvalidOperationException(scope+": "+a+" != "+e);}
        private static void Vector(GV a,GV e,string scope){Near(a.x,e.x,scope);Near(a.y,e.y,scope);Near(a.z,e.z,scope);}
        private static void Bits(float a,float e){bitStates++;if(BitConverter.SingleToInt32Bits(a)!=BitConverter.SingleToInt32Bits(e))throw new InvalidOperationException("Tactical scalar state bits changed.");}
        private static bool Position(Pawn __instance,ref GV __result){if(!enabled||!positions.TryGetValue(__instance,out __result))return true;return false;}
        private static bool Training(ref bool __result){if(!enabled)return true;__result=true;return false;}
        private static bool Aiming(ref bool __result){if(!enabled)return true;__result=aiming;return false;}
        private static bool Visual(ref float __result){if(!enabled)return true;__result=visualAngle;return false;}
        private static bool Equipment(ref float __1,ref bool __result){if(!enabled)return true;__1=visualAngle;__result=visualEnabled;return false;}
        private static bool Formation(ref bool __result){if(!enabled)return true;__result=formation;return false;}
        private static bool Sight(ref bool __result){if(!enabled)return true;__result=lineOfSight;return false;}
        private static bool Hit(ref bool __result){if(!enabled)return true;__result=canHit;return false;}
        private static bool Range(ref float __result){if(!enabled)return true;__result=range;return false;}
        private static bool SlideGate(ref bool __result){if(!enabled||!slidePhase)return true;__result=true;return false;}
        private static bool MoveTicks(ref float __result){if(!enabled||!slidePhase)return true;__result=ticksPerCell;return false;}
        private static bool Walk(IntVec3 __0,ref bool __result){if(!enabled||!slidePhase)return true;scan?.Add(__0);__result=__0!=walkBlock;return false;}
        private static bool Stand(IntVec3 __0,ref bool __result){if(!enabled||!slidePhase)return true;__result=__0!=standBlock;return false;}
        private static bool Draw(GV __0,GV __1,SimpleColor __2,float __3){if(lines==null)return true;lines.Add(new(__0,__1,__2,__3));return false;}
        private static void OutAngle(Type a,Type e,string name,params object[] input)
        {object[] aa=(object[])input.Clone(),ee=(object[])input.Clone();bool av=(bool)Invoke(a,name,aa),ev=(bool)Invoke(e,name,ee);if(av!=ev)throw new InvalidOperationException(name+" branch changed.");Near((float)aa[^1],(float)ee[^1],name);}
        private static void Aims(Pawn pawn,Pawn target,Thing weapon)
        {
            foreach(GV origin in new[]{GV.zero,new GV(-0f,4,float.Epsilon),new GV(100.25f,-2,90.5f)})
                foreach(GV offset in new[]{GV.zero,new GV(.031622774f,7,0),new GV(MathF.BitIncrement(.031622774f),-3,0),new GV(1,0,0),new GV(0,9,-1),new GV(-1,0,1),new GV(17,8,-23)})foreach(float fallback in new[]{-720f,0,37,359.99f})
                {GV point=origin+offset;Near((float)Invoke(Focus,"AngleToFocus",new NV(origin.x,origin.y,origin.z),new NV(point.x,point.y,point.z),fallback),(float)Invoke(FAim,"AngleToFocus",origin,point,fallback),"focus direction");focusCases++;}
            foreach(GV origin in new[]{pawn.Position.ToVector3Shifted(),new GV(100.5f,-3,110.25f)})foreach(GV offset in new[]{GV.zero,new GV(.001f,5,0),new GV(.031622774f,0,0),new GV(1,0,1),new GV(-1,0,-1),new GV(14,8,-17)})
            {
                positions[pawn]=origin;positions[target]=origin+offset;
                foreach(LocalTargetInfo t in new[]{new LocalTargetInfo(target),new LocalTargetInfo(target.Position),new LocalTargetInfo(pawn.Position),LocalTargetInfo.Invalid}){OutAngle(Aim,FAim,"TryGetTargetAngle",pawn,t,0f);targetCases++;}
            }
            foreach(IntVec3 offset in new[]{IntVec3.Zero,new IntVec3(1,0,0),new IntVec3(-1,0,1),new IntVec3(7,0,-11),new IntVec3(0,3,-1)})
            {OutAngle(Aim,FAim,"TryGetFocusAngle",pawn,pawn.Position+offset,0f);focusCellCases++;}
            var angles=new List<float>{-720f,-180,-90,-.001f,-0f,0f,2.388f,30f,45,89.999f,90,179.99f,180,270,359.99f,720};
            for(int cell=0;cell<12;cell++){float boundary=(float)(Math.Asin((cell+.5)/12)*180/Math.PI);foreach(float adjacent in new[]{MathF.BitDecrement(boundary),boundary,MathF.BitIncrement(boundary)})foreach(float quadrant in new[]{0f,90,180,270})angles.Add(adjacent+quadrant);}
            foreach(float angle in angles)
            {
                visualAngle=angle;if(!Equals(Invoke(Aim,"VisualAimTargetCell",pawn),Invoke(FAim,"VisualAimTargetCell",pawn)))throw new InvalidOperationException("Visual target grid cell changed.");visualCells++;
                foreach(float previous in new[]{-180f,0,37,359.99f})foreach(GV point in new[]{GV.zero,new GV(1.25f,-0f,-4.5f),new GV(-20,9,30)})foreach(bool on in new[]{false,true})
                {
                    visualEnabled=on;object[] aa={weapon,point,previous},ee={weapon,point,previous};
                    Invoke(typeof(Patch_DrawEquipmentAiming_TacticalAim),"Prefix",aa);Invoke(FAim,"Prefix",ee);Vector((GV)aa[1],(GV)ee[1],"aim equipment offset");Near((float)aa[2],(float)ee[2],"equipment aim");Bits(((GV)aa[1]).y,((GV)ee[1]).y);equipmentCases++;
                }
            }
            visualEnabled=true;
        }
        private static void Strikes(Pawn pawn,Pawn victim,Thing weapon,int baseTick)
        {
            foreach(GV offset in new[]{GV.zero,new GV(.001f,4,0),new GV(1,2,0),new GV(-1,0,1),new GV(7,0,-3)})foreach(bool aim in new[]{false,true})foreach(int elapsed in new[]{-1,0,1,6,7,8,12,23,24,25})
            {
                positions[pawn]=new GV(120.25f,3,110.5f);positions[victim]=positions[pawn]+offset;aiming=aim;visualAngle=359.5f;Find.TickManager.DebugSetTicksGame(baseTick);
                States(Strike,"strikeAnimations").Clear();States(FStrike,"strikeAnimations").Clear();Invoke(Strike,"BeginStrikeAnimation",pawn,victim);Invoke(FStrike,"BeginStrikeAnimation",pawn,victim);
                var a=States(Strike,"strikeAnimations");var e=States(FStrike,"strikeAnimations");if(a.Count!=e.Count)throw new InvalidOperationException("Strike animation start gate changed.");
                if(a.Count>0)foreach(string field in new[]{"startTick","targetAngle","returnAngle"}){if(field=="startTick"){if(!Equals(Field(a[pawn.thingIDNumber],field),Field(e[pawn.thingIDNumber],field)))throw new InvalidOperationException("Strike start time changed.");}else Near((float)Field(a[pawn.thingIDNumber],field),(float)Field(e[pawn.thingIDNumber],field),"strike state");}
                Find.TickManager.DebugSetTicksGame(baseTick+elapsed);object[] aa={pawn,0f,0f},ee={pawn,0f,0f};if(!Equals(Invoke(Strike,"TryGetVisual",aa),Invoke(FStrike,"TryGetVisual",ee)))throw new InvalidOperationException("Strike expiry gate changed.");Near((float)aa[1],(float)ee[1],"strike visual angle");Near((float)aa[2],(float)ee[2],"strike thrust");
                object[] ad={weapon,new NV(121.5f,-0f,111.75f),37f},ed={weapon,new GV(121.5f,-0f,111.75f),37f};Invoke(Strike,"AdjustEquipmentDraw",ad);Invoke(FStrike,"AdjustEquipmentDraw",ed);NV actualDraw=(NV)ad[1];Vector(new GV(actualDraw.X,actualDraw.Y,actualDraw.Z),(GV)ed[1],"strike equipment");Near((float)ad[2],(float)ed[2],"strike aim");if(a.Count!=e.Count)throw new InvalidOperationException("Strike cleanup changed.");strikes++;
            }
            aiming=false;Find.TickManager.DebugSetTicksGame(baseTick);
        }
        private static object CoordinateState(Type type,Pawn pawn,Pawn partner,Verb verb)
        {
            if(type==FCoord)return Invoke(FCoord,"FixtureState",pawn,partner,verb);
            object state=Activator.CreateInstance(type.GetNestedType("State",All),true);
            foreach(var pair in new (string,object)[]{("shooter",pawn),("partner",partner),("verb",verb),("shooterOrigin",pawn.Position),("partnerOrigin",partner.Position)})Set(state,pair.Item1,pair.Item2);return state;
        }
        private static void Coordinated(Pawn pawn,Pawn partner,Pawn victim,Verb verb)
        {
            object a=CoordinateState(Coord,pawn,partner,verb),e=CoordinateState(FCoord,pawn,partner,verb);States(Coord,"states")[pawn.thingIDNumber]=a;States(FCoord,"states")[pawn.thingIDNumber]=e;
            foreach(float reference in new[]{0f,30,90,180,270,359.99f})foreach(float distance in new[]{.5f,20,100})
            {
                range=distance;positions[pawn]=new GV(120.25f,1,110.5f);positions[partner]=positions[pawn]+GV.forward.RotatedBy(reference);
                foreach(float delta in new[]{-30.001f,-30f,-29.999f,0f,29.999f,30f,30.001f,180f})foreach(bool hit in new[]{false,true})foreach(bool sight in new[]{false,true})
                {
                    canHit=hit;lineOfSight=sight;positions[victim]=positions[pawn]+GV.forward.RotatedBy(reference+delta)*5;
                    bool av=(bool)Invoke(Coord,"TargetInPartnerArc",a,new LocalTargetInfo(victim)),ev=(bool)Invoke(FCoord,"TargetInPartnerArc",e,new LocalTargetInfo(victim));if(av!=ev)throw new InvalidOperationException("Coordinated target arc/range/LOS branch changed.");if(av)acceptedArcs++;arcs++;
                }
                foreach(bool valid in new[]{false,true})
                {
                    formation=valid;lines=new();TacticalCoordinatedAimUtility.DrawActive(pawn.Map);var actual=lines.ToArray();lines.Clear();Invoke(FCoord,"DrawActive",pawn.Map);if(actual.Length!=lines.Count||actual.Length!=(valid?3:0))throw new InvalidOperationException("Coordinated draw gate changed.");
                    for(int i=0;i<actual.Length;i++){Vector(actual[i].From,lines[i].From,"coordinated start");Vector(actual[i].To,lines[i].To,"coordinated end");Near(actual[i].Width,lines[i].Width,"coordinated width");if(actual[i].Color!=lines[i].Color)throw new InvalidOperationException("Coordinated color changed.");}draws++;lines=null;
                }
            }
            canHit=lineOfSight=formation=true;
        }
        private static bool Lane(Type type,Pawn pawn,IntVec3 destination)=> (bool)Invoke(type,"HasClearSlidePath",pawn,destination);
        private static void Scans(IntVec3[] actual)
        {if(!actual.SequenceEqual(scan))throw new InvalidOperationException("Slide path sampled different cells/order.");scanCells+=actual.Length;scan=null;}
        private static void Slides(Pawn pawn)
        {
            slidePhase=true;IntVec3 origin=pawn.Map.Center;
            var directions=new[]{IntVec3.Zero,new IntVec3(1,0,0),new IntVec3(-1,0,0),new IntVec3(0,0,1),new IntVec3(0,0,-1),new IntVec3(3,0,4),new IntVec3(-3,0,4),new IntVec3(-7,0,-9),new IntVec3(11,0,-2)};
            foreach(IntVec3 direction in directions)foreach(float speed in new[]{1f,3.5f,20f})foreach(int blocked in new[]{0,1,2,3})
            {
                pawn.SetPositionDirect(origin);walkBlock=standBlock=null;lineOfSight=true;ticksPerCell=speed;IntVec3 destination=origin+direction;scan=new();Lane(FSlide,pawn,destination);var samples=scan.ToArray();scan=null;
                if(samples.Length>0){if(blocked==1)walkBlock=samples[samples.Length/2];if(blocked==2)standBlock=samples[0];}if(blocked==3)lineOfSight=false;
                scan=new();bool pa=Lane(Slide,pawn,destination);var actual=scan.ToArray();scan.Clear();bool pe=Lane(FSlide,pawn,destination);if(pa!=pe)throw new InvalidOperationException("Slide clear-path branch changed.");Scans(actual);pathCases++;
                Invoke(Slide,"InitializeSlide",pawn,destination);Invoke(FSlide,"InitializeSlide",pawn,destination);
                OutAngle(Slide,FSlide,"TryGetSlideAngle",pawn,0f);
                bool ended=false;
                for(int step=0;step<1000;step++)
                {
                    Near((float)Invoke(Slide,"MovementTicksMultiplierFor",pawn),(float)Invoke(FSlide,"MovementTicksMultiplierFor",pawn),"slide speed curve");IntVec3 before=pawn.Position;
                    scan=new();string av=Invoke(Slide,"AdvanceSlide",pawn).ToString();var pos=pawn.Position;actual=scan.ToArray();scan.Clear();pawn.SetPositionDirect(before);string ev=Invoke(FSlide,"AdvanceSlide",pawn).ToString();if(av!=ev||pos!=pawn.Position)throw new InvalidOperationException("Slide advance result/cell changed.");Scans(actual);
                    object a=States(Slide,"activeStates")[pawn.thingIDNumber],e=States(FSlide,"activeStates")[pawn.thingIDNumber];Bits((float)Field(a,"travelledDistance"),(float)Field(e,"travelledDistance"));foreach(string f in new[]{"origin","destination","sliding"})if(!Equals(Field(a,f),Field(e,f)))throw new InvalidOperationException("Slide state changed: "+f);slideSteps++;
                    if(av!="Continue"){ended=true;break;}
                }
                if(!ended)throw new InvalidOperationException("Slide fixture did not finish within its expected distance/speed bound.");slideProfiles++;
            }
            slidePhase=false;scan=null;walkBlock=standBlock=null;lineOfSight=true;
        }
        internal static void Run(string output,Action<bool,string> check)
        {
            var h=new Harmony("helodrace.tests.native-tactical-geometry");
            void P(Type t,string name,string prefix,params Type[] args)=>h.Patch(AccessTools.Method(t,name,args),prefix:new HarmonyMethod(typeof(NativeTacticalGeometryChecks),prefix));
            void G(Type t,string name,string prefix)=>h.Patch(AccessTools.PropertyGetter(t,name),prefix:new HarmonyMethod(typeof(NativeTacticalGeometryChecks),prefix));
            G(typeof(Pawn),"DrawPos",nameof(Position));G(typeof(Pawn),"TicksPerMoveCardinal",nameof(MoveTicks));G(typeof(Verb),"EffectiveRange",nameof(Range));
            P(Aim,"HasCQBTraining",nameof(Training),typeof(Pawn));P(Aim,"HasAdvancedCQBTraining",nameof(Training),typeof(Pawn));P(Aim,"IsAiming",nameof(Aiming),typeof(Pawn));P(Aim,"VisualAimAngle",nameof(Visual),typeof(Pawn));P(Aim,"TryGetEquipmentVisualAngle",nameof(Equipment),typeof(Thing),typeof(float).MakeByRefType());
            foreach(Type t in new[]{Coord,FCoord})P(t,"CanSelectPartner",nameof(Formation),typeof(Pawn),typeof(Pawn));
            foreach(Type t in new[]{Slide,FSlide})foreach(string method in new[]{"IsActive","IsSliding"})P(t,method,nameof(SlideGate),typeof(Pawn));
            foreach(MethodInfo method in AccessTools.GetDeclaredMethods(typeof(GenSight)).Where(m=>m.Name=="LineOfSight"))h.Patch(method,prefix:new HarmonyMethod(typeof(NativeTacticalGeometryChecks),nameof(Sight)));
            P(typeof(Verb),"CanHitTarget",nameof(Hit),typeof(LocalTargetInfo));P(typeof(GenGrid),"Walkable",nameof(Walk),typeof(IntVec3),typeof(Map));P(typeof(GenGrid),"Standable",nameof(Stand),typeof(IntVec3),typeof(Map));
            P(typeof(GenDraw),"DrawLineBetween",nameof(Draw),typeof(GV),typeof(GV),typeof(SimpleColor),typeof(float));
            int tick=Find.TickManager.TicksGame;var pawns=new List<Pawn>();enabled=true;
            try
            {
                Map map=Find.CurrentMap;
                for(int i=0;i<3;i++){var p=PawnGenerator.GeneratePawn(new PawnGenerationRequest(DefDatabase<PawnKindDef>.GetNamed("HD_WW_HelodColonist"),Faction.OfPlayer,canGeneratePawnRelations:false));pawns.Add(p);GenSpawn.Spawn(p,map.Center+new IntVec3(i,0,0),map);p.drafter.Drafted=true;}
                Pawn pawn=pawns[0],partner=pawns[1],victim=pawns[2];var weapon=(ThingWithComps)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("HD_Gun_M16A4_Weapon"));pawn.equipment.AddEquipment(weapon);Verb verb=weapon.GetComp<CompEquippable>().PrimaryVerb;
                foreach(var pair in new[]{(Strike,"strikeAnimations"),(FStrike,"strikeAnimations"),(Coord,"states"),(FCoord,"states"),(Slide,"activeStates"),(FSlide,"activeStates")})if(States(pair.Item1,pair.Item2).Count!=0)throw new InvalidOperationException("Use isolated tactical state fixtures.");
                File.WriteAllText(Path.Combine(output,"native-tactical-phase.txt"),"aim\n");Aims(pawn,victim,weapon);
                File.AppendAllText(Path.Combine(output,"native-tactical-phase.txt"),"strike\n");Strikes(pawn,victim,weapon,tick);
                File.AppendAllText(Path.Combine(output,"native-tactical-phase.txt"),"coordinated\n");Coordinated(pawn,partner,victim,verb);
                partner.DeSpawn();victim.DeSpawn();positions.Clear();File.AppendAllText(Path.Combine(output,"native-tactical-phase.txt"),"slide\n");Slides(pawn);
                check(focusCases==84&&targetCases==48&&focusCellCases==5&&visualCells==160,"native aim direction/gates and rounded visual target cells match frozen geometry");
                check(equipmentCases==3840&&strikes==100,"native aiming offsets and live strike animation/expiry/thrust match frozen equipment geometry");
                check(arcs==576&&acceptedArcs>0&&draws==36,"coordinated arc/range/LOS branches and native fan endpoints match frozen behavior");
                check(slideProfiles==108&&pathCases==108&&slideSteps>108,"native slide lane sampling, every advance cell/result/state and speed profile match frozen utility");
            }
            finally
            {
                File.WriteAllText(Path.Combine(output,"native-tactical-geometry.json"),JsonSerializer.Serialize(new{Focus=focusCases,Targets=targetCases,FocusCells=focusCellCases,VisualCells=visualCells,Equipment=equipmentCases,Strikes=strikes,Arcs=arcs,AcceptedArcs=acceptedArcs,Draws=draws,SlideProfiles=slideProfiles,SlideSteps=slideSteps,Paths=pathCases,ScanCells=scanCells,BitStates=bitStates,Components=components,MaximumFloatError=maximumError,NonzeroDifferences=differences,Baseline="68ad3bf5",ControlledDrawPositionsTrainingAimHitSightRangeGridAndSlideGates=true},new JsonSerializerOptions{WriteIndented=true}));
                enabled=slidePhase=false;scan=null;lines=null;positions.Clear();foreach(var pair in new[]{(Strike,"strikeAnimations"),(FStrike,"strikeAnimations"),(Coord,"states"),(FCoord,"states"),(Slide,"activeStates"),(FSlide,"activeStates")})States(pair.Item1,pair.Item2).Clear();h.UnpatchAll(h.Id);Find.TickManager.DebugSetTicksGame(tick);foreach(Pawn p in pawns)if(!p.Destroyed)p.Destroy();
            }
        }
    }
}
