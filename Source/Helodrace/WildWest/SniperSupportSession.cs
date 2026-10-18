using System.Collections.Generic;
using Verse;

namespace Helodrace
{
    public enum HelodSniperSupportDirection
    {
        North,
        East,
        South,
        West
    }

    public sealed class HelodSniperSupportSession : IExposable
    {
        public const int DurationTicks = 2 * 2500;
        public const int MaxActions = 5;
        private HelodForwardBase forwardBase;
        private HelodSniperSupportDirection direction;
        private int expiresTick;
        private int remainingActions;

        public HelodForwardBase ForwardBase => forwardBase;
        public HelodSniperSupportDirection Direction => direction;
        public int RemainingActions => remainingActions;
        public int RemainingTicks(int now) => System.Math.Max(0, expiresTick - now);
        public bool IsWithinDuration(int now) => RemainingTicks(now) > 0;
        public bool CanRequestShot(int now) => IsWithinDuration(now) && remainingActions > 0;

        public HelodSniperSupportSession() { }

        public HelodSniperSupportSession(HelodForwardBase forwardBase,
            HelodSniperSupportDirection direction, int now)
        {
            this.forwardBase = forwardBase;
            this.direction = direction;
            expiresTick = now + DurationTicks;
            remainingActions = MaxActions;
        }

        public bool TryConsumeAction(int now)
        {
            if (!CanRequestShot(now)) return false;
            remainingActions--;
            return true;
        }

        // RimWorld's north edge has the highest z coordinate.
        public static IEnumerable<IntVec3> EdgeCells(IntVec3 mapSize, HelodSniperSupportDirection direction)
        {
            bool horizontal = direction == HelodSniperSupportDirection.North
                || direction == HelodSniperSupportDirection.South;
            int length = horizontal ? mapSize.x : mapSize.z;
            for (int i = 0; i < length; i++)
            {
                IntVec3 cell = horizontal
                    ? new IntVec3(i, 0, direction == HelodSniperSupportDirection.North ? mapSize.z - 1 : 0)
                    : new IntVec3(direction == HelodSniperSupportDirection.East ? mapSize.x - 1 : 0, 0, i);
                if (IsOnEdge(cell, mapSize, direction)) yield return cell;
            }
        }

        public static bool IsOnEdge(IntVec3 cell, IntVec3 mapSize,
            HelodSniperSupportDirection direction)
        {
            if (cell.x < 0 || cell.z < 0 || cell.x >= mapSize.x || cell.z >= mapSize.z)
                return false;
            switch (direction)
            {
                case HelodSniperSupportDirection.North: return cell.z == mapSize.z - 1;
                case HelodSniperSupportDirection.East: return cell.x == mapSize.x - 1;
                case HelodSniperSupportDirection.South: return cell.z == 0;
                case HelodSniperSupportDirection.West: return cell.x == 0;
                default: return false;
            }
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref forwardBase, "forwardBase");
            Scribe_Values.Look(ref direction, "direction");
            Scribe_Values.Look(ref expiresTick, "expiresTick");
            Scribe_Values.Look(ref remainingActions, "remainingActions");
        }
    }
}
