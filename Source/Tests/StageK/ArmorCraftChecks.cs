using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helodrace.ModernWar;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.StageK;
public sealed partial class Checks
{
    private static CompModularArmor ArmorDraft(CompModularArmor armor)
        => (CompModularArmor)AccessTools.Method(typeof(CompModularArmor),"CreateEditorDraft").Invoke(armor,null);
    private static string ArmorSignature(CompModularArmor armor)
        => (string)AccessTools.Method(typeof(CompModularArmor),"ConfigurationSignature").Invoke(armor,null);
    private bool ArmorStart(CompModularArmor armor,CompModularArmor draft,Thing bench,out string rejection)
    {
        object[] args={draft,ArmorSignature(armor),bench,null};
        bool started=(bool)AccessTools.Method(typeof(CompModularArmor),"TryStartCustomization").Invoke(armor,args);
        rejection=(string)args[3];
        var pawn=armor.Wearer;
        System.IO.File.AppendAllText(System.IO.Path.Combine(output,"checks.txt"),"ARMORSTART ok="+started+" job="+pawn?.CurJobDef+" carry="+pawn?.carryTracker.CarriedThing+" queue="+(pawn?.CurJob?.countQueue==null?"":string.Join(",",pawn.CurJob.countQueue))+"\n");return started;
    }
    private IEnumerable<int> ArmorFinish(Pawn pawn)
    {
        int elapsed=0;
        while(pawn.CurJobDef?.defName=="HD_CustomizeModularArmor"&&elapsed<20000){yield return 120;elapsed+=120;}
        Check(pawn.CurJobDef?.defName!="HD_CustomizeModularArmor","Customization job finishes within its real work budget");
        pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
    }
    private IEnumerable<int> ArmorCraftChecks(Pawn worker)
    {
        var separate=DefDatabase<ModularArmorPartDef>.AllDefsListForReading
            .Where(p=>p.playerSelectable&&p.RequiredThingDef!=null).ToList();
        foreach(var p in separate)
            Check(p.CanInstantCraft==(p.plateThingDef==null&&p.RequiredThingDef.GetCompProperties<CompProperties_ArmorPlateSet>()==null&&p.defName!="HD_FASTMTPart_GPNVG18"),"Only plate sets and night vision require separate manufacture: "+p.defName);
        var table=ItemClothingBench(worker);yield return 1200;
        var press=playMap.listerThings.ThingsOfDef(Def<ThingDef>("HD_ElectricHydraulicPress")).Single();
        var vest=(Apparel)ThingMaker.MakeThing(Def<ThingDef>("HD_Apparel_IBTVAssault"));worker.apparel.Wear(vest);
        var armor=vest.GetComp<CompModularArmor>();var draft=ArmorDraft(armor);
        var shoulder=Def<ModularArmorPartDef>("HD_IOTVPart_ShoulderGuards");
        var camel=Def<ModularArmorPartDef>("HD_IOTVPart_CamelBak");
        var magazine=Def<ModularArmorPartDef>("HD_IOTVPart_RifleMagazinePouch");
        Check(draft.SetPart(shoulder.slot,shoulder)&&draft.SetPart(camel.slot,camel),"Editor can preview unmade protection and hydration modules");
        int placed=0;
        foreach(var panel in draft.Props.palsPanels)
            for(int y=0;y<panel.rows;y++)for(int x=0;x<panel.columns;x++)
                if(placed<6&&draft.InstallPalsPart(magazine,panel,x,y)!=null)placed++;
        Check(placed==6,"Editor preview includes six separately placed magazine pouches");
        Check(armor.InstalledParts.Count==0,"Editing a draft creates no real installed accessories");
        var nylon=Def<ThingDef>("HD_Nylon");var aramid=Def<ThingDef>("HD_Aramid");var machine=Def<ThingDef>("HD_GeneralMachinePart");
        ItemSupply(nylon,91,table.InteractionCell);ItemSupply(aramid,12,table.InteractionCell);ItemSupply(machine,1,table.InteractionCell);
        // Research is an actual recipe condition, not a separate editor-only gate.
        Find.ResearchManager.ResetAllProgress();
        Check(!ArmorStart(armor,draft,table,out string reason)&&armor.InstalledParts.Count==0,"Missing research rejects an instant-craft plan without mutating the armor: "+reason);
        foreach(var project in DefDatabase<ResearchProjectDef>.AllDefsListForReading)Find.ResearchManager.FinishProject(project,doCompletionDialog:false);
        Check(!ArmorStart(armor,draft,table,out reason)&&ItemCount(nylon)==91&&armor.InstalledParts.Count==0,"Aggregate batch cost rejects 91 nylon for a 92-nylon draft: "+reason);
        ItemSupply(nylon,1,table.InteractionCell);
        press.TryGetComp<CompFlickable>().SwitchIsOn=false;
        Check(!ArmorStart(armor,draft,table,out reason)&&ItemCount(nylon)==92,"Making new ballistic protection still requires the real press: "+reason);
        press.TryGetComp<CompFlickable>().SwitchIsOn=true;yield return 600;

        var dialog=new Dialog_ModularArmor(armor,table.GetComp<CompClothingWorkbench>());
        AccessTools.Method(typeof(Dialog_ModularArmor),"OpenPartMenu").Invoke(dialog,new object[]{shoulder.slot});
        var menu=Find.WindowStack.Windows.OfType<FloatMenu>().Last();
        var options=(List<FloatMenuOption>)AccessTools.Field(typeof(FloatMenu),"options").GetValue(menu);
        Check(options.Any(o=>o.Label.Contains("HD_ArmorCraft_InstantGroup".Translate().Resolve())),"Real armor menu groups instant-craft accessories separately");menu.Close();dialog.Close();

        Check(ArmorStart(armor,draft,table,out reason),"Actual customization starts with raw materials and no premade accessories: "+reason);
        bool interrupted=false;
        for(int ticks=0;ticks<12000&&worker.CurJobDef?.defName=="HD_CustomizeModularArmor";ticks+=120)
        {
            var collected=(List<Thing>)AccessTools.Field(worker.jobs.curDriver.GetType(),"collected").GetValue(worker.jobs.curDriver);
            System.IO.File.AppendAllText(System.IO.Path.Combine(output,"checks.txt"),"ARMORTRACE t="+ticks+" toil="+worker.jobs.curDriver.CurToilIndex+" collected="+collected.Count+" pos="+worker.Position+" carry="+worker.carryTracker.CarriedThing+"\n");
            if(collected.Count==worker.CurJob.targetQueueA.Count&&worker.Position==table.InteractionCell)
            {
                press.TryGetComp<CompFlickable>().SwitchIsOn=false;yield return 120;
                Check(worker.CurJobDef?.defName!="HD_CustomizeModularArmor"&&armor.InstalledParts.Count==0,"Press loss cancels an in-progress customization before committing crafted parts");
                Check(ItemCount(nylon)==92&&ItemCount(aramid)==12&&ItemCount(machine)==1,"Cancelled customization retains every gathered raw material");interrupted=true;break;
            }
            yield return 120;
        }
        Check(interrupted,"Real material hauling reached the cancellable work phase");
        press.TryGetComp<CompFlickable>().SwitchIsOn=true;yield return 600;
        Check(ArmorStart(armor,draft,table,out reason),"Cancelled draft can restart using gathered inventory materials: "+reason);
        foreach(int ticks in ArmorFinish(worker))yield return ticks;
        Check(armor.InstalledParts.Count==8&&armor.InstalledParts.All(r=>r.InstalledItem!=null),"One successful customization manufactures and installs all eight real accessory records");
        Check(ItemCount(nylon)==0&&ItemCount(aramid)==0&&ItemCount(machine)==0,"Successful batch consumes exactly 92 nylon, 12 aramid and one dedicated machine part");
        var existing=armor.InstalledIn(shoulder.slot).InstalledItem;existing.HitPoints-=7;int hp=existing.HitPoints;
        var remove=ArmorDraft(armor);Check(remove.SetPart(shoulder.slot,null),"Draft can remove an instantly crafted accessory");
        Check(ArmorStart(armor,remove,table,out reason),"Removal starts without manufacturing resources: "+reason);
        foreach(int ticks in ArmorFinish(worker))yield return ticks;
        Check(worker.inventory.innerContainer.Contains(existing)&&existing.HitPoints==hp,"Removal returns the exact physical accessory and its condition");
        press.TryGetComp<CompFlickable>().SwitchIsOn=false;
        var reattach=ArmorDraft(armor);reattach.SetPart(shoulder.slot,shoulder);
        Check(ArmorStart(armor,reattach,table,out reason),"Reusing an existing accessory needs no new crafting materials or fabrication press: "+reason);
        foreach(int ticks in ArmorFinish(worker))yield return ticks;
        Check(armor.InstalledIn(shoulder.slot).InstalledItem==existing&&existing.HitPoints==hp,"Reinstallation preserves identity and condition without crafting a duplicate");
        press.TryGetComp<CompFlickable>().SwitchIsOn=true;yield return 600;

        var oldPouch=armor.InstalledParts.First(r=>r.IsPalsMounted);
        var destination=oldPouch.palsPanel;int destX=oldPouch.palsX,destY=oldPouch.palsY;
        var tracked=armor.InstalledParts.Last(r=>r.IsPalsMounted).InstalledItem;
        var ammo=tracked.TryGetComp<CompAmmoPouch>();ammo.ConsumeShot(30);float ammoLeft=ammo.RemainingFraction;
        var pouchRemove=ArmorDraft(armor);pouchRemove.RemovePalsPart(pouchRemove.InstalledParts.First(r=>r.IsPalsMounted));
        Check(ArmorStart(armor,pouchRemove,table,out reason),"Pouch removal starts without fresh raw materials: "+reason);
        foreach(int ticks in ArmorFinish(worker))yield return ticks;
        var moving=ArmorDraft(armor);var movingRecord=moving.InstalledParts.Single(r=>r.InstalledItem==tracked);
        Check(moving.MovePalsPart(movingRecord,destination,destX,destY),"Editor can move a retained pouch into a vacant position");
        Check(ArmorStart(armor,moving,table,out reason),"Repositioning uses the already installed pouch: "+reason);
        foreach(int ticks in ArmorFinish(worker))yield return ticks;
        Check(armor.InstalledParts.Any(r=>r.InstalledItem==tracked&&r.palsPanel==destination&&r.palsX==destX&&r.palsY==destY)
            &&Math.Abs(ammo.RemainingFraction-ammoLeft)<.001&&ItemCount(nylon)==0,"Pouch movement preserves physical identity and remaining ammunition without charging nylon");

        var sapi=Def<ModularArmorPartDef>("HD_IOTVPart_FrontRear_SAPI");var plateDraft=ArmorDraft(armor);plateDraft.SetPart(sapi.slot,sapi);
        ItemSupply(Def<ThingDef>("HD_Alumina"),18,table.InteractionCell);ItemSupply(aramid,18,table.InteractionCell);ItemSupply(nylon,6,table.InteractionCell);ItemSupply(machine,1,table.InteractionCell);
        Check(!ArmorStart(armor,plateDraft,table,out reason)&&ItemCount(nylon)==6,"Plate raw materials cannot replace a separately manufactured plate set: "+reason);
        var separateDialog=new Dialog_ModularArmor(armor,table.GetComp<CompClothingWorkbench>());
        AccessTools.Method(typeof(Dialog_ModularArmor),"OpenPartMenu").Invoke(separateDialog,new object[]{sapi.slot});
        menu=Find.WindowStack.Windows.OfType<FloatMenu>().Last();
        options=(List<FloatMenuOption>)AccessTools.Field(typeof(FloatMenu),"options").GetValue(menu);
        Check(options.Any(o=>o.Label.Contains("HD_ArmorCraft_IndependentGroup".Translate().Resolve()))
            &&options.Where(o=>o.Label.Contains("SAPI")).All(o=>o.Disabled),"Real plate menu labels separately crafted equipment and disables missing finished items");menu.Close();separateDialog.Close();
        var plate=ThingMaker.MakeThing(sapi.RequiredThingDef);var plateComp=plate.TryGetComp<CompArmorPlateSet>();plateComp.ApplyDamage(1,7);int front=plateComp.HitPointsFor(1),rear=plateComp.HitPointsFor(2);
        worker.inventory.innerContainer.TryAdd(plate,false);
        Check(ArmorStart(armor,plateDraft,table,out reason),"Separately manufactured SAPI can be installed: "+reason);
        foreach(int ticks in ArmorFinish(worker))yield return ticks;
        Check(armor.InstalledIn(sapi.slot).InstalledItem==plate&&plateComp.HitPointsFor(1)==front&&plateComp.HitPointsFor(2)==rear&&ItemCount(nylon)==6,"Plate installation preserves both independent durability values and does not charge raw crafting resources");

        var helmet=(Apparel)ThingMaker.MakeThing(Def<ThingDef>("HD_Apparel_FASTMT"));worker.apparel.Wear(helmet);var head=helmet.GetComp<CompModularArmor>();
        var nvg=Def<ModularArmorPartDef>("HD_FASTMTPart_GPNVG18");var headDraft=ArmorDraft(head);headDraft.SetPart(nvg.slot,nvg);
        ItemSupply(ThingDefOf.Plasteel,18,table.InteractionCell);ItemSupply(Def<ThingDef>("HD_ElectronicComponent"),8,table.InteractionCell);
        Check(!ArmorStart(head,headDraft,table,out reason)&&head.InstalledParts.Count==0,"Night-vision raw materials cannot replace its separately manufactured module: "+reason);
        var nvgItem=ThingMaker.MakeThing(nvg.RequiredThingDef);worker.inventory.innerContainer.TryAdd(nvgItem,false);
        Check(ArmorStart(head,headDraft,table,out reason),"Separately manufactured night vision starts installation: "+reason);
        foreach(int ticks in ArmorFinish(worker))yield return ticks;
        Check(head.InstalledIn(nvg.slot).InstalledItem==nvgItem,"Night vision installs the supplied physical item");

        // One order can combine a separately supplied plate with a new accessory.
        var esapi=Def<ModularArmorPartDef>("HD_IOTVPart_FrontRear_ESAPI");
        var lower=Def<ModularArmorPartDef>("HD_IOTVPart_LowerBody");
        var upgradePlate=ThingMaker.MakeThing(esapi.RequiredThingDef);worker.inventory.innerContainer.TryAdd(upgradePlate,false);
        int nylonBefore=ItemCount(nylon),aramidBefore=ItemCount(aramid);
        ItemSupply(nylon,14,table.InteractionCell);ItemSupply(aramid,10,table.InteractionCell);
        var mixed=ArmorDraft(armor);Check(mixed.SetPart(esapi.slot,esapi)&&mixed.SetPart(lower.slot,lower),"A single draft can mix a supplied plate with an instant-craft protector");
        Check(ArmorStart(armor,mixed,table,out reason),"Mixed physical and raw-material acquisition starts: "+reason);
        foreach(int ticks in ArmorFinish(worker))yield return ticks;
        Check(armor.InstalledIn(esapi.slot).InstalledItem==upgradePlate&&armor.InstalledIn(lower.slot).InstalledItem?.def==lower.RequiredThingDef,
            "Mixed order installs the exact supplied plate and the newly manufactured protector");
        Check(worker.inventory.innerContainer.Contains(plate)&&plateComp.HitPointsFor(1)==front&&plateComp.HitPointsFor(2)==rear
            &&ItemCount(nylon)==nylonBefore&&ItemCount(aramid)==aramidBefore,"Mixed order charges only the new protector and returns the replaced plate with its original durability");
    }
}
