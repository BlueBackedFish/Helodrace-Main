using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Helodrace
{
    public class HelodForwardBaseConstruction : WorldObject
    {
        private int completeTick;
        private string contractInfo;

        public int CompleteTick => completeTick;
        public string ContractInfo => contractInfo;

        public void StartConstruction(int durationTicks)
        {
            completeTick = Find.TickManager.TicksGame + durationTicks;
        }

        public void SetContractInfo(string info)
        {
            contractInfo = info;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref completeTick, "completeTick", 0);
            Scribe_Values.Look(ref contractInfo, "contractInfo");
        }

        protected override void Tick()
        {
            base.Tick();
            if (completeTick > 0 && Find.TickManager.TicksGame >= completeTick)
            {
                CompleteConstruction();
            }
        }

        public override string GetInspectString()
        {
            string inspect = base.GetInspectString();
            if (completeTick > Find.TickManager.TicksGame)
            {
                string timeLeft = (completeTick - Find.TickManager.TicksGame).ToStringTicksToPeriod();
                if (!inspect.NullOrEmpty())
                {
                    inspect += "\n";
                }

                inspect += "HD_ForwardBaseConstruction_TimeLeft".Translate(timeLeft);
            }

            if (!contractInfo.NullOrEmpty())
            {
                if (!inspect.NullOrEmpty())
                {
                    inspect += "\n";
                }

                inspect += contractInfo;
            }

            return inspect;
        }

        private void CompleteConstruction()
        {
            WorldObjectDef completeDef = DefDatabase<WorldObjectDef>.GetNamedSilentFail("HD_ForwardBase");
            if (completeDef == null)
            {
                Log.ErrorOnce("Helodrace: HD_ForwardBase WorldObjectDef is missing.", 72851061);
                return;
            }

            int targetTile = Tile;
            Faction targetFaction = Faction;
            Destroy();

            WorldObject complete = WorldObjectMaker.MakeWorldObject(completeDef);
            complete.Tile = targetTile;
            complete.SetFaction(targetFaction);
            HelodForwardBase forwardBase = complete as HelodForwardBase;
            if (forwardBase != null)
            {
                forwardBase.SetContractInfo(contractInfo);
            }
            Find.WorldObjects.Add(complete);
            Messages.Message("HD_ForwardBaseConstruction_Completed".Translate(), complete, MessageTypeDefOf.PositiveEvent);
        }
    }

    public class HelodForwardBase : WorldObject
    {
        private string contractInfo;

        public string ContractInfo => contractInfo;

        public void SetContractInfo(string info)
        {
            contractInfo = info;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref contractInfo, "contractInfo");
        }

        public override string GetInspectString()
        {
            string inspect = base.GetInspectString();
            if (!contractInfo.NullOrEmpty())
            {
                if (!inspect.NullOrEmpty())
                {
                    inspect += "\n";
                }

                inspect += contractInfo;
            }

            return inspect;
        }
    }
}
