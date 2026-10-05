using System;
using System.Xml;
using HarmonyLib;
using Helodrace;
using Verse;

internal static class RaidPawnCommandTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
        var order = new RaidPawnOrder();
        var target = new IntVec3(20, 0, 20);
        var assign = AccessTools.Method(typeof(RaidPawnCommand), "Assign");
        bool Assign(IntVec3 destination, RaidMovementNode connection = null) => (bool)assign.Invoke(order.Command,
            new object[] { RaidCommandOwner.Security, RaidOrderKind.Move, destination, false, true, 3f, false, connection });
        Check(Assign(target) && order.Command.Revision == 1, "A role owns a newly committed movement intent.");
        order.Kind = RaidOrderKind.Hold; order.Destination = new IntVec3(10, 0, 10);
        Check(order.Command.Kind == RaidOrderKind.Move && order.Command.Destination == target,
            "Temporary admission holds retain the original Security movement intent.");
        Check(!Assign(target) && order.Command.Revision == 1, "Repeated intent does not renew its ownership generation.");
        var connection = new RaidMovementNode { Center = target };
        Check(Assign(target, connection) && order.Command.Revision == 2,
            "A new connection revises the affected pawn's task even with the same endpoint.");
        Check(RaidOrderPolicy.RecoverMove(true, false, false, false),
            "An owned Wait cannot silently swallow an outstanding Goto.");
        Check(!RaidOrderPolicy.RecoverMove(true, false, true, false)
            && !RaidOrderPolicy.RecoverMove(true, true, false, false)
            && !RaidOrderPolicy.RecoverMove(true, false, false, true)
            && !RaidOrderPolicy.RecoverMove(false, false, false, false),
            "Stable paths, arrival, retry windows and intended holds do not cause repeated job replacement.");
        LoadSaveMode mode = Scribe.mode;
        XmlNode parent = Scribe.loader.curXmlParent;
        IExposable exposable = Scribe.loader.curParent;
        try
        {
            var xml = new XmlDocument();
            xml.LoadXml("<command><owner>Security</owner><kind>Move</kind><destination>(20, 0, 20)</destination>"
                + "<fightOnArrival>True</fightOnArrival><radius>3</radius><revision>8</revision></command>");
            var command = new RaidPawnCommand();
            Scribe.mode = LoadSaveMode.LoadingVars; Scribe.loader.curXmlParent = xml.DocumentElement;
            Scribe.loader.curParent = command; command.ExposeData();
            Check(command.Owner == RaidCommandOwner.Security && command.Kind == RaidOrderKind.Move
                && command.Destination == target && command.FightOnArrival && command.Radius == 3f && command.Revision == 8,
                "Real Scribe restores task ownership and intent, rather than only the temporary hold.");
        }
        finally { Scribe.mode = mode; Scribe.loader.curXmlParent = parent; Scribe.loader.curParent = exposable; }
        Console.WriteLine($"PASS: {checks} durable role intent, transient hold and movement recovery checks.");
    }
}
