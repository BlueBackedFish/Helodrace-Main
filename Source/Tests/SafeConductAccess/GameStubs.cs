using System;
using System.Collections.Generic;
using Helodrace.Economy;

// Minimal native boundaries for the production access wrappers. The native
// Harmony script separately verifies both actual game instruction replacements.
namespace Verse
{
    public sealed class Pawn
    {
        public RimWorld.Faction Faction;
        public RimWorld.TraderKindDef TraderKind;
        public bool Spawned = true, Protected, CanSocial = true, HasPermit, Prisoner;
    }
    public readonly struct AcceptanceReport
    {
        public readonly bool Accepted;
        public AcceptanceReport(bool accepted) { Accepted = accepted; }
    }
}
namespace RimWorld
{
    public sealed class Faction
    {
        public static readonly Faction OfPlayer = new Faction();
        public bool Hostile;
        public bool IsPlayer => this == OfPlayer;
    }
    public sealed class TraderKindDef { public bool RequiresPermit; }
    public static class FactionUtility
    {
        public static bool ThrowDuringTrade;
        public static bool HostileTo(Faction a, Faction b)
            => a != b && a != null && b != null && !FinancialCaravanScope.Covers(a, b) && (a.Hostile || b.Hostile);
        public static Verse.AcceptanceReport CanTradeWith(Verse.Pawn actor, Faction faction, TraderKindDef kind)
        {
            if (ThrowDuringTrade) throw new InvalidOperationException("Native trade check failed");
            return new Verse.AcceptanceReport(actor.CanSocial && !HostileTo(faction, actor.Faction)
                && (kind?.RequiresPermit != true || actor.HasPermit));
        }
    }
    public sealed class FloatMenuContext { public Verse.Pawn FirstSelectedPawn; }
    public class FloatMenuOptionProvider_Trade
    {
        public IEnumerable<Verse.AcceptanceReport> GetOptionsFor(Verse.Pawn clickedPawn, FloatMenuContext context)
        {
            yield return FactionUtility.CanTradeWith(context.FirstSelectedPawn, clickedPawn.Faction, clickedPawn.TraderKind);
        }
    }
    public sealed class Building_Door { public bool PawnCanOpen(Verse.Pawn pawn) => true; }
}
namespace Verse.AI
{
    public static class GenAI
    {
        public static bool ThrowDuringDoorCheck;
        public static bool MachinesLike(RimWorld.Faction faction, Verse.Pawn pawn)
        {
            if (ThrowDuringDoorCheck) throw new InvalidOperationException("Native door check failed");
            return !pawn.Prisoner && !RimWorld.FactionUtility.HostileTo(pawn.Faction, faction);
        }
    }
}
namespace Helodrace.Economy
{
    public static class FinancialCaravanUtility
    {
        public static bool IsProtected(Verse.Pawn pawn) => pawn != null && pawn.Spawned && pawn.Protected;
    }
}
