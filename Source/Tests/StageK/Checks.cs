using System;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;
namespace Helodrace.Tests.StageK;
public sealed partial class Checks:GameComponent
{
    bool started;string output;
    public Checks(Game game){}
    partial void MoodChecks(Pawn pawn);
    partial void CowboyChecks(Pawn pawn);
    void Finish(){File.AppendAllText(Path.Combine(output,"checks.txt"),"COMPLETE\n");Waylith.EngineApplication.Instance.Quit(0);}
    static T Def<T>(string n)where T:Verse.Def=>DefDatabase<T>.GetNamed(n);
    void Check(bool ok,string message){if(!ok)throw new Exception(message);File.AppendAllText(Path.Combine(output,"checks.txt"),"PASS: "+message+"\n");}
    public override void GameComponentTick()
    {
        if(started||Find.CurrentMap==null||!GenCommandLine.TryGetCommandLineArg("hdStageK",out output))return;started=true;
        Directory.CreateDirectory(output);if(statePhase==0)File.WriteAllText(Path.Combine(output,"checks.txt"),"Actual 1.7 Stage K life checks\n");
        try
        {
            Find.TickManager.Pause();var map=Find.CurrentMap;
            if(GenCommandLine.TryGetCommandLineArg("hdKGeneAudit",out _)){GeneAudit();Finish();return;}
            if(GenCommandLine.TryGetCommandLineArg("hdKState",out _)){playMap=map;ResumeState();return;}
            if(GenCommandLine.TryGetCommandLineArg("hdKTextureItems",out _)||GenCommandLine.TryGetCommandLineArg("hdKMinerals",out _)){steps=TextureItemChecks().GetEnumerator();return;}
            var incident=Def<IncidentDef>("StrangerInBlackJoin");
            Check(incident.pawnKind.defName=="HD_RescuerInBlack"&&incident.Worker.GetType()==typeof(IncidentWorker_WandererJoin),"Only crisis rescuer pawn kind changes; native worker remains");
            Check(Def<PawnKindDef>("StrangerInBlack").race==ThingDefOf.Human,"Original stranger pawn definition is preserved for other consumers");
            Check(DefDatabase<IncidentDef>.AllDefsListForReading.Where(d=>d.workerClass.Name.Contains("CreepJoiner")).All(d=>d.pawnKind?.defName!="HD_RescuerInBlack"),"Anomaly creep joiner definitions are not replaced");
            var worker=(IncidentWorker_WandererJoin)incident.Worker;
            for(int i=0;i<4;i++)
            {
                var pawn=worker.GeneratePawn(map);
                Check(pawn.def.defName=="Helod"&&!pawn.Dead&&!pawn.Downed,"Native incident generates a living Helod rescuer");
                Check(!pawn.WorkTagIsDisabled(WorkTags.Caring)&&!pawn.WorkTagIsDisabled(WorkTags.Violent),"Rescuer can fight and rescue/tend");
                Check(pawn.inventory.innerContainer.Any(t=>t.def==ThingDefOf.MedicineIndustrial)&&pawn.inventory.innerContainer.Any(t=>t.def.defName=="HD_PlainDogChew"),"Medical and BTX supplies survive native inventory generation");
                Check(pawn.genes.Xenotype.defName=="HD_MackenzieValleyHelod","Rescuer uses the current Mackenzie lineage without editing its genes: "+pawn.genes.Xenotype.defName);
                CowboyChecks(pawn);
                GenSpawn.Spawn(pawn,map.Center+new IntVec3(12+i*2,0,8),map);
                MoodChecks(pawn);
                pawn.Destroy(DestroyMode.Vanish);
            }
            if(GenCommandLine.TryGetCommandLineArg("hdKRecreation",out _)){steps=RecreationChecks().GetEnumerator();return;}
            if(GenCommandLine.TryGetCommandLineArg("hdKPistols",out _)){steps=PistolChecks().GetEnumerator();return;}
            if(GenCommandLine.TryGetCommandLineArg("hdKXenotypes",out _)){steps=XenotypeLifeChecks().GetEnumerator();return;}
            Finish();
        }
        catch(Exception e){File.AppendAllText(Path.Combine(output,"checks.txt"),"FAIL: "+e+"\n");Log.Error(e.ToString());Waylith.EngineApplication.Instance.Quit(1);}
    }
}
