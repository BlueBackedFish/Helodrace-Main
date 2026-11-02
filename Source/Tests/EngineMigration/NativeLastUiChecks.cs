using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HarmonyLib;
using Helodrace.ResearchUI;
using RimWorld;
using RimWorld.Planet;
using Verse;
using GR=UnityEngine.Rect;

namespace Helodrace.Tests
{
    internal static partial class NativeWindowSubmissionChecks
    {
        private static bool lastUiActive,lastClickAway,lastCanvasAccepted;
        private static string lastReject;
        private static float lastOverflow;
        private static MainTabWindow_Research lastResearchWindow;
        private static HelodForwardBase lastBase;
        private static int lastSniperFrames,lastResearchFrames,lastRequests,lastTargets,lastCanvasCalls,lastTabCalls,lastCloseCalls,lastMessages;
        private static readonly HashSet<string> lastTabAdapterSites=new();
        private sealed class ForeignResearchWindow:MainTabWindow_Research { }
        private static T LastBare<T>()=>(T)RuntimeHelpers.GetUninitializedObject(typeof(T));
        private static bool LastRejection(ref string __result){if(!lastUiActive||!active)return true;__result=lastReject;Add("sniper-rejection",default,lastReject);return false;}
        private static void LastRequest(HelodForwardBase __0,HelodSniperSupportDirection __1)
        {if(!lastUiActive||!active)return;if(!ReferenceEquals(__0,lastBase))throw new InvalidOperationException("Support request base changed.");lastRequests++;Add("sniper-request",default,__1.ToString());}
        private static bool LastTarget(Map __0,HelodSniperSupportMode __1,HelodForwardBase __2)
        {if(!lastUiActive||!active)return true;if(__0!=Find.CurrentMap||__2!=lastBase)throw new InvalidOperationException("Targeting context changed.");lastTargets++;Add("sniper-target",default,__1.ToString());return false;}
        private static bool LastMessage(string __0){if(!lastUiActive||!active)return true;lastMessages++;Add("message",default,__0);return false;}
        private static bool LastClose(Window __instance){if(!lastUiActive||!active)return true;lastCloseCalls++;Add("window-close",default,__instance.GetType().Name);return false;}
        private static bool LastOverflow(GR __0,List<TabRecord> __1,float __2,float __3,ref float __result)
        {if(!lastUiActive||!active)return true;Add("overflow-height",__0,null,__1.Count,__2,__3);__result=lastOverflow;return false;}
        private static bool LastTabs(GR __0,List<TabRecord> __1,float __2,float __3,ref TabRecord __result)
        {
            if(!lastUiActive||!active)return true;lastTabCalls++;Add("tabs-overflow",__0,null,__1.Count,__2,__3);
            if(lastClickAway)AccessTools.Field(typeof(MainTabWindow_Research),"curTabInt").SetValue(lastResearchWindow,ResearchTabDefOf.Main);__result=null;return false;
        }
        private static bool LastCanvas(Window __0,Waylith.Math.Rect __1,ref bool __result)
        {if(!lastUiActive||!active)return true;if(__0!=lastResearchWindow)throw new InvalidOperationException("Research viewport delivered to wrong window.");lastCanvasCalls++;Add("research-canvas",new GR(__1.x,__1.y,__1.width,__1.height));__result=lastCanvasAccepted;return false;}
        private static float LastOverflowBoundary(GR rect,List<TabRecord> tabs,float min,float max)
        {float result=0;return LastOverflow(rect,tabs,min,max,ref result)?TabDrawer.GetOverflowTabHeight(rect,tabs,min,max):result;}
        private static TabRecord LastTabsBoundary(GR rect,List<TabRecord> tabs,float min,float max)
        {TabRecord result=null;return LastTabs(rect,tabs,min,max,ref result)?TabDrawer.DrawTabsOverflow(rect,tabs,min,max):result;}
        private static IEnumerable<CodeInstruction> LastResearchCalls(IEnumerable<CodeInstruction> instructions,MethodBase __originalMethod)
        {
            foreach(CodeInstruction instruction in instructions)
            {
                if(instruction.opcode==OpCodes.Call&&instruction.operand is MethodInfo method&&method.DeclaringType==typeof(TabDrawer)&&method.IsGenericMethod&&method.GetGenericArguments().Single()==typeof(TabRecord))
                {
                    string adapter=method.Name=="GetOverflowTabHeight"?nameof(LastOverflowBoundary):method.Name=="DrawTabsOverflow"?nameof(LastTabsBoundary):null;
                    if(adapter!=null){lastTabAdapterSites.Add(__originalMethod.DeclaringType.FullName+":"+method.Name);instruction.operand=typeof(NativeWindowSubmissionChecks).GetMethod(adapter,All);}
                }
                yield return instruction;
            }
        }
        private static void LastSniperAfter(object dialog,MapComponent_HelodSniperSupport support)
        {
            var session=support.GetSession(lastBase);Add("sniper-after",default,Get(dialog,"selectedDirection").ToString(),session==null?-1:(int)session.Direction,session?.RemainingActions??-1,session?.RemainingTicks(Find.TickManager.TicksGame)??-1);
        }
        private static void LastSniperCases(Map map)
        {
            lastBase=(HelodForwardBase)WorldObjectMaker.MakeWorldObject(DefDatabase<WorldObjectDef>.GetNamed("HD_ForwardBase"));lastBase.Tile=map.Tile;
            var support=map.GetComponent<MapComponent_HelodSniperSupport>();var sessions=(List<HelodSniperSupportSession>)Get(support,"sessions");var effects=(List<SniperSupportEffect>)Get(support,"effects");
            var a=new Dialog_HelodSniperControl(map,lastBase);var e=new FrozenLastUi.Dialog_HelodSniperControl(map,lastBase);int now=Find.TickManager.TicksGame;
            try
            {
                foreach(GR rect in new[]{new GR(0,0,620,430),new GR(12.125f,-3.5f,630.75f,470.125f),new GR(0,0,0,0),new GR(-40,20,-300,-100)})foreach(int profile in Enumerable.Range(0,6))foreach(bool rejected in new[]{false,true})foreach(HelodSniperSupportDirection direction in Enum.GetValues<HelodSniperSupportDirection>())foreach(int click in Enumerable.Range(0,5))
                {
                    lastReject=rejected?"Controlled sniper rejection":null;EnumField(a,"selectedDirection",direction.ToString());EnumField(e,"selectedDirection",direction.ToString());clickButtons=click!=0;clickText=click switch{1=>HelodSniperSupportUtility.DirectionLabel(HelodSniperSupportDirection.East),2=>"HD_SniperSupport_Request".Translate().ToString(),3=>"HD_SniperSupport_Suppress".Translate().ToString(),4=>"HD_SniperSupport_Kill".Translate().ToString(),_=>null};
                    void Prepare()
                    {
                        sessions.RemoveAll(s=>s.ForwardBase==lastBase);effects.RemoveAll(s=>s.ForwardBase==lastBase);
                        if(profile>0){var session=new HelodSniperSupportSession(lastBase,HelodSniperSupportDirection.West,profile==2||profile==5?now-HelodSniperSupportSession.DurationTicks-1:now);if(profile==3)Set(session,"remainingActions",0);sessions.Add(session);}
                        if(profile==4||profile==5)effects.Add(new SniperSupportEffect(null,HelodSniperSupportMode.Kill,now,now+30,lastBase,HelodSniperSupportDirection.North,map.Center));
                    }
                    Frame(()=>{a.DoWindowContents(rect);LastSniperAfter(a,support);},()=>{e.DoWindowContents(rect);LastSniperAfter(e,support);},Prepare);State(a,e);lastSniperFrames++;
                }
            }
            finally{sessions.RemoveAll(s=>s.ForwardBase==lastBase);effects.RemoveAll(s=>s.ForwardBase==lastBase);lastBase=null;clickButtons=false;clickText=null;}
        }
        private static void LastResearchCases()
        {
            MainTabWindow_Research a=LastBare<MainTabWindow_Research>(),e=LastBare<MainTabWindow_Research>(),fa=LastBare<ForeignResearchWindow>(),fe=LastBare<ForeignResearchWindow>();
            if(!FrozenLastUi.ResearchWindowCompatibility.Vanilla.IsActive(a))throw new InvalidOperationException("Actual vanilla research host is not active.");
            FieldInfo tabField=AccessTools.Field(typeof(MainTabWindow_Research),"tabs"),current=AccessTools.Field(typeof(MainTabWindow_Research),"curTabInt");Type recordType=tabField.FieldType.GetGenericArguments()[0];ResearchTabDef helodTab=DefDatabase<ResearchTabDef>.GetNamed("HelodTechTab");
            foreach(GR rect in new[]{new GR(0,0,620,430),new GR(12.125f,-3.5f,630.75f,470.125f),new GR(0,0,0,0),new GR(-40,20,-300,-100)})foreach(float maxX in new[]{-100f,0,50,400,900,rect.x+rect.width})foreach(float overflow in new[]{-1f,0,30,90})foreach(int count in new[]{0,1,8})foreach(int guard in new[]{0,1,2})foreach(bool away in new[]{false,true})foreach(bool accepted in new[]{false,true})
            {
                var aw=guard==2?fa:a;var ew=guard==2?fe:e;lastOverflow=overflow;lastClickAway=away;lastCanvasAccepted=accepted;
                foreach(var window in new[]{aw,ew}){var tabs=(IList)Activator.CreateInstance(tabField.FieldType);for(int i=0;i<count;i++)tabs.Add(RuntimeHelpers.GetUninitializedObject(recordType));tabField.SetValue(window,tabs);current.SetValue(window,guard==1?ResearchTabDefOf.Main:helodTab);}
                bool ar=false,er=false;
                Frame(()=>{lastResearchWindow=aw;ar=ResearchWorkspaceWindowPatch.Prefix(aw,rect,maxX);Add("research-after",default,aw.CurTab?.defName,ar?1:0);},()=>{lastResearchWindow=ew;er=FrozenLastUi.ResearchWorkspaceWindowPatch.Prefix(ew,rect,maxX);Add("research-after",default,ew.CurTab?.defName,er?1:0);});
                if(ar!=er||aw.CurTab!=ew.CurTab)throw new InvalidOperationException("Research wrapper return/tab state changed.");if(guard!=0&&!ar)throw new InvalidOperationException("Research non-Helod/foreign guard must preserve vanilla drawing.");if(guard==0&&ar!=(!away&&!accepted))throw new InvalidOperationException("Research tab-switch/canvas return expectation failed.");lastResearchFrames++;
            }
            lastResearchWindow=null;
        }
        internal static void RunLastUi(string output,Action<bool,string> check)
        {
            var h=new Harmony("Helodrace.Tests.NativeLastUi");InstallUi(h,closePrefix:nameof(LastClose));int tick=Find.TickManager.TicksGame;
            h.Patch(AccessTools.Method(typeof(HelodSniperSupportUtility),"RequestRejection"),prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),nameof(LastRejection)));h.Patch(AccessTools.Method(typeof(MapComponent_HelodSniperSupport),"TryRequestSupport"),prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),nameof(LastRequest)));h.Patch(AccessTools.Method(typeof(HelodSniperSupportUtility),"BeginTargeting"),prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),nameof(LastTarget)));
            foreach(MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Messages)).Where(m=>m.Name=="Message"&&m.GetParameters().Length>0&&m.GetParameters()[0].ParameterType==typeof(string)))h.Patch(method,prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),nameof(LastMessage)));
            foreach(Type type in new[]{typeof(ResearchWorkspaceWindowPatch),typeof(FrozenLastUi.ResearchWorkspaceWindowPatch)})h.Patch(AccessTools.Method(type,"Prefix"),transpiler:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),nameof(LastResearchCalls)));
            h.Patch(AccessTools.Method(typeof(ResearchWindowCompatibility),"TryDraw"),prefix:new HarmonyMethod(typeof(NativeWindowSubmissionChecks),nameof(LastCanvas)));
            active=lastUiActive=true;clickButtons=false;
            try
            {
                Find.TickManager.DebugSetTicksGame(Math.Max(10000,tick));LastSniperCases(Find.CurrentMap);check(lastSniperFrames==960&&lastRequests>0&&lastTargets>0&&lastMessages>0&&lastCloseCalls>0,"native sniper-control layout and direction/request/attack branches match frozen widget payloads and session results");
                LastResearchCases();check(lastResearchFrames==3456&&lastTabCalls>0&&lastCanvasCalls>0&&lastTabAdapterSites.Count==4,"native research wrapper preserves header/overflow/inset rectangles, host/tab guards, tab-switch recheck and canvas return behavior");
                check(frames==lastSniperFrames+lastResearchFrames,"all final UI comparisons preserve widget order/text/style and numeric rectangles");
            }
            finally
            {
                File.WriteAllText(Path.Combine(output,"native-last-ui.json"),JsonSerializer.Serialize(new{SniperFrames=lastSniperFrames,ResearchFrames=lastResearchFrames,Frames=frames,Submissions=widgets,Components=components,MaximumFloatError=maximumError,NonzeroDifferences=differences,RequestCalls=lastRequests,TargetCalls=lastTargets,CloseCalls=lastCloseCalls,Messages=lastMessages,TabDrawCalls=lastTabCalls,CanvasCalls=lastCanvasCalls,TabAdapterSites=lastTabAdapterSites.Count,Baseline="218fa730",ControlledWidgetsRejectionTabOverflowAndCanvas=true,RealSupportSessionRequestLogic=true,NoNaturalInputOrGpuClaim=true},new JsonSerializerOptions{WriteIndented=true}));
                active=lastUiActive=false;trace=null;clickButtons=false;clickText=null;lastResearchWindow=null;lastBase=null;Find.TickManager.DebugSetTicksGame(tick);h.UnpatchAll(h.Id);
            }
        }
    }
}
