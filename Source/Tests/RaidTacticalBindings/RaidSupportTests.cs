using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using HarmonyLib;
using Helodrace.Squads;
using Verse;

internal static class RaidSupportTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
        var organization = new CombatOrganization();
        organization.SetBudget(new FormationPlan { initialRaidPoints = 1000, formationPointsSpent = 500 });
        int first = organization.ReserveSupport(100);
        Check(first > 0 && organization.SupportPointsRemaining == 400 && organization.SupportPointsReserved == 100,
            "A request must reserve funds before any delayed action.");
        Check(!organization.FinishSupport(first + 99, false) && organization.SupportPointsRemaining == 400,
            "A nonexistent request cannot mint a refund.");
        Check(organization.FinishSupport(first, false) && organization.SupportPointsRemaining == 500
            && !organization.FinishSupport(first, false), "Failure refunds exactly once.");
        var ids = Enumerable.Range(0, 4).Select(_ => organization.ReserveSupport(100)).ToArray();
        Check(ids.All(id => id > 0) && organization.ReserveSupport(1) == 0 && organization.PendingSupportCount == 4,
            "The shared organization queue must cap at four reservations.");
        Check(organization.FinishSupport(ids[0], true) && organization.SupportPointsRemaining == 100,
            "Success consumes the reservation without charging it again.");
        Check(organization.ReserveSupport(float.NaN) == 0 && organization.ReserveSupport(float.PositiveInfinity) == 0
            && organization.ReserveSupport(-1) == 0 && organization.ReserveSupport(101) == 0, "Invalid and overspent requests are rejected.");
        foreach (int id in ids.Skip(1)) organization.FinishSupport(id, false);
        Check(organization.SupportPointsRemaining == 400 && organization.SupportPointsReserved == 0,
            "Spent plus remaining funds must equal the original support budget.");
        CheckSave(Check);
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Defs"))) directory = directory.Parent;
        foreach (var node in XDocument.Load(Path.Combine(directory.FullName, "Defs/Organization/RaidStrategies.xml")).Root.Elements())
            foreach (var child in node.Elements())
                Check(AccessTools.Field(typeof(RimWorld.RaidStrategyDef), child.Name.LocalName) != null,
                    "Unsupported native raid XML field: " + child.Name);
        Check(typeof(LordJob_HelodSweep).GetMethod("CreateGraph").GetBaseDefinition().DeclaringType == typeof(Verse.AI.Group.LordJob),
            "The mission must use a real native Lord graph.");
        Check(typeof(LordToil_HelodWithdrawal).IsSubclassOf(typeof(RimWorld.LordToil_AssaultColony)),
            "Withdrawal preparation must remain visible to the existing tactical safety owner.");
        Console.WriteLine("PASS: " + checks + " raid support reservation, refund, save payload and native strategy checks.");
    }
    private static void CheckSave(Action<bool,string> check)
    {
        string path = Path.Combine(Path.GetTempPath(), "Helodrace-support-" + Guid.NewGuid() + ".xml");
        var organization = new CombatOrganization();
        organization.SetBudget(new FormationPlan { initialRaidPoints = 1000, formationPointsSpent = 500 });
        int pending = organization.ReserveSupport(80); organization.TrySpendSupportPoints(120);
        var cache = (IDictionary)AccessTools.Field(typeof(GenTypes), "typeCache").GetValue(null);
        Type key = typeof(GenTypes).GetNestedType("TypeCacheKey", BindingFlags.NonPublic);
        Type reservation = typeof(CombatOrganization.SupportReservation);
        cache[Activator.CreateInstance(key, new object[] { reservation.FullName, null })] = reservation;
        cache[Activator.CreateInstance(key, new object[] { typeof(CombatOrganization).FullName, null })] = typeof(CombatOrganization);
        bool previous = ScribeMetaHeaderUtility.modListChanged; ScribeMetaHeaderUtility.modListChanged = true;
        try
        {
            Scribe.saver.InitSaving(path, "support"); Scribe_Deep.Look(ref organization, "organization"); Scribe.saver.FinalizeSaving();
            CombatOrganization loaded = null;
            Scribe.loader.InitLoading(path); Scribe_Deep.Look(ref loaded, "organization");
            Scribe.ExitNode();
            foreach (string field in new[] { "curXmlParent", "curParent", "curPathRelToParent" })
                AccessTools.Field(typeof(ScribeLoader), field).SetValue(Scribe.loader, null);
            Scribe.mode = LoadSaveMode.Inactive;
            var crossRefs = AccessTools.Field(typeof(ScribeLoader), "crossRefs").GetValue(Scribe.loader);
            AccessTools.Method(crossRefs.GetType(), "ResolveAllCrossReferences").Invoke(crossRefs, null);
            var initer = AccessTools.Field(typeof(ScribeLoader), "initer").GetValue(Scribe.loader);
            AccessTools.Method(initer.GetType(), "DoAllPostLoadInits").Invoke(initer, null);
            check(loaded.SupportPointsRemaining == 300 && loaded.SupportPointsReserved == 80 && loaded.PendingSupportCount == 1,
                "Save/load must retain pending reservations and spent funds without rebudgeting.");
            check(loaded.FinishSupport(pending, false) && loaded.SupportPointsRemaining == 380
                && !loaded.FinishSupport(pending, false), "A restored request may refund exactly once.");
        }
        finally { Scribe.mode = LoadSaveMode.Inactive; ScribeMetaHeaderUtility.modListChanged = previous; if (File.Exists(path)) File.Delete(path); }
    }
}
