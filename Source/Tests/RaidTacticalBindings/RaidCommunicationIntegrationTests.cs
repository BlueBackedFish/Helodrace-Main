using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Helodrace;
using Helodrace.Squads;
using RimWorld;
using Verse;

internal static class RaidCommunicationIntegrationTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception(message); }
        Game previous = Current.Game;
        var game = (Game)RuntimeHelpers.GetUninitializedObject(typeof(Game));
        game.tickManager = (TickManager)RuntimeHelpers.GetUninitializedObject(typeof(TickManager));
        Current.Game = game;
        try
        {
            var bindingField = AccessTools.Field(typeof(DefOfHelper), "bindingNow");
            bool binding = (bool)bindingField.GetValue(null);
            try
            {
                // Suppress the Unity-only pre-def-load warning in this standalone host.
                // Vanilla smoke is real; custom smoke DefOfs remain absent from this fixture.
                bindingField.SetValue(null, true);
                RuntimeHelpers.RunClassConstructor(typeof(HelodGasDefOf).TypeHandle);
            }
            finally { bindingField.SetValue(null, binding); }
            var frameType = typeof(RaidContactMemory).Assembly.GetType("Helodrace.RaidCommunicationFrame");
            var map = (Map)RuntimeHelpers.GetUninitializedObject(typeof(Map));
            map.info = new MapInfo { Size = new IntVec3(100, 1, 100) };
            map.cellIndices = new CellIndices(map); map.edificeGrid = new EdificeGrid(map);
            map.gasGrid = (GasGrid)RuntimeHelpers.GetUninitializedObject(typeof(GasGrid));
            var gas = new uint[map.Area];
            AccessTools.Field(typeof(GasGrid), "map").SetValue(map.gasGrid, map);
            AccessTools.Field(typeof(GasGrid), "gasDensity").SetValue(map.gasGrid, gas);
            AccessTools.Field(typeof(Game), "maps").SetValue(game, new List<Map> { map });
            var execution = new MapComponent_RaidTacticalExecution(map);
            var communications = new MapComponent_RaidTacticalCommunications(map);
            AccessTools.Field(typeof(Map), "components").SetValue(map, new List<MapComponent> { execution, communications });
            var faction = (Faction)RuntimeHelpers.GetUninitializedObject(typeof(Faction));
            var doctrine = (DoctrineDef)RuntimeHelpers.GetUninitializedObject(typeof(DoctrineDef));
            doctrine.tacticalRadio = true; doctrine.radioReportTicks = 20; doctrine.voiceReportTicks = 40;
            doctrine.voiceContactRange = 8; doctrine.communicationAckTicks = 20;
            var formation = (FormationDef)RuntimeHelpers.GetUninitializedObject(typeof(FormationDef)); formation.unitLevel = "Squad";
            var pawnDef = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef)); pawnDef.defName = "RadioTestPawn";
            var a = new Pawn { thingIDNumber = 51001, def = pawnDef, Position = new IntVec3(1, 0, 1) };
            var b = new Pawn { thingIDNumber = 51002, def = pawnDef, Position = new IntVec3(70, 0, 70) };
            var receivingSoldier = new Pawn { thingIDNumber = 51003, def = pawnDef, Position = new IntVec3(71, 0, 70) };
            foreach (Pawn pawn in new[] { a, b, receivingSoldier })
                AccessTools.Field(typeof(Thing), "mapIndexOrState").SetValue(pawn, (sbyte)0);
            bool Voice() => (bool)AccessTools.Method(frameType, "VoiceTo").Invoke(null, new object[] { a, b, 8 });
            Check(!Voice(), "LOW physical contact is impossible outside the local voice radius.");
            AccessTools.Field(typeof(Thing), "positionInt").SetValue(b, new IntVec3(3, 0, 1));
            Check(Voice(), "Nearby mutually visible soldiers can begin an oral report.");
            var wallCell = new IntVec3(2, 0, 1);
            var wall = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef)); wall.fillPercent = 1f;
            map.edificeGrid.InnerArray[map.cellIndices.CellToIndex(wallCell)] = new Building { def = wall };
            Check(!Voice(), "A wall between adjacent units prevents physical confirmation/reporting.");
            map.edificeGrid.InnerArray[map.cellIndices.CellToIndex(wallCell)] = null;
            gas[map.cellIndices.CellToIndex(wallCell)] = 255u << (int)GasType.BlindSmoke;
            Check(!Voice(), "Dense smoke also prevents mutually visible LOW contact.");
            gas[map.cellIndices.CellToIndex(wallCell)] = 0;
            AccessTools.Field(typeof(Thing), "positionInt").SetValue(b, new IntVec3(70, 0, 70));
            object MakeFrame(string id, Pawn commander, params Pawn[] members)
            {
                var group = new CombatGroup { id = id, formation = formation,
                    roleAssignments = members.Select(pawn => new RoleAssignment { pawn = pawn }).ToList() };
                var organization = new CombatOrganization { id = id, faction = faction, doctrine = doctrine,
                    rootGroups = new List<CombatGroup> { group } };
                organization.RestoreTreeLinks();
                var unit = RaidTacticalUnit.ForGroup(group);
                var state = new MapComponent_RaidTacticalExecution.ExecutionState { UnitId = unit.Id };
                object frame = Activator.CreateInstance(frameType, true);
                void Set(string field, object value) => AccessTools.Field(frameType, field).SetValue(frame, value);
                Set("Unit", unit); Set("State", state); Set("Commander", commander);
                Set("Members", members.ToDictionary(pawn => pawn.thingIDNumber));
                Set("CommandDelays", members.ToDictionary(pawn => pawn.thingIDNumber, pawn => pawn == commander ? 0 : 40));
                CompTacticalRadio Radio()
                {
                    var thing = new ThingWithComps { def = pawnDef };
                    return new CompTacticalRadio { parent = thing, props = new CompProperties_TacticalRadio { network = "test", range = 300 } };
                }
                Set("Radios", members.ToDictionary(pawn => pawn.thingIDNumber, pawn => new List<CompTacticalRadio> { Radio() }));
                return frame;
            }
            object frameA = MakeFrame("A", a, a), frameB = MakeFrame("B", b, b, receivingSoldier);
            var stateA = (MapComponent_RaidTacticalExecution.ExecutionState)AccessTools.Field(frameType, "State").GetValue(frameA);
            var stateB = (MapComponent_RaidTacticalExecution.ExecutionState)AccessTools.Field(frameType, "State").GetValue(frameB);
            var frames = (IDictionary)AccessTools.Field(typeof(MapComponent_RaidTacticalCommunications), "frames").GetValue(communications);
            frames.Add(stateA.UnitId, frameA); frames.Add(stateB.UnitId, frameB);
            var pending = (List<RaidReportTransmission>)AccessTools.Field(typeof(MapComponent_RaidTacticalCommunications), "pending").GetValue(communications);
            var report = new RaidTacticalReport { Id = "A:enemy", OriginUnit = stateA.UnitId, ObserverId = a.thingIDNumber,
                ObservedTick = 0, Revision = 0, Kind = RaidReportKind.Contact, EnemyId = 91,
                Position = new IntVec3(3, 0, 3), Route = new List<string> { stateA.UnitId } };
            RaidReportTransmission Packet(int receiver, int due, RaidTacticalReport snapshot = null) => new RaidReportTransmission {
                FromUnit = stateA.UnitId, ToUnit = stateB.UnitId, FromPawn = a.thingIDNumber, ToPawn = receiver,
                StartedTick = 0, DueTick = due, Mode = RaidCommunicationMode.Radio, Report = (snapshot ?? report).Copy() };
            void Tick(int tick)
            {
                AccessTools.Field(typeof(TickManager), "ticksGameInt").SetValue(game.tickManager, tick);
                // Inject detached, already validated personnel/equipment frames; exercise the actual queue tick.
                // Unity health/lean generation and complete raid capture are verified in-game.
                AccessTools.Field(typeof(MapComponent_RaidTacticalCommunications), "frameTick").SetValue(communications, tick);
                AccessTools.Method(typeof(MapComponent_RaidTacticalCommunications), "ProcessTick").Invoke(communications, new object[] { tick });
            }
            pending.Add(Packet(b.thingIDNumber, 40));
            Tick(20);
            Check(stateB.Communication.Knowledge.Reports.Count == 0 && pending.Count == 1,
                "Actual communication tick does not deliver before the transmission deadline.");
            Tick(40);
            Check(stateB.Communication.Knowledge.Reports.Count == 1 && pending.Single().AwaitingAck,
                "Actual reception is followed by a distinct acknowledgement wait.");
            Check(stateB.Communication.Knowledge.Reports[0].ObservedTick == 0
                && stateB.Communication.Knowledge.Reports[0].ReceivedTick == 40,
                "The actual queue preserves source age while recording reception time.");
            Tick(60);
            Check(pending.Count == 0 && stateA.Communication.Receipts.Any(receipt => receipt.Status == "Acknowledged"),
                "Successful acknowledgement clears the actual pending queue and updates sender knowledge.");
            var radios = (Dictionary<int, List<CompTacticalRadio>>)AccessTools.Field(frameType, "Radios").GetValue(frameA);
            var radio = radios[a.thingIDNumber].Single();
            radios[a.thingIDNumber].Clear(); pending.Add(Packet(b.thingIDNumber, 100));
            Tick(80);
            Check(pending.Count == 0 && stateA.Communication.Receipts.Last().Status == "Interrupted",
                "Losing actual carried radio equipment cancels a pending distant report.");
            radios[a.thingIDNumber].Add(radio);
            var report2 = report.Copy(); report2.Id = "A:second"; report2.Revision = report2.ObservedTick = 100;
            pending.Add(Packet(b.thingIDNumber, 100, report2)); Tick(100);
            radios[a.thingIDNumber].Clear(); Tick(120);
            Check(pending.Count == 0 && stateB.Communication.Knowledge.Reports.Any(value => value.Id == report2.Id),
                "Loss during acknowledgement retains intelligence already delivered to the receiver.");
            radios[a.thingIDNumber].Add(radio);
            var report3 = report.Copy(); report3.Id = "A:ordinary"; report3.ObservedTick = report3.Revision = 140;
            pending.Add(Packet(receivingSoldier.thingIDNumber, 140, report3)); Tick(140);
            Check(stateB.Communication.For(receivingSoldier.thingIDNumber).Reports.Reports.Any(value => value.Id == report3.Id)
                && !stateB.Communication.Knowledge.Reports.Any(value => value.Id == report3.Id),
                "A report received by an ordinary soldier does not instantly reach their unit commander.");
            Check(pending.Any(packet => packet.FromUnit == stateB.UnitId && packet.ToUnit == stateB.UnitId
                && packet.Report.Id == report3.Id && packet.DueTick == 180),
                "The recipient's actual intra-unit command relay has its own contact delay.");
            Tick(160); Tick(180);
            Check(stateB.Communication.Knowledge.Reports.Any(value => value.Id == report3.Id && value.ObservedTick == 140),
                "Command learns the ordinary soldier's report only after the delayed relay.");
            var report4 = report.Copy(); report4.Id = "A:blackout"; report4.ObservedTick = report4.Revision = 200;
            pending.Clear(); pending.Add(Packet(b.thingIDNumber, 220, report4));
            AccessTools.Field(typeof(MapComponent_RaidTacticalCommunications), "blackout").SetValue(communications, true); Tick(200);
            Check(pending.Count == 0 && !stateB.Communication.Knowledge.Reports.Any(value => value.Id == report4.Id),
                "Actual equipment-network validation cancels distant transmission during blackout.");
            AccessTools.Field(typeof(MapComponent_RaidTacticalCommunications), "blackout").SetValue(communications, false);
            var rerouted = report.Copy(); rerouted.Id = "B:rerouted";
            rerouted.OriginUnit = stateB.UnitId; rerouted.ObservedTick = rerouted.Revision = 240;
            rerouted.Route = new List<string> { stateB.UnitId };
            stateB.Communication.For(receivingSoldier.thingIDNumber).Reports.Publish(rerouted);
            Tick(240);
            Check(pending.Any(packet => packet.Report.Id == rerouted.Id && packet.CommandDelay == 40),
                "A queued command relay retains the delay of its actual initial contact route.");
            var commandDelays = (Dictionary<int, int>)AccessTools.Field(frameType, "CommandDelays").GetValue(frameB);
            commandDelays[receivingSoldier.thingIDNumber] = 80;
            Tick(260);
            Check(!pending.Any(packet => packet.Report.Id == rerouted.Id)
                && !stateB.Communication.Knowledge.Reports.Any(value => value.Id == rerouted.Id),
                "A longer replacement command route interrupts instead of using the faster original deadline.");
            commandDelays[receivingSoldier.thingIDNumber] = 40;
            pending.Add(Packet(b.thingIDNumber, 1300)); Tick(1300);
            Check(!pending.Any(packet => packet.Report.Id == report.Id)
                && !stateB.Communication.Knowledge.Reports.Any(value => value.Id == report.Id),
                "Restored/delayed packets cannot deliver expired original observations.");
        }
        finally { Current.Game = previous; }
        Console.WriteLine($"PASS: {checks} native transmission delay, receipt, ACK, equipment loss, internal relay and blackout checks.");
    }
}
