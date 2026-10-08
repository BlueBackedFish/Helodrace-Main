using System;
using System.Linq;
using HarmonyLib;
using Helodrace.Tactical;
using Verse;

internal static class PartHemostasisTests
{
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
            var marker = new Hediff_PartHemostasis { bleedingFactor=factor, expiresTick=expiry };
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
        var targets = Patch_PartHemostasis_Bleeding.TargetMethods().ToArray();
        Check(targets.Length==3 && targets.All(method=>method!=null) && targets.Distinct().Count()==3,
            "Real installed injury/missing-part/base bleeding getters bind independently.");
        foreach (var target in targets) Console.WriteLine("BOUND: "+target.DeclaringType.FullName+"."+target.Name);
        Console.WriteLine("PASS: "+checks+" part-specific hemostasis, expiry, strongest-only and cache checks.");
    }
}
