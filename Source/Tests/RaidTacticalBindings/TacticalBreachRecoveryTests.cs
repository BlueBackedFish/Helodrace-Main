using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace;
using Helodrace.Tactics;
using RimWorld;
using Verse;

internal static class TacticalBreachRecoveryTests
{
    internal static void Run()
    {
        Game previous = Current.Game;
        try
        {
            var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
            var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
            AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map> { map }); Current.Game = game;
            var definition = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef)); definition.defName = "Hammer";
            var donor = new Pawn { def = definition };
            donor.apparel = new Pawn_ApparelTracker(donor);
            donor.equipment = new Pawn_EquipmentTracker(donor);
            donor.health = (Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(donor.health, PawnHealthState.Down);
            var hammer = new Apparel { def = definition };
            AccessTools.Field(typeof(ThingWithComps), "comps").SetValue(hammer,
                new List<ThingComp> { new CompSledgehammerBreach { parent = hammer } });
            var owner = (ThingOwner<Apparel>)donor.apparel.GetDirectlyHeldThings();
            owner.InnerListForReading.Add(hammer); hammer.holdingOwner = owner;
            var tools = typeof(RaidTacticalPlan).Assembly.GetType("Helodrace.Tactics.TacticalBreachTools", true);
            MethodInfo source = AccessTools.Method(tools, "SourceFor");
            MethodInfo remember = AccessTools.Method(tools, "Remember");
            var command = new TacticalSquadCommand();
            remember.Invoke(null, new object[] { command, donor }); remember.Invoke(null, new object[] { command, donor });
            if (command.BreachTools.Count != 1 || source.Invoke(null, new object[] { hammer }) != donor)
                throw new Exception("Downed source and deduplicated equipment capture must survive roster removal.");
            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(donor.health, PawnHealthState.Mobile);
            if (source.Invoke(null, new object[] { hammer }) != null) throw new Exception("Do not steal a living carrier's gear.");
            AccessTools.Field(typeof(Pawn_HealthTracker), "healthState").SetValue(donor.health, PawnHealthState.Dead);
            var corpse = new Corpse();
            var corpseOwner = new ThingOwner<Pawn>(corpse); corpseOwner.InnerListForReading.Add(donor); donor.holdingOwner = corpseOwner;
            if (source.Invoke(null, new object[] { hammer }) != corpse || command.BreachTools[0] != hammer)
                throw new Exception("Deceased tool must resolve through the actual corpse independently of roster.");
            hammer.holdingOwner = null;
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(hammer, (sbyte)0);
            if (source.Invoke(null, new object[] { hammer }) != hammer) throw new Exception("Loose tool must become the source.");
            AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(hammer, (sbyte)-2);
            if (source.Invoke(null, new object[] { hammer }) != null) throw new Exception("Destroyed gear cannot be recovered.");
            Console.WriteLine("PASS: New corpse/downed/loose tool resolution, healthy-carrier rejection and reference retention.");
        }
        finally { Current.Game = previous; }
    }
}
