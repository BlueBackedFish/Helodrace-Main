using System.Collections.Generic;
using System.Linq;
using Helodrace.ModernWar;
using RimWorld;
using Verse;

namespace Helodrace.Tests.StageK;
public sealed partial class Checks
{
    private IEnumerable<int> HelmetShieldChecks(Pawn worker, Building_WorkTable machining)
    {
        machining=ItemClothingBench(worker);yield return 1200;
        CheckArmorRecipe("HD_Apparel_FASTMT",new (string,int)[]{("HD_Nylon",15),("HD_Aramid",25),("HD_GeneralMachinePart",2)});
        CheckArmorRecipe("HD_Apparel_GreatWarM1Helmet",new (string,int)[]{("HD_UniformRolledSteelPlate",5)});
        CheckArmorRecipe("HD_Apparel_ShieldDefenTechIIIA",new (string,int)[]{("Steel",55),("HD_Aramid",30),("HD_GeneralMachinePart",2)});
        CheckArmorRecipe("HD_Apparel_ShieldIronHideIV",new (string,int)[]{("Steel",120),("HD_Aramid",40),("HD_Alumina",40),("HD_GeneralMachinePart",4)});
        CheckArmorRecipe("HD_Apparel_ShieldIronHideRiot",new (string,int)[]{("HD_CarbonSteel",20),("Steel",20),("Chemfuel",12)});

        var ibtv=Def<ThingDef>("HD_Apparel_IBTVAssault");
        Check(ibtv.GetCompProperties<CompProperties_ModularArmor>().defaultParts.NullOrEmpty(),"IBTV declares no free default parts");
        var empty=ThingMaker.MakeThing(ibtv);
        var armor=empty.TryGetComp<CompModularArmor>();
        Check(armor.InstalledParts.Count==0,"New IBTV starts with no plates, protection modules or pouches");
        GenSpawn.Spawn(empty,playMap.Center+new IntVec3(18,0,-12),playMap);
        Check(armor.InstalledParts.Count==0,"Spawning IBTV does not add default parts");
        foreach(string name in new[]{"HD_IOTVPart_ShoulderGuards","HD_IOTVPart_LowerBody","HD_IOTVPart_BackProtector"})
        {
            var part=Def<ModularArmorPartDef>(name);
            Check(!armor.SetPart(part.slot,part)&&armor.InstalledParts.Count==0,"A protection module cannot be installed for free: "+name);
            var item=ThingMaker.MakeThing(part.RequiredThingDef);
            Check(armor.SetPart(part.slot,part,suppliedItem:item)&&armor.InstalledIn(part.slot).InstalledItem==item,"A separately supplied physical module can be installed: "+name);
            Check(armor.SetPart(part.slot,null)&&armor.InstalledParts.Count==0&&item.Spawned,"Removing the module returns the same physical item and leaves IBTV empty: "+name);
            item.Destroy(DestroyMode.Vanish);
        }
        empty.DeSpawn();
        GenSpawn.Spawn(empty,playMap.Center+new IntVec3(18,0,-12),playMap);
        Check(armor.InstalledParts.Count==0,"Empty IBTV stays empty after respawning");
        empty.Destroy(DestroyMode.Vanish);

        // Authored combat loadouts intentionally assemble equipment; they are
        // independent of the defaults on an ordinary newly made carrier.
        var equipped=ThingMaker.MakeThing(ibtv);
        var preset=Def<ModularArmorPresetDef>("HD_ArmorPreset_IBTV_Rifleman");
        var equippedArmor=equipped.TryGetComp<CompModularArmor>();
        bool applied=equippedArmor.TryApplyPreset(preset,out string rejection);
        Check(applied,"Explicit rifleman loadout still assembles after default removal: "+rejection);
        Check(preset.fixedParts.All(e=>equippedArmor.InstalledIn(e.slot)?.InstalledItem?.def==e.part.RequiredThingDef),"Explicit rifleman modules and plates have physical items");
        equipped.Destroy(DestroyMode.Vanish);

        foreach(string name in new[]{"HD_Apparel_FASTMT","HD_Apparel_ShieldDefenTechIIIA","HD_Apparel_ShieldIronHideIV","HD_Apparel_ShieldIronHideRiot","HD_Apparel_IBTVAssault"})
            foreach(int ticks in ItemBill(worker,machining,"Make_"+name))yield return ticks;
        var made=playMap.listerThings.ThingsOfDef(ibtv);
        Check(made.Count==1&&made.All(t=>t.TryGetComp<CompModularArmor>().InstalledParts.Count==0),"Actually crafted IBTV is an empty carrier without free modules or plates");
    }
}
