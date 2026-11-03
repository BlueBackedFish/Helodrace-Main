using System;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Tests.StageK;
public sealed partial class Checks
{
    partial void CowboyChecks(Pawn pawn)
    {
        Check(pawn.equipment.Primary?.def.defName=="HD_Gun_ColtSAA_Weapon","Native crisis rescuer carries the actual SAA, not a random revolver");
        var expected=new[]{("HD_Apparel_CowboyHat",156,117,68),("Apparel_CollarShirt",225,210,175),("Apparel_Pants",57,52,44),("Apparel_Duster",112,74,43)};
        if(pawn.apparel.WornApparel.Count!=4)
        {
            var pairs=(System.Collections.Generic.List<ThingStuffPair>)HarmonyLib.AccessTools.Field(typeof(PawnApparelGenerator),"allApparelPairs").GetValue(null);
            foreach(var req in pawn.kindDef.specificApparelRequirements)
                foreach(var pair in pairs.Where(p=>p.thing==req.ApparelDef&&(req.Stuff==null||p.stuff==req.Stuff)))
                    System.IO.File.AppendAllText(System.IO.Path.Combine(output,"checks.txt"),"APPAREL_CANDIDATE "+pair.thing.defName+"/"+pair.stuff?.defName+" common="+pair.Commonality+" canStuff="+HarmonyLib.AccessTools.Method(typeof(PawnApparelGenerator),"CanUseStuff").Invoke(null,new object[]{pawn,pair})+" hasParts="+ApparelUtility.HasPartsToWear(pawn,pair.thing)+"\n");
        }
        Check(pawn.apparel.WornApparel.Count==4,"Western outfit contains exactly four specified garments, without modern armor: "+string.Join(",",pawn.apparel.WornApparel.Select(a=>a.def.defName)));
        foreach(var (name,r,g,b) in expected)
        {
            var garment=pawn.apparel.WornApparel.SingleOrDefault(a=>a.def.defName==name);
            Check(garment!=null,"Native apparel generation supplies "+name);
            var c=garment.DrawColor;
            Check(Math.Abs(c.r-r/255f)<.01&&Math.Abs(c.g-g/255f)<.01&&Math.Abs(c.b-b/255f)<.01,"Western garment retains its specified color: "+name);
        }
        Check(pawn.story.Adulthood?.defName=="HD_FrontierRescuer","Dedicated frontier backstory is selected by native generation");
        var trait=pawn.story.traits.GetTrait(Def<TraitDef>("HD_FrontierSixgunTraining"));
        Check(trait!=null,"Dedicated backstory grants its nonrandom sixgun-training trait");
        float aim=StatDefOf.AimingDelayFactor.Worker.GetValue(StatRequest.For(pawn));
        float cooldown=StatDefOf.RangedCooldownFactor.Worker.GetValue(StatRequest.For(pawn));
        pawn.story.traits.RemoveTrait(trait);
        float ordinaryAim=StatDefOf.AimingDelayFactor.Worker.GetValue(StatRequest.For(pawn));
        float ordinaryCooldown=StatDefOf.RangedCooldownFactor.Worker.GetValue(StatRequest.For(pawn));
        pawn.story.traits.GainTrait(trait);
        Check(Math.Abs(aim/ordinaryAim-.75f)<.001,"Native pawn warmup factor is reduced by 25 percent by the backstory training");
        Check(Math.Abs(cooldown/ordinaryCooldown-.5f)<.001,"Native pawn ranged cooldown factor is reduced by 50 percent, more than warmup");
        var verb=pawn.equipment.Primary.GetComp<CompEquippable>().PrimaryVerb;
        float actual=verb.verbProps.AdjustedCooldown(verb,pawn);
        float expectedCooldown=pawn.equipment.Primary.GetStatValue(StatDefOf.RangedWeapon_Cooldown)*pawn.GetStatValue(StatDefOf.RangedCooldownFactor);
        Check(Math.Abs(actual-expectedCooldown)<.001,"Actual SAA cooldown consumes the native pawn cooldown factor");
    }
}
