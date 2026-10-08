using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace;
using Helodrace.ModernWar;
using Helodrace.Tactics;
using RimWorld;
using Verse;

internal static class TacticalCooperationTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        var goal = new IntVec3(100,0,100);
        var agenda = new TacticalCooperationAgenda("pair", "A", "B", goal, IntVec3.East, 900, 6000);
        var a = new TacticalCooperationState { Agenda = agenda, Stage = TacticalAgreementStage.Offered, NegotiationStarted = 0 };
        var b = new TacticalCooperationState();
        Check(!a.CanStart(1000,true) && !b.CanStart(1000,true), "An unconfirmed proposal cannot start a joint mission.");
        Check(b.Receive(TacticalMessageKind.Offer,agenda,"B",goal,40) && b.Negotiating && !b.Active, "Offer only creates the recipient's acceptance.");
        Check(a.Receive(TacticalMessageKind.Accept,agenda,"A",goal,80) && a.Active && !b.Active, "Sender's accepted state cannot silently mutate its peer.");
        Check(b.Receive(TacticalMessageKind.Confirm,agenda,"B",goal,120) && b.Active, "Recipient requires delivered confirmation.");
        Check(b.Receive(TacticalMessageKind.Offer,agenda,"B",goal,140) && b.Active && b.NegotiationStarted == 40, "Duplicate offers cannot downgrade or restart agreement timeout.");
        Check(a.CanStart(900,true) && !a.CanStart(899,true) && !a.CanStart(1000,false), "Separated LOW units use their pre-agreed time and own readiness.");
        a.LocalReady = true;
        Check(!a.Receive(TacticalMessageKind.Start,agenda,"A",goal,200,start:250), "Start cannot substitute for a peer readiness report.");
        Check(a.Receive(TacticalMessageKind.Status,agenda,"A",goal,210,ready:true,sampleTick:180) && a.PeerStatusAt == 180, "Status freshness uses send time.");
        Check(!a.Receive(TacticalMessageKind.Status,agenda,"A",goal,220,ready:false,sampleTick:170) && a.PeerReady, "Out-of-order status cannot roll readiness back.");
        Check(a.Receive(TacticalMessageKind.Start,agenda,"A",goal,230,start:260) && a.CanStart(260,true), "Fresh acknowledged readiness supports a delivered scheduled start.");
        Check(!a.Receive(TacticalMessageKind.Start,agenda,"A",goal,900,start:950), "Old readiness cannot authorize a new start.");
        Check(agenda.Area("A") != agenda.Area("B") && agenda.Side("A") == -agenda.Side("B"), "Pair roles must allocate distinct frontage and area directions.");
        Check(!b.Receive(TacticalMessageKind.Status,agenda,"B",goal+IntVec3.North,240), "Old mission messages cannot affect a new objective.");
        Check(b.Receive(TacticalMessageKind.Abort,agenda,"B",goal,300) && !b.Active
            && !b.Receive(TacticalMessageKind.Confirm,agenda,"B",goal,320), "Abort cannot be resurrected by an old confirmation.");
        var waiting = new TacticalCooperationState { Agenda=agenda, Stage=TacticalAgreementStage.Offered, NegotiationStarted=50 };
        Check(!waiting.Expired(649) && waiting.Expired(650) && a.Expired(6000), "Negotiation and mission deadlines provide bounded independent fallback.");
        var source = new TacticalContactMemory(); source.Remember(7,new IntVec3(12,0,12),new IntVec3(13,0,12),true,100,"A");
        var report = source.Entries.Single().Copy(); var recipient = new TacticalContactMemory();
        Check(recipient.Receive(report,200) && recipient.Entries.Single().SeenTick==100, "Reported contact retains original observation time.");
        source.Remember(7,new IntVec3(20,0,20),IntVec3.Invalid,false,250,"A");
        Check(recipient.Entries.Single().Position == new IntVec3(12,0,12), "Reports must be detached value snapshots.");
        recipient.Remember(7,new IntVec3(30,0,30),IntVec3.Invalid,false,300,"B");
        Check(!recipient.Receive(report,350) && recipient.Entries.Single().Origin=="B", "Old external knowledge cannot overwrite newer local observation.");
        Check(!new TacticalContactMemory().Receive(report,1900), "Delayed reports cannot refresh expired contacts.");
        Check(TacticalCommunicationPolicy.Radio(true,true,"net","net",300,200,199*199)
            && !TacticalCommunicationPolicy.Radio(true,true,"net","other",300,300,1)
            && !TacticalCommunicationPolicy.Radio(false,true,"net","net",300,300,1)
            && !TacticalCommunicationPolicy.Radio(true,true,"net","net",300,200,201*201), "Radio doctrine, network and shortest range are required.");
        // Actual installed item holder and production radio enumeration, without a Unity world.
        var armor = new Apparel { def = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef)) };
        var modular = new CompModularArmor { parent=armor, props=new CompProperties_ModularArmor() };
        AccessTools.Field(typeof(ThingWithComps),"comps").SetValue(armor,new List<ThingComp>{modular});
        var radioDef=(ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef)); radioDef.useHitPoints=true;
        var item = new ThingWithComps { def=radioDef, HitPoints=80 };
        var radio=new CompTacticalRadio {parent=item,props=new CompProperties_TacticalRadio()};
        AccessTools.Field(typeof(ThingWithComps),"comps").SetValue(item,new List<ThingComp>{radio});
        var part=new InstalledModularArmorPart();
        ((List<Thing>)AccessTools.Field(typeof(ThingOwner<Thing>),"innerList").GetValue(part.GetDirectlyHeldThings())).Add(item);
        var parts=new List<InstalledModularArmorPart>{part};
        AccessTools.Field(typeof(CompModularArmor),"installedParts").SetValue(modular,parts);
        int Radios(params Apparel[] worn) => ((IEnumerable<CompTacticalRadio>)AccessTools.Method(typeof(RaidTacticalRadioUtility),"InstalledRadios")
            .Invoke(null,new object[]{worn})).Count();
        Check(Radios(armor)==1,"Worn armor's physical radio module enables communication.");
        parts.Clear(); Check(Radios(armor)==0,"Removed radio part cannot communicate.");
        parts.Add(part); item.HitPoints=0; Check(Radios(armor)==0,"Broken radio cannot communicate.");
        item.HitPoints=80; Check(Radios(armor)==1 && Radios()==0,"Restored part works only while its vest is worn.");
        var tablet=new Apparel {def=armor.def};
        AccessTools.Field(typeof(ThingWithComps),"comps").SetValue(tablet,new List<ThingComp>{new CompTacticalRadio{parent=tablet,props=new CompProperties_TacticalRadio()}});
        Check(Radios(tablet)==0,"Temporary directly worn tablet is not armor radio hardware.");
        Console.WriteLine("PASS: "+checks+" R5 agreement, report freshness, isolated state, role allocation and physical armor radio checks.");
    }
}
