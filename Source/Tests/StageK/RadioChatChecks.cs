using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.Tests.StageK;
public sealed partial class Checks
{
    private static Building_Radio radioFixture;
    private static int radioJoyErrors;
    private static bool RadioPower(Building_Radio __instance,ref bool __result)
    {if(__instance!=radioFixture)return true;__result=true;return false;}
    private static void RadioJoyError(string text)
    {if(text.StartsWith("Joy source joyKind and jobDef.joyKind are not the same"))radioJoyErrors++;}
    private IEnumerable<int> RadioChatChecks()
    {
        radioFixture=(Building_Radio)ThingMaker.MakeThing(Def<ThingDef>("HD_Radio"));radioFixture.SetFaction(Faction.OfPlayer);
        GenSpawn.Spawn(radioFixture,playMap.Center,playMap);
        radioFixture.GetComp<CompPowerTrader>().PowerOn=true;
        var h=new Harmony("helodrace.tests.stagek.radio-chat");
        h.Patch(AccessTools.PropertyGetter(typeof(Building_Radio),nameof(Building_Radio.CanUseNow)),prefix:new HarmonyMethod(typeof(Checks),nameof(RadioPower)));
        h.Patch(AccessTools.Method(typeof(Log),nameof(Log.ErrorOnce)),prefix:new HarmonyMethod(typeof(Checks),nameof(RadioJoyError)));
        // Electrical supply and daily mission rolls are independent of the joy-kind regression.
        var schedule=(HamRadioSchedule)AccessTools.Field(typeof(MapComponent_HamRadio),"schedule").GetValue(playMap.GetComponent<MapComponent_HamRadio>());
        schedule.PreparedDay=int.MaxValue;schedule.HasRumor=false;
        var user=Player(radioFixture.InteractionCell);
        Check(Building_Radio.Frequencies.Any(),"Actual map contains a valid HAM frequency for conversation");
        var job=Def<JoyGiverDef>("HD_ChatOnRadio").Worker.TryGiveJob(user);
        Check(job?.def.joyKind==Def<JoyKindDef>("Social")&&radioFixture.def.building.joyKind==Def<JoyKindDef>("Television"),"Native chat giver keeps Social conversation and Television listening distinct: job="+job?.def.defName+" power="+radioFixture.GetComp<CompPowerTrader>().PowerOn);
        Check(user.jobs.TryTakeOrderedJob(job),"Actual pawn begins the radio conversation job");
        yield return 300;
        Check(user.CurJobDef==Def<JobDef>("HD_ChatOnRadio")&&user.needs.joy.CurLevel>.05f,"Native radio chat continues and increases actual recreation");
        Check(radioJoyErrors==0,"Actual native JoyTickCheckEnd emits no mismatched radio joy-kind error");
        user.jobs.EndCurrentJob(Verse.AI.JobCondition.InterruptForced);user.Destroy();radioFixture.Destroy();radioFixture=null;
        h.UnpatchAll(h.Id);
    }
}
