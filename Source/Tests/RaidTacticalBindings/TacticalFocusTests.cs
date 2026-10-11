using System;
using System.IO;
using System.Linq;
using Helodrace.Tactics;
using Verse;

internal static class TacticalFocusTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
        TacticalMeleeThreat Threat(int id, int urgency, bool shield, int tick = 100) => new TacticalMeleeThreat {
            EnemyId = id, Position = new IntVec3(12 + id, 0, 10), Urgency = urgency,
            Shielded = shield, SeenTick = tick, Origin = "source" };
        var anchor = new IntVec3(10, 0, 10);
        Check(TacticalFocusPolicy.Urgency(false, 1) == 0, "Passive proximity must not create a melee threat.");
        Check(TacticalFocusPolicy.Urgency(true, 9) == 3 && TacticalFocusPolicy.Urgency(true, 100) == 2
            && TacticalFocusPolicy.Urgency(true, 900) == 1 && TacticalFocusPolicy.Urgency(true, 901) == 0,
            "Intent and contact danger boundaries must agree.");
        var plain = Threat(1, 2, false); var shield = Threat(2, 2, true); var imminent = Threat(3, 3, false);
        var candidates = new[] { plain, shield };
        Check(TacticalFocusPolicy.Select(candidates, -1, 0, anchor, 100) == shield,
            "A shield bearer must win among comparably urgent approaching enemies.");
        Check(TacticalFocusPolicy.Select(candidates, plain.EnemyId, 100, anchor, 120) == plain,
            "The brief hold must prevent equal-urgency target thrashing.");
        Check(TacticalFocusPolicy.Select(candidates, plain.EnemyId, 100, anchor, 200) == shield,
            "Shield priority must apply once the brief hold expires.");
        Check(TacticalFocusPolicy.Select(new[] { shield, imminent }, shield.EnemyId, 100, anchor, 101) == imminent,
            "An imminent unshielded attacker must override a distant shield and the hold.");
        shield.Shielded = false;
        Check(TacticalFocusPolicy.Select(new[] { shield }, shield.EnemyId, 100, anchor, 150) == shield,
            "Breaking the shield alone must not end an ongoing melee approach.");
        Check(TacticalFocusPolicy.Select(candidates, -1, 0, anchor, 280) == null,
            "LOS loss must expire the focus without accessing a hidden Pawn.");
        var sender = new TacticalMeleeMemory(); var receiver = new TacticalMeleeMemory();
        Check(sender.Receive(plain, 100) && receiver.Receive(sender.Entries.Single(), 120), "Fresh melee reports must transfer.");
        plain.Urgency = 0;
        Check(sender.Entries.Single().Urgency == 2 && receiver.Entries.Single().Urgency == 2,
            "Report and recipient storage must own detached value snapshots.");
        var retreat = Threat(1, 0, false, 130);
        Check(receiver.Receive(retreat, 130) && !receiver.Dangerous(130), "A visible retreat must retract danger.");
        Check(!receiver.Receive(Threat(1, 3, true, 100), 150) && !receiver.Dangerous(150),
            "An older queued report must not resurrect a visibly resolved attacker.");
        Check(!receiver.Receive(Threat(5, 3, true, 200), 150), "Future-dated reports must be rejected.");
        Check(!receiver.Receive(Threat(5, 3, true, 1), 181), "Expired reports must be rejected.");
        for (int id = 2; id <= 8; id++) sender.Receive(Threat(id, 1, false, 100 + id), 110);
        Check(sender.Entries.Count == TacticalFocusPolicy.CandidateLimit && sender.Find(1) == null,
            "Threat memory must retain a fixed cap and evict old identities.");
        sender.Expire(288); Check(sender.Entries.Count == 0, "No snapshot may survive its freshness window.");
        var ranged = new TacticalContactMemory();
        Check(ranged.Entries.Count == 0 && receiver.Entries.Count == 1,
            "Melee reports must remain separate from ranged observation eligibility.");
        string path = Path.Combine(Path.GetTempPath(), "Helodrace-Focus-" + Guid.NewGuid() + ".xml");
        try
        {
            Scribe.saver.InitSaving(path, "threat"); retreat.ExposeData(); Scribe.saver.FinalizeSaving();
            var loaded = new TacticalMeleeThreat();
            Scribe.loader.InitLoading(path); loaded.ExposeData(); Scribe.loader.ForceStop();
            Check(loaded.EnemyId == 1 && loaded.Urgency == 0 && loaded.SeenTick == 130
                && loaded.Position == retreat.Position && loaded.Origin == "source" && !loaded.Shielded,
                "Native Scribe must retain both threat and explicit retreat report values.");
        }
        finally { Scribe.ForceStop(); if (File.Exists(path)) File.Delete(path); }
        Console.WriteLine($"PASS: {checks} A1.4 melee danger, shield priority, stability, report isolation/expiry and native save payload checks.");
    }
}
