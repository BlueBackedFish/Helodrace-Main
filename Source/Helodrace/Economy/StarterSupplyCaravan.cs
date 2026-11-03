using System.Linq;
using RimWorld;
using Verse;

namespace Helodrace.Economy;

public sealed class ScenPart_StarterSupplyCaravan : ScenPart
{
    public override void PostGameStart()
    {
        base.PostGameStart();
        Current.Game.GetComponent<GameComponent_StarterSupplyCaravan>().Arm(Find.AnyPlayerHomeMap);
    }
    public override string Summary(Scenario scen) => "HD_StarterSupply_Summary".Translate();
}

public sealed class GameComponent_StarterSupplyCaravan : GameComponent
{
    public const string TraderDefName = "HD_Caravan_StarterSupply";
    private bool armed, delivered, delayNotified;
    private int arrivalTick = -1, nextRetryTick = -1, attempts;
    private Map targetMap;
    public bool Armed => armed;
    public bool Delivered => delivered;
    public int ArrivalTick => arrivalTick;
    public int Attempts => attempts;
    public GameComponent_StarterSupplyCaravan(Game game) { }
    public void Arm(Map target)
    {
        if (armed || delivered) return;
        armed = true; targetMap = target;
        arrivalTick = Find.TickManager.TicksGame + GenDate.TicksPerDay;
        nextRetryTick = arrivalTick;
    }
    public override void ExposeData()
    {
        Scribe_Values.Look(ref armed, "starterSupplyArmed", false);
        Scribe_Values.Look(ref delivered, "starterSupplyDelivered", false);
        Scribe_Values.Look(ref delayNotified, "starterSupplyDelayNotified", false);
        Scribe_Values.Look(ref arrivalTick, "starterSupplyArrival", -1);
        Scribe_Values.Look(ref nextRetryTick, "starterSupplyNextRetry", -1);
        Scribe_Values.Look(ref attempts, "starterSupplyAttempts", 0);
        Scribe_References.Look(ref targetMap, "starterSupplyMap");
    }
    public override void GameComponentTick()
    {
        if (!armed || delivered || Find.TickManager.TicksGame < nextRetryTick) return;
        int now = Find.TickManager.TicksGame;
        nextRetryTick = now + 2500;
        if (targetMap == null || !Find.Maps.Contains(targetMap) || !targetMap.IsPlayerHome)
            targetMap = Find.Maps.FirstOrDefault(m => m.IsPlayerHome);
        Faction provider = Find.FactionManager.AllFactionsListForReading.FirstOrDefault(f =>
            f.def.defName == HelodCommercialCaravan.FactionName && !f.defeated && !f.HostileTo(Faction.OfPlayer));
        if (targetMap != null && provider != null
            && provider.def.allowedArrivalTemperatureRange.ExpandedBy(-4f).Includes(targetMap.mapTemperature.OutdoorTemp))
        {
            var parms = StorytellerUtility.DefaultParmsNow(IncidentDefOf.TraderCaravanArrival.category, targetMap);
            parms.faction = provider;
            parms.traderKind = DefDatabase<TraderKindDef>.GetNamed(TraderDefName);
            parms.points = System.Math.Max(120f, parms.points);
            parms.forced = true;
            attempts++;
            if (IncidentDefOf.TraderCaravanArrival.Worker.TryExecute(parms))
            {
                delivered = true;
                Messages.Message("HD_StarterSupply_Arrived".Translate(), MessageTypeDefOf.PositiveEvent);
                return;
            }
        }
        if (!delayNotified && now >= arrivalTick + 2 * GenDate.TicksPerDay)
        {
            delayNotified = true;
            Messages.Message("HD_StarterSupply_Delayed".Translate(), MessageTypeDefOf.NeutralEvent);
        }
    }
}