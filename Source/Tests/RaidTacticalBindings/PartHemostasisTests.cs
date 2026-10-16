using System;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace;
using Helodrace.Tactical;
using Verse;

internal static class PartHemostasisTests
{
    private sealed class BleedingTestHediff : Hediff
    {
        public float rate;
        public override float BleedRate => rate;
    }
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        var set = new HediffSet(new Pawn());
        var left = new BodyPartRecord(); var right = new BodyPartRecord();
        Check(PartHemostasis.Factor(set,left,0) == 1f && PartHemostasis.Factor(null,left,0) == 1f
            && PartHemostasis.Factor(set,null,0) == 1f, "Untreated and missing data cannot suppress bleeding.");
        Hediff_PartHemostasis Marker(BodyPartRecord part, float factor, int expiry)
        {
            var marker = new Hediff_PartHemostasis { bleedingFactor=factor, expiresTick=expiry,
                def=new HediffDef { defName=PartHemostasis.DressingDefName } };
            // No initialized Unity body/defs in this isolated cache test. Native test
            // exercises MakeHediff/AddHediff and the real Part setter separately.
            AccessTools.Field(typeof(Hediff),"part").SetValue(marker,part); return marker;
        }
        var dressing = Marker(left,.30f,500);
        set.hediffs.Add(dressing); PartHemostasis.Invalidate(set);
        Check(PartHemostasis.Factor(set,left,1)==.30f && PartHemostasis.Factor(set,right,1)==1f,
            "A marker must affect the exact record, never another limb.");
        var strong = Marker(left,.05f,200);
        set.hediffs.Add(strong); PartHemostasis.Invalidate(set);
        Check(PartHemostasis.Factor(set,left,100)==.05f, "Same-part treatments use the strongest factor, not a product.");
        Check(PartHemostasis.Factor(set,left,199)==.05f && PartHemostasis.Factor(set,left,200)==.30f,
            "Expired stronger treatment exposes the still-active weaker treatment without a tick scan.");
        Check(PartHemostasis.Factor(set,left,500)==1f, "An expired marker waiting for native removal has no effect.");
        dressing.expiresTick=900; PartHemostasis.Invalidate(set);
        Check(PartHemostasis.Factor(set,left,600)==.30f, "Refreshing treatment invalidates a previously empty cache.");
        set.hediffs.Remove(dressing); PartHemostasis.Invalidate(set);
        Check(PartHemostasis.Factor(set,left,601)==1f, "Removal releases the reduction immediately.");
        set.hediffs.Add(Marker(right,-2f,900));
        PartHemostasis.Invalidate(set);
        Check(PartHemostasis.Factor(set,right,700)==0f && PartHemostasis.Factor(set,left,700)==1f,
            "Invalid potency clamps safely and does not leak to another part.");
        var missingLeft = new BodyPartRecord(); var missingRight = new BodyPartRecord();
        var stump = Marker(left,.20f,900); stump.bleedingPart = missingLeft;
        var otherStump = Marker(left,.30f,900); otherStump.bleedingPart = missingRight;
        set.hediffs.Add(stump); set.hediffs.Add(otherStump); PartHemostasis.Invalidate(set);
        Check(PartHemostasis.Factor(set,missingLeft,700)==.20f
            && PartHemostasis.Factor(set,missingRight,700)==.30f
            && PartHemostasis.Factor(set,left,700)==1f
            && PartHemostasis.HasDressing(set,missingLeft,700)
            && !PartHemostasis.HasDressing(set,left,700),
            "Stump markers reduce bleeding on their own missing parts without affecting the surviving parent.");
        var combined = new HediffSet(new Pawn());
        var local = Marker(left,PartHemostasis.DressingFactor,900);
        combined.hediffs.Add(local);
        var drug = new Hediff_SystemicHemostatic { expiresTick=800 };
        combined.hediffs.Add(drug);
        Check(Math.Abs(PartHemostasis.Factor(combined,left,700)-.06f)<.00001f
            && PartHemostasis.Factor(combined,right,700)==.30f && PartHemostasis.Factor(combined,null,700)==.30f,
            "Systemic agent combines multiplicatively with dressing and also affects untreated/partless bleeding.");
        Check(PartHemostasis.Factor(combined,left,800)==.20f && PartHemostasis.Factor(combined,right,800)==1f,
            "Drug expiry preserves independent local dressing.");
        var duplicate = Marker(left,.20f,900); combined.hediffs.Add(duplicate); PartHemostasis.Invalidate(combined);
        Check(PartHemostasis.PainOffset(combined,local,800)+PartHemostasis.PainOffset(combined,duplicate,800)==.05f,
            "One dressed part cannot contribute pain twice.");
        for (int i=0;i<6;i++) combined.hediffs.Add(Marker(new BodyPartRecord(),.20f,900));
        PartHemostasis.Invalidate(combined);
        Check(Math.Abs(combined.hediffs.OfType<Hediff_PartHemostasis>()
            .Sum(marker=>PartHemostasis.PainOffset(combined,marker,800))-.20f)<.00001f,
            "Seven dressed parts cannot add more than 20 percent pain.");
        Check(combined.hediffs.OfType<Hediff_PartHemostasis>()
            .Sum(marker=>PartHemostasis.PainOffset(combined,marker,900))==0f,
            "Expired dressings no longer contribute pain.");
        Check(PartHemostasis.DressingTicks==30000 && PartHemostasis.DressingFactor==.20f,
            "Both dressing sources share 12 hours and 80 percent reduction.");
        var patient = new Pawn();
        patient.health = (Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
        patient.health.hediffSet = new HediffSet(patient);
        var parts = new BodyPartRecord[8];
        for (int i=0;i<8;i++)
        {
            parts[i]=new BodyPartRecord { def=new BodyPartDef { defName=i==0 ? "Brain" : "Part"+i } };
            var wound=new BleedingTestHediff { rate=i+1 };
            AccessTools.Field(typeof(Hediff),"part").SetValue(wound,parts[i]);
            patient.health.hediffSet.hediffs.Add(wound);
        }
        var extra=new BleedingTestHediff { rate=20f };
        AccessTools.Field(typeof(Hediff),"part").SetValue(extra,parts[0]); patient.health.hediffSet.hediffs.Add(extra);
        var chosen=PartHemostasis.BleedingParts(patient,TcccRules.HemostasisMaxParts);
        Check(chosen.Count==6 && chosen[0]==parts[0] && !chosen.Contains(parts[1]) && !chosen.Contains(parts[2]),
            "Hemostasis chooses six largest summed body-part bleed rates, including brain.");
        Check(PartHemostasis.BleedingParts(patient).Count==8 && PartHemostasis.BleedingParts(patient,0).Count==0,
            "Repeated wounds count once per part and explicit selection limit is respected.");
        var untreated=PartHemostasis.BleedingParts(patient,6,part=>part!=parts[0] && part!=parts[7]);
        Check(untreated.Count==6 && !untreated.Contains(parts[0]) && !untreated.Contains(parts[7]),
            "AI selection may omit already dressed parts without changing the six-part cap or player selection.");
        var duration=AccessTools.Method(typeof(JobDriver_MedibagTreatment),"TreatmentDuration");
        var bag=new CompMedibag { props=new CompProperties_Medibag { treatmentTicks=180,
            hemostasisPartCount=4 } };
        Check(bag.HemostasisDuration(patient)==600
            && (int)duration.Invoke(new JobDriver_MedibagPlasmaTransfusion(),new object[]{bag})==180,
            "Four bleeding parts take ten seconds with the bag; plasma keeps its three-second time.");
        Check(TcccRules.HemostasisDuration(0)==300 && TcccRules.HemostasisDuration(1)==300
            && TcccRules.HemostasisDuration(3)==300 && TcccRules.HemostasisDuration(4)==600
            && TcccRules.HemostasisDuration(5)==900 && TcccRules.HemostasisDuration(6)==1200
            && TcccRules.HemostasisDuration(8)==1200,
            "Hemostasis is five seconds through three parts, rising to twenty seconds at six.");
        Check(TcccRules.HemostaticTicks==60 && TcccRules.DragAttachTicks==60,
            "Hemostatic administration and casualty tether each take one second.");
        var small=new CompMedicalPouch { props=new CompProperties_MedicalPouch { capacity=4 } };
        var large=new CompMedicalPouch { props=new CompProperties_MedicalPouch { capacity=8, hemostaticCapacity=2 } };
        AccessTools.Field(typeof(CompMedicalPouch),"charges").SetValue(small,4);
        AccessTools.Field(typeof(CompMedicalPouch),"charges").SetValue(large,8);
        AccessTools.Field(typeof(CompMedicalPouch),"hemostatic").SetValue(large,2);
        Check(small.CanSpend(TcccTreatment.Hemostasis) && !small.CanSpend(TcccTreatment.Analgesic)
            && !small.CanSpend(TcccTreatment.Hemostatic)
            && large.CanSpend(TcccTreatment.Hemostasis) && large.CanSpend(TcccTreatment.Analgesic)
            && large.CanSpend(TcccTreatment.Hemostatic),
            "Small pouch permits basic care; large pouch also permits analgesic and loaded hemostatic care.");
        Check(small.TrySpend(TcccTreatment.Hemostasis) && small.Charges==3
            && large.TrySpend(TcccTreatment.Analgesic) && large.Charges==7
            && large.TrySpend(TcccTreatment.Hemostatic) && large.Hemostatic==1 && large.Charges==7,
            "Treatment charges and the separate hemostatic compartment decrease independently.");
        Check(Math.Abs(TcccDrugs.ReducedAddictionChance(.30f)-.21f)<.000001f,
            "Native analgesic addiction chance is reduced by thirty percent.");
        Check(TcccDrugs.DrugNameForBreathing(.699f)=="BD_Ketamine"
            && TcccDrugs.DrugNameForBreathing(.70f)=="BD_Fentanyl"
            && TcccDrugs.DrugNameForBreathing(1f)=="BD_Fentanyl",
            "Breathing below seventy percent selects ketamine; the boundary selects fentanyl.");
        var targets = Patch_PartHemostasis_Bleeding.TargetMethods().ToArray();
        Check(targets.Length==3 && targets.All(method=>method!=null) && targets.Distinct().Count()==3,
            "Real installed injury/missing-part/base bleeding getters bind independently.");
        foreach (var target in targets) Console.WriteLine("BOUND: "+target.DeclaringType.FullName+"."+target.Name);
        Console.WriteLine("PASS: "+checks+" part-specific hemostasis, expiry, strongest-only and cache checks.");
    }
}
