using System.Collections.Generic;
using System.Linq;
using Helodrace.Economy;
using RimWorld;
using Verse;
using Verse.AI;

namespace Helodrace.Tests.Stage2;

public sealed partial class Checks
{
    private List<Verb> KnifeVerbs(Pawn pawn, Apparel knife, Thing target)
    {
        var owned = knife.TryGetComp<CompWornMeleeWeapon>().AllVerbs;
        var entries = pawn.meleeVerbs.GetUpdatedAvailableVerbsList(false).ToList();
        Check(owned.All(v => v.caster == pawn && entries.Count(e => e.verb == v) == 1
            && entries.Single(e => e.verb == v).GetSelectionWeight(target) > 0f),
            "Each worn knife attack has the wearer as caster and one positively weighted native entry");
        float highest = entries.Max(e => VerbUtility.InitialVerbWeight(e.verb, pawn));
        foreach (VerbEntry entry in entries)
        {
            var category = VerbUtility.SelectionCategoryForWeight(VerbUtility.InitialVerbWeight(entry.verb, pawn), highest);
            int peers = entries.Count(e => VerbUtility.SelectionCategoryForWeight(VerbUtility.InitialVerbWeight(e.verb, pawn), highest) == category);
            Check(entry.GetSelectionWeight(target) == VerbUtility.FinalSelectionWeightFor(category, peers)
                * (target.def.IsEdifice() ? entry.verb.verbProps.commonalityVsEdificeFactor : 1f),
                "Attack weight matches 1.7 native DPS category: " + entry.verb.tool?.label);
        }
        return owned;
    }
    private IEnumerable<int> MeleeChecks()
    {
        fighter = NewPawn(HelodRace.ColonistKind, Faction.OfPlayer, map.Center + new IntVec3(10, 0, 0));
        fighter.equipment.DestroyAllEquipment();
        fighter.equipment.AddEquipment((ThingWithComps)ThingMaker.MakeThing(Def<ThingDef>("Gun_BoltActionRifle")));
        fighter.skills.GetSkill(SkillDefOf.Melee).Level = 20;
        savedKnife = (Apparel)ThingMaker.MakeThing(Def<ThingDef>("HD_USMCFightingKnife"), ThingDefOf.Steel);
        fighter.apparel.Wear(savedKnife, false);
        Check(fighter.apparel.WornApparel.Contains(savedKnife), "Knife actually worn with a rifle equipped");
        Thing target = ThingMaker.MakeThing(ThingDefOf.Wall, ThingDefOf.WoodLog);
        GenSpawn.Spawn(target, fighter.Position + new IntVec3(1, 0, 0), map);
        var owned = KnifeVerbs(fighter, savedKnife, target);
        int knifeSelections = 0;
        Rand.PushState(8181);
        try
        {
            for (int i = 0; i < 200; i++)
            {
                fighter.meleeVerbs.Notify_PawnDespawned();
                if (owned.Contains(fighter.meleeVerbs.TryGetMeleeVerb(target))) knifeSelections++;
            }
        }
        finally { Rand.PopState(); }
        Write("MELEE native knife selections=" + knifeSelections + "/200");
        Check(knifeSelections > 0 && knifeSelections < 200, "Normal melee selection uses the knife and retains other attacks");
        int initial = target.HitPoints;
        bool cooldown = false;
        for (int i = 0; i < 12 && !target.Destroyed && target.HitPoints == initial; i++)
        {
            fighter.meleeVerbs.TryMeleeAttack(target, owned[i % owned.Count]);
            cooldown |= fighter.stances.curStance is Stance_Cooldown;
            yield return 150;
        }
        Check(!target.Destroyed && target.HitPoints < initial && cooldown,
            "Actual worn-knife attacks damage a target and apply melee cooldown while holding a rifle");
        Write("MELEE damage=" + (initial - target.HitPoints));
        fighter.apparel.Remove(savedKnife);
        Check(owned.All(v => v.caster == null)
            && fighter.meleeVerbs.GetUpdatedAvailableVerbsList(false).All(e => !owned.Contains(e.verb)),
            "Removing the knife clears caster and all attack entries immediately");
        fighter.apparel.Wear(savedKnife, false);
        KnifeVerbs(fighter, savedKnife, target);
        roundtripStage = 1;
        GameDataSaveLoader.SaveGame("Stage2Roundtrip");
        Write("SAVE: Stage2Roundtrip with rifle, worn knife and financial ledger");
        loadRequested = true;
        yield return 0;
    }
    private IEnumerable<int> AfterLoad()
    {
        Check(fighter != null && savedKnife != null && fighter.apparel.WornApparel.Contains(savedKnife)
            && fighter.equipment.Primary.def.defName == "Gun_BoltActionRifle", "Actual save/load preserves the rifle and worn knife");
        var target = map.listerThings.ThingsOfDef(ThingDefOf.Wall).First(t => t.Position.AdjacentToCardinal(fighter.Position));
        var old = KnifeVerbs(fighter, savedKnife, target).ToList();
        Check(GameComponent_FinancialGoodwill.Current.Get(Low) > 0f, "Helod financial balance survives actual save/load");
        fighter.apparel.Remove(savedKnife);
        var replacement = (Apparel)ThingMaker.MakeThing(savedKnife.def, ThingDefOf.Steel);
        fighter.apparel.Wear(replacement, false);
        var fresh = KnifeVerbs(fighter, replacement, target);
        Check(fighter.meleeVerbs.GetUpdatedAvailableVerbsList(false).All(e => !old.Contains(e.verb)),
            "Replacing a knife after load leaves no old attack entries");
        Pawn other = NewPawn(HelodRace.ColonistKind, Faction.OfPlayer, fighter.Position + new IntVec3(0, 0, 2));
        other.apparel.Wear(savedKnife, false);
        var transferred = KnifeVerbs(other, savedKnife, target);
        Check(transferred.All(v => v.caster == other)
            && fighter.meleeVerbs.GetUpdatedAvailableVerbsList(false).All(e => !transferred.Contains(e.verb)),
            "Transferring the old knife binds its new wearer without leaking attacks to the former wearer");
        fighter.apparel.Remove(replacement);
        Check(fresh.All(v => v.caster == null), "Replacement knife clears its caster on removal");
        yield return 120;
    }
}
