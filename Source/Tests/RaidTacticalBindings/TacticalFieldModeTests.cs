using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using HarmonyLib;
using Verse.AI;
using Helodrace;
using Helodrace.ModernWar;
using Helodrace.Tactics;
using Verse;

internal static class TacticalFieldModeTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
        Check(TacticalFieldPolicy.Prepare(false,false,false,30,10000,TacticalObservedMotion.Approaching),
            "A distant approaching observation must prepare cover before incoming damage.");
        foreach (var motion in new[]{TacticalObservedMotion.Unknown,TacticalObservedMotion.Stationary,TacticalObservedMotion.Crossing,TacticalObservedMotion.Leaving})
            Check(!TacticalFieldPolicy.Prepare(false,false,false,30,10000,motion),"A distant passive sighting must preserve the attacking mission.");
        var props = new CompProperties_SharpshooterWeapon(); props.ResolveReferences(null);
        Check(!props.DefensiveAI,"Untagged guns do not receive automatic modes.");
        props.aiModeTags = new(){"Defensive"}; props.ResolveReferences(null);
        Check(props.DefensiveAI,"Explicit tags are resolved once, outside ticks.");
        var weapon=(ThingWithComps)RuntimeHelpers.GetUninitializedObject(typeof(ThingWithComps));
        weapon.def=(ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef)); weapon.def.equipmentType=EquipmentType.Primary;
        var pawn=new Pawn { def=(ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef)) };
        pawn.def.defName="Helod"; pawn.equipment=new Pawn_EquipmentTracker(pawn);
        pawn.jobs=(Pawn_JobTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_JobTracker)); pawn.stances=new Pawn_StanceTracker(pawn);
        var owner=(ThingOwner<ThingWithComps>)AccessTools.Field(typeof(Pawn_EquipmentTracker),"equipment").GetValue(pawn.equipment);
        owner.InnerListForReading.Add(weapon); weapon.holdingOwner=owner;
        var comp = new CompSharpshooterWeapon { parent=weapon,props=props };
        var eq=new CompEquippable { parent=weapon };
        var normal=new Verb_Shoot { verbProps=new VerbProperties { isPrimary=true }, caster=pawn };
        var alternate=new Verb_Shoot { verbProps=new VerbProperties(),caster=pawn };
        normal.verbTracker=alternate.verbTracker=eq.verbTracker;
        AccessTools.Field(typeof(VerbTracker),"verbs").SetValue(eq.verbTracker,new List<Verb>{normal,alternate});
        AccessTools.Field(typeof(ThingWithComps),"comps").SetValue(weapon,new List<ThingComp>{comp,eq});
        Check(comp.CanSwitchMode && comp.TrySetMode(true) && comp.TrySetMode(true),"Equipped capable shooters switch immediately.");
        Check(comp.altModeActive,"Repeated desired ON is idempotent.");
        Check(comp.SelectedVerb==alternate,"Immediate selection uses the alternate verb.");
        CheckAuxiliaryFire(pawn,comp,eq,normal,alternate,Check);
        var cooldown=(Stance_Cooldown)RuntimeHelpers.GetUninitializedObject(typeof(Stance_Cooldown));
        cooldown.ticksLeft=90; cooldown.verb=alternate; pawn.stances.curStance=cooldown;
        Check(!comp.PerformSwitch() && comp.altModeActive,"Actual native cooldown blocks mode switching without consuming its timer.");
        Check(((Stance_Cooldown)pawn.stances.curStance).ticksLeft==90,"A rejected toggle does not erase cooldown.");
        pawn.stances.curStance=new Stance_Mobile();
        AccessTools.Field(typeof(Verb),"burstShotsLeft").SetValue(alternate,2);
        Check(!comp.CanSwitchMode && !comp.PerformSwitch(),"An active native burst cannot be escaped by mode switching.");
        alternate.Reset();
        var warmup=(Stance_Warmup)RuntimeHelpers.GetUninitializedObject(typeof(Stance_Warmup));
        warmup.ticksLeft=100; warmup.verb=alternate; pawn.stances.curStance=warmup;
        Check(comp.PerformSwitch() && pawn.stances.curStance is Stance_Mobile && !comp.altModeActive,
            "Immediate mode switching cancels actual weapon warmup, without issuing or replacing a job.");
        Check(comp.TrySetMode(true,true),"AI can select the same immediate mode path.");
        cooldown=(Stance_Cooldown)RuntimeHelpers.GetUninitializedObject(typeof(Stance_Cooldown));
        cooldown.ticksLeft=90; cooldown.verb=alternate; pawn.stances.curStance=cooldown;
        comp.ReleaseAIMode();
        Check(comp.altModeActive && comp.aiModeOwned,"AI cleanup must also respect an active cooldown.");
        pawn.stances.curStance=new Stance_Mobile(); comp.TickEquipped(pawn,100);
        Check(!comp.altModeActive && !comp.aiModeOwned,"Deferred cleanup releases the mode when cooldown ends.");
        comp.TrySetMode(true);
        comp.aiModeOwned = true; comp.ReleaseAIMode(); comp.ReleaseAIMode();
        Check(!comp.altModeActive && !comp.aiModeOwned,"AI cleanup idempotently releases only owned modes.");
        comp.TrySetMode(true); comp.ReleaseAIMode();
        Check(comp.altModeActive,"Unowned manual modes survive cleanup.");
        comp.aiModeOwned = true; comp.PerformSwitch();
        Check(!comp.aiModeOwned && !comp.altModeActive,"Manual switching takes ownership away from AI.");
        CheckPreparedFire(pawn,comp,normal,alternate,Check);
        var clock=new SharpshooterPreparationClock(); var cell=new IntVec3(5,0,5);
        clock.Update(true,false,cell,100);
        Check(clock.Remaining(219,120)==1 && clock.Remaining(220,120)==0,"Stationary setup respects the exact two-second boundary.");
        clock.Update(true,false,cell,300);
        Check(clock.StartedAt==100,"Repeated stop/inspection calls do not restart completed setup.");
        clock.Update(true,true,cell,301);
        Check(clock.Remaining(1000,120)==120,"Movement clears readiness instead of letting its old clock finish.");
        clock.Update(true,false,cell,400);
        Check(clock.Remaining(519,120)==1 && clock.Remaining(520,120)==0,"Stopping starts a full fresh setup.");
        clock.Update(true,false,cell+IntVec3.East,600);
        Check(clock.StartedAt==600,"Unexpected displacement cannot reuse old stationary preparation.");
        clock.Update(false,false,cell,700);
        Check(clock.StartedAt==-1 && clock.Remaining(700,0)==0,"Normal mode has no setup timer or shot blockade.");
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../../"));
        var defs = XDocument.Load(Path.Combine(root,"Defs/GreatWar/Items/Weapons_GreatWar.xml"));
        string[] tagged = defs.Root.Elements("ThingDef").Where(x=>x.Descendants("aiModeTags").Any(t=>t.Elements("li").Any(v=>v.Value=="Defensive")))
            .Select(x=>x.Element("defName").Value).OrderBy(x=>x).ToArray();
        Check(tagged.SequenceEqual(new[]{"HD_Gun_M1903A4_Weapon","HD_Gun_M1918A2HAR_Weapon","HD_Gun_M1919A6LMG_Weapon","HD_Gun_M1D_Weapon"}.OrderBy(x=>x)),
            "Only reviewed long range/automatic defensive modes are tagged.");
        var west=XDocument.Load(Path.Combine(root,"Defs/WildWest/Items/Weapons_WildWest.xml"));
        var prepared=defs.Root.Elements("ThingDef").Concat(west.Root.Elements("ThingDef"))
            .Where(x=>x.Descendants("stationaryPreparationTicks").Any(t=>t.Value=="120"))
            .Select(x=>x.Element("defName").Value).OrderBy(x=>x);
        Check(prepared.SequenceEqual(new[]{"HD_Gun_SprMOne_Weapon","HD_Gun_SprMThreeHigh_Weapon","HD_Gun_M1903A4_Weapon",
            "HD_Gun_M1918A2HAR_Weapon","HD_Gun_M1919A6LMG_Weapon","HD_Gun_M1D_Weapon"}.OrderBy(x=>x)),"Exactly the six requested guns opt into stationary preparation.");
        Check(!defs.Descendants("moveSpeedOffset").Any() && !defs.Descendants("switchTicks").Any(),"Obsolete movement penalties and timed switching are removed.");
        var hooks=Patch_SharpshooterPreparationCast.TargetMethods().Cast<System.Reflection.MethodInfo>().ToArray();
        Check(hooks.Length==2 && hooks.All(m=>m.GetParameters().Length==5 || m.GetParameters().Length==6),"Both native cast overloads, including mobile-fire shortcuts, are gated.");
        Console.WriteLine("PASS: " + checks + " field approach and defensive desired-mode checks.");
    }
    private static void CheckAuxiliaryFire(Pawn pawn,CompSharpshooterWeapon comp,CompEquippable eq,
        Verb normal,Verb alternate,Action<bool,string> check)
    {
        // Auxiliary verbs can also have isPrimary=true by default. Only the first
        // primary and the configured mode verb belong to the rifle's mode switch.
        var grenade=new Verb_ShootModularUnderbarrel { verbProps=new VerbProperties { isPrimary=true },caster=pawn,verbTracker=eq.verbTracker };
        var auxiliary=new Verb_Shoot { verbProps=new VerbProperties { isPrimary=true },caster=pawn,verbTracker=eq.verbTracker };
        eq.AllVerbs.Add(grenade); eq.AllVerbs.Add(auxiliary);
        try
        {
            check(comp.IsModeVerb(normal) && comp.IsModeVerb(alternate)
                && !comp.IsModeVerb(grenade) && !comp.IsModeVerb(auxiliary),
                "Mode selection owns only the rifle verbs, including when an auxiliary verb is marked primary.");
            foreach(bool alt in new[]{false,true})
            {
                comp.altModeActive=alt;
                foreach(Verb verb in new Verb[]{grenade,auxiliary})
                {
                    bool result=false;
                    check(Patch_SharpshooterCurrentVerbShortCast.Prefix(verb,default,false,false,false,false,ref result) && !result,
                        "The short cast overload must preserve auxiliary fire in both rifle modes.");
                    check(Patch_SharpshooterCurrentVerbCast.Prefix(verb,default,default,false,false,false,false,ref result) && !result,
                        "The destination cast overload must preserve auxiliary fire in both rifle modes.");
                }
            }
            var warmup=(Stance_Warmup)RuntimeHelpers.GetUninitializedObject(typeof(Stance_Warmup));
            warmup.verb=grenade; warmup.ticksLeft=90; pawn.stances.curStance=warmup;
            check(comp.PerformSwitch() && pawn.stances.curStance==warmup && warmup.ticksLeft==90,
                "Switching rifle modes must preserve the underbarrel launcher's ongoing aim.");
            var doubleTap=new CompCEDoubleTapWeapon { parent=comp.parent,
                props=new CompProperties_CEDoubleTapWeapon { normalVerbIndex=0,doubleTapVerbIndex=1 } };
            check(doubleTap.IsModeVerb(normal) && doubleTap.IsModeVerb(alternate) && !doubleTap.IsModeVerb(grenade),
                "CE double-tap mode selection also excludes the independent grenade launcher.");
        }
        finally
        {
            eq.AllVerbs.Remove(grenade); eq.AllVerbs.Remove(auxiliary);
            comp.altModeActive=true; pawn.stances.curStance=new Stance_Mobile();
        }
    }
    private static void CheckPreparedFire(Pawn pawn,CompSharpshooterWeapon comp,Verb normal,Verb alternate,Action<bool,string> check)
    {
        Game old=Current.Game;
        var game=(Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        game.tickManager=(TickManager)RuntimeHelpers.GetUninitializedObject(typeof(TickManager));
        var tickField=AccessTools.Field(typeof(TickManager),"ticksGameInt");
        pawn.health=(Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
        AccessTools.Field(typeof(Pawn_HealthTracker),"healthState").SetValue(pawn.health,PawnHealthState.Mobile);
        comp.Props.stationaryPreparationTicks=120;
        try
        {
            Current.Game=game; tickField.SetValue(game.tickManager,100);
            check(comp.TrySetMode(true) && !comp.ReadyToFire,"Selecting prepared fire is instant but firing is initially unavailable.");
            bool result=true;
            check(!Patch_SharpshooterPreparationCast.Prefix(normal,ref result) && !result,
                "A retained normal-fire command cannot shoot while the selected mode prepares.");
            result=true;
            check(!Patch_SharpshooterPreparationCast.Prefix(alternate,ref result) && !result,
                "The alternate cast cannot start its warmup before stationary setup.");
            var grenade=new Verb_ShootModularUnderbarrel { verbProps=new VerbProperties(),caster=pawn,verbTracker=normal.verbTracker };
            result=true;
            check(Patch_SharpshooterPreparationCast.Prefix(grenade,ref result) && result,
                "Rifle stationary preparation must not block the independent underbarrel launcher.");
            AccessTools.Field(typeof(Verb),"burstShotsLeft").SetValue(grenade,1);
            Patch_SharpshooterBurstPreparation.Postfix(grenade,ref result);
            check(result,"Rifle stationary preparation must not cancel an auxiliary shot already in progress.");
            tickField.SetValue(game.tickManager,219);
            check(!comp.ReadyToFire,"Actual equipped mode waits through the final preparation tick.");
            tickField.SetValue(game.tickManager,220);
            check(comp.ReadyToFire && comp.AltModeEffective,"The prepared mode becomes effective at the exact completion boundary.");
            result=false;
            check(Patch_SharpshooterPreparationCast.Prefix(alternate,ref result),"Completed setup lets native firing resume.");
            var warmup=(Stance_Warmup)RuntimeHelpers.GetUninitializedObject(typeof(Stance_Warmup));
            warmup.verb=alternate; warmup.ticksLeft=90; pawn.stances.curStance=warmup;
            comp.NotifyMovementStarted(pawn);
            check(comp.altModeActive && pawn.stances.curStance is Stance_Mobile && comp.Preparation.StartedAt==-1,
                "Movement cancels warmup and readiness without disabling selection or issuing a preparation job.");
            tickField.SetValue(game.tickManager,300); comp.TickEquipped(pawn,300);
            tickField.SetValue(game.tickManager,419);
            check(!comp.ReadyToFire,"The next stopped position needs its own full setup.");
            tickField.SetValue(game.tickManager,420);
            check(comp.ReadyToFire,"Repeated stationary setup eventually enables the same selected mode.");
            comp.NotifyMovementStarted(pawn); AccessTools.Field(typeof(Verb),"burstShotsLeft").SetValue(alternate,2);
            result=true; Patch_SharpshooterBurstPreparation.Postfix(alternate,ref result);
            check(!result,"Movement invalidation prevents remaining alternate burst shots through the native unavailable path.");
            alternate.Reset(); pawn.stances.curStance=new Stance_Mobile();
            check(comp.TrySetMode(false) && comp.ReadyToFire && comp.SelectedVerb==normal,"Switching back to normal restores normal fire immediately.");
        }
        finally { Current.Game=old; comp.Props.stationaryPreparationTicks=0; }
    }
}
