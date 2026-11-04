using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Tests.StageK;
public sealed partial class Checks
{
    private IEnumerable<int> ArmorMaterialChecks(Pawn worker, Building_WorkTable machining)
    {
        var mask=ContentFinder<UnityEngine.Texture2D>.Get("Items/SyntheticFibers/ClothTintMask");
        Check(mask!=null,"Dedicated shared cloth color mask resolves");
        foreach(string n in new[]{"HD_Nylon","HD_Aramid"})
        {
            var d=Def<ThingDef>(n);
            Check(d.graphicData.texPath==ThingDefOf.Cloth.graphicData.texPath,"Fiber references the vanilla cloth texture directory: "+n);
            foreach(int amount in new[]{1,75,150})
            {
                var t=ThingMaker.MakeThing(d);t.stackCount=amount;
                var graphic=((Graphic_StackCount)t.Graphic).SubGraphicFor(t);var material=graphic.MatSingle;
                var original=((Graphic_StackCount)ThingDefOf.Cloth.graphicData.Graphic).SubGraphicFor(t).MatSingle;
                Check(material!=BaseContent.BadMat&&material.mainTexture==original.mainTexture,"Live stack uses original vanilla cloth pixels: "+n+"/"+amount);
                Check(material.GetTexture("_MaskTex")==mask,"Live stack material uses the explicit color mask: "+n+"/"+amount);
                var color=material.color;var expected=d.graphicData.color;
                Check(Math.Abs(color.r-expected.r)<.001&&Math.Abs(color.g-expected.g)<.001&&Math.Abs(color.b-expected.b)<.001,"Live cloth tint matches the fiber color: "+n+"/"+amount);
            }
        }
        CheckArmorRecipe("HD_Apparel_M1952AFlakJacket",new (string,int)[]{("HD_Nylon",65),("Steel",25)});
        CheckArmorRecipe("HD_Apparel_IBTVAssault",new (string,int)[]{("HD_Nylon",60),("HD_Aramid",40),("HD_GeneralMachinePart",3)});
        CheckArmorRecipe("HD_Apparel_MOLLEBattleBelt",new (string,int)[]{("HD_Nylon",30),("Steel",10),("HD_GeneralMachinePart",1)});
        CheckArmorRecipe("HD_ModularPart_IBTVShoulderGuards",new (string,int)[]{("HD_Nylon",12),("HD_Aramid",12)});
        CheckArmorRecipe("HD_ModularPart_IBTVLowerBody",new (string,int)[]{("HD_Nylon",14),("HD_Aramid",10)});
        CheckArmorRecipe("HD_ModularPart_IBTVBackProtector",new (string,int)[]{("HD_Nylon",14),("HD_Aramid",9)});
        CheckArmorRecipe("HD_ModularPart_IFAK",new (string,int)[]{("HD_Nylon",12),("MedicineIndustrial",1)});
        CheckArmorRecipe("HD_ModularPart_RifleMagazinePouch",new (string,int)[]{("HD_Nylon",10)});
        CheckArmorRecipe("HD_ModularPart_WalkieTalkie",new (string,int)[]{("HD_Nylon",10),("HD_ElectronicComponent",1)});
        CheckArmorRecipe("HD_ModularPart_MediL",new (string,int)[]{("HD_Nylon",18),("MedicineIndustrial",2)});
        CheckArmorRecipe("HD_ModularPart_AdminPouch",new (string,int)[]{("HD_Nylon",12)});
        CheckArmorRecipe("HD_ModularPart_IBTVCamelBak",new (string,int)[]{("HD_Nylon",20),("HD_GeneralMachinePart",1)});
        CheckArmorRecipe("HD_ModularPart_UCPKneePads",new (string,int)[]{("HD_Nylon",8),("Steel",6)});
        CheckArmorRecipe("HD_ModularPart_MOLLEDropLeg",new (string,int)[]{("HD_Nylon",16),("Steel",6)});
        CheckArmorRecipe("HD_ArmorPlate_SAPI",new (string,int)[]{("HD_Alumina",18),("HD_Aramid",18),("HD_Nylon",6),("HD_GeneralMachinePart",1)});
        CheckArmorRecipe("HD_ArmorPlate_ESAPI",new (string,int)[]{("HD_Alumina",28),("HD_Aramid",16),("HD_Nylon",6),("HD_GeneralMachinePart",2)});
        CheckArmorRecipe("HD_ArmorPlate_SSAPI",new (string,int)[]{("HD_Alumina",40),("HD_Aramid",12),("HD_Nylon",4),("HD_GeneralMachinePart",3)});
        CheckArmorRecipe("HD_ArmorPlate_ESBI",new (string,int)[]{("HD_Alumina",44),("HD_Aramid",10),("HD_Nylon",4),("HD_GeneralMachinePart",3)});
        CheckArmorRecipe("HD_ArmorPlate_RAMPART4800_Plate",new (string,int)[]{("Uranium",10),("Plasteel",48),("HD_Nylon",6),("HD_GeneralMachinePart",4)});
        foreach(string name in new[]{"HD_ArmorPlate_SAPI","HD_ArmorPlate_ESAPI","HD_ArmorPlate_SSAPI","HD_ArmorPlate_ESBI"})
            Check(Def<ThingDef>(name).GetCompProperties<Helodrace.ModernWar.CompProperties_ArmorPlateSet>().material.ToString()=="Ceramic","Alumina plate retains its ceramic behavior: "+name);
        var rampart=Def<ThingDef>("HD_ArmorPlate_RAMPART4800_Plate");
        Check(rampart.GetCompProperties<Helodrace.ModernWar.CompProperties_ArmorPlateSet>().material.ToString()=="UHMWPE"&&!rampart.costList.Any(c=>c.thingDef.defName=="HD_Alumina"),"UHMWPE RAMPART keeps its material behavior and has no alumina");
        var bench=(Building_WorkTable)ItemBuilding("HD_BasicWorkbench",20,-9);
        foreach(int ticks in ItemBill(worker,bench,"Make_HD_Apparel_M1952AFlakJacket"))yield return ticks;
        foreach(string name in new[]{"Make_HD_Apparel_IBTVAssault","Make_HD_ModularPart_IBTVShoulderGuards","Make_HD_ModularPart_RifleMagazinePouch","Make_HD_ArmorPlate_SAPI","Make_HD_ArmorPlate_RAMPART4800_Plate"})
            foreach(int ticks in ItemBill(worker,machining,name))yield return ticks;
    }
    private void CheckArmorRecipe(string name,(string material,int amount)[] expected)
    {
        var d=Def<ThingDef>(name);var recipe=Def<RecipeDef>("Make_"+name);
        Check(recipe.ingredients.Count==expected.Length&&expected.All(e=>recipe.ingredients.Any(i=>i.filter.AllowedDefCount==1&&i.filter.Allows(Def<ThingDef>(e.material))&&i.GetBaseCount()==e.amount)),"Generated armor bill has the exact new material list: "+name);
        Check(expected.All(e=>recipe.defaultIngredientFilter.Allows(Def<ThingDef>(e.material))),"Every new ingredient is enabled in a default bill: "+name);
    }
}
