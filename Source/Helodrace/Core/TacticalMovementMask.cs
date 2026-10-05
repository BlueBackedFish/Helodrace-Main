using System;
using System.Threading;

namespace Helodrace
{
    internal sealed class TacticalMovementMaskInput
    {
        public TacticalGeometryResult Structure;
        public int Width;
        public int Height;
        public bool Reactive;
        public bool ExteriorOnly;
        public int InitialRoom;
        public int ExcludedRoom;
        public bool SelectedOpeningOnly;
        public int BreachIndex = -1;
        public bool RestrictPortals;
        public bool RestrictRooms;
        public int[] AllowedRooms = Array.Empty<int>();
        public bool RestrictCells;
        public int[] AllowedCells = Array.Empty<int>();
        public int[] AllowedPortals = Array.Empty<int>();
        public bool Fight;
        public int FightX;
        public int FightZ;
        public float FightRadius;
        public int FightRoom;
        public int LeashX;
        public int LeashZ;
        public float LeashRadius;
    }

    // A captured connection is read by the worker and the live path follower.
    // Hash sets are built once per shared input, never per pawn path step.
    internal sealed class TacticalMovementPermission
    {
        private readonly TacticalMovementMaskInput input;
        private readonly System.Collections.Generic.HashSet<int> portals, rooms, cells;
        private readonly bool unrestricted;
        internal bool Unrestricted => unrestricted;
        internal TacticalMovementPermission(TacticalMovementMaskInput input)
        {
            this.input = input;
            unrestricted = !input.Reactive && !input.ExteriorOnly && input.ExcludedRoom <= 0
                && !input.SelectedOpeningOnly && !input.RestrictRooms && !input.RestrictPortals
                && !input.RestrictCells && !input.Fight;
            if (input.RestrictRooms || input.RestrictPortals) portals = new System.Collections.Generic.HashSet<int>(input.AllowedPortals);
            if (input.RestrictRooms) rooms = new System.Collections.Generic.HashSet<int>(input.AllowedRooms);
            if (input.RestrictCells) cells = new System.Collections.Generic.HashSet<int>(input.AllowedCells);
        }
        internal bool Allows(int i)
        {
            if (i < 0 || i >= input.Width * input.Height) return false;
            if (unrestricted) return true;
            TacticalRawCell raw = input.Structure?.Input.Cells[i] ?? default(TacticalRawCell);
            bool excluded = input.Reactive ? raw.Room != input.InitialRoom
                : input.Structure != null && (input.ExteriorOnly && raw.Room > 0 && raw.Room != input.InitialRoom
                    || input.ExcludedRoom > 0 && raw.Room == input.ExcludedRoom
                    || input.SelectedOpeningOnly && i != input.BreachIndex
                        && (raw.Has(TacticalRawFlags.WallLine) || input.Structure.Cells[i].ExteriorAccess));
            bool escapeRoom = input.RestrictRooms && !rooms.Contains(input.InitialRoom) && raw.Room == input.InitialRoom;
            excluded |= input.RestrictRooms && raw.Room != input.InitialRoom && !rooms.Contains(raw.Room)
                && !portals.Contains(i);
            excluded |= input.RestrictPortals && raw.Has(TacticalRawFlags.Door) && !portals.Contains(i) && !escapeRoom;
            excluded |= input.RestrictCells && !cells.Contains(i);
            if ((input.SelectedOpeningOnly || input.RestrictCells && !input.RestrictPortals)
                && i == input.BreachIndex) excluded = false;
            if (input.Fight)
            {
                int x = i % input.Width, z = i / input.Width;
                excluded |= DistanceSquared(x, z, input.FightX, input.FightZ) > input.FightRadius * input.FightRadius
                    || input.LeashRadius > 0 && DistanceSquared(x, z, input.LeashX, input.LeashZ) > input.LeashRadius * input.LeashRadius
                    || input.FightRoom > 0 && (input.Structure == null || raw.Room != input.FightRoom);
            }
            return !excluded;
        }
        private static int DistanceSquared(int x1, int z1, int x2, int z2) =>
            (x1 - x2) * (x1 - x2) + (z1 - z2) * (z1 - z2);
    }

    internal static class TacticalMovementMask
    {
        public static ushort[] Calculate(TacticalMovementMaskInput input, CancellationToken cancellation)
        {
            var costs = new ushort[checked(input.Width * input.Height)];
            var permission = new TacticalMovementPermission(input);
            if (permission.Unrestricted) { cancellation.ThrowIfCancellationRequested(); return costs; }
            for (int i = 0; i < costs.Length; i++)
            {
                if ((i & 255) == 0) cancellation.ThrowIfCancellationRequested();
                if (!permission.Allows(i)) costs[i] = ushort.MaxValue;
            }
            cancellation.ThrowIfCancellationRequested();
            return costs;
        }
    }
}
