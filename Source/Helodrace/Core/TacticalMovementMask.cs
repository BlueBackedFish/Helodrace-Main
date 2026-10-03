using System;
using System.Threading;

namespace Helodrace
{
    internal struct TacticalMaskRoot
    {
        public int X;
        public int Z;
        public int Radius;
    }

    internal sealed class TacticalMovementMaskInput
    {
        public TacticalGeometryResult Structure;
        public int Width;
        public int Height;
        public TacticalMaskRoot[] Roots = Array.Empty<TacticalMaskRoot>();
        public bool Reactive;
        public bool ExteriorOnly;
        public int InitialRoom;
        public int ExcludedRoom;
        public bool SelectedOpeningOnly;
        public int BreachIndex = -1;
        public bool Fight;
        public int FightX;
        public int FightZ;
        public float FightRadius;
        public int FightRoom;
        public int LeashX;
        public int LeashZ;
        public float LeashRadius;
    }

    internal static class TacticalMovementMask
    {
        public static ushort[] Calculate(TacticalMovementMaskInput input, CancellationToken cancellation)
        {
            var costs = new ushort[checked(input.Width * input.Height)];
            if (!input.Reactive)
            {
                for (int i = 0; i < costs.Length; i++)
                {
                    if ((i & 255) == 0) cancellation.ThrowIfCancellationRequested();
                    costs[i] = 100;
                }
                foreach (TacticalMaskRoot root in input.Roots)
                {
                    cancellation.ThrowIfCancellationRequested();
                    for (int z = Math.Max(0, root.Z - root.Radius); z <= Math.Min(input.Height - 1, root.Z + root.Radius); z++)
                        for (int x = Math.Max(0, root.X - root.Radius); x <= Math.Min(input.Width - 1, root.X + root.Radius); x++)
                            if ((x - root.X) * (x - root.X) + (z - root.Z) * (z - root.Z) <= root.Radius * root.Radius)
                                costs[z * input.Width + x] = 0;
                }
            }
            for (int i = 0; i < costs.Length; i++)
            {
                if ((i & 255) == 0) cancellation.ThrowIfCancellationRequested();
                TacticalRawCell raw = input.Structure?.Input.Cells[i] ?? default(TacticalRawCell);
                bool excluded = input.Reactive ? raw.Room != input.InitialRoom
                    : input.Structure != null && (input.ExteriorOnly && raw.Room > 0 && raw.Room != input.InitialRoom
                        || input.ExcludedRoom > 0 && raw.Room == input.ExcludedRoom
                        || input.SelectedOpeningOnly && i != input.BreachIndex
                            && (raw.Has(TacticalRawFlags.WallLine) || input.Structure.Cells[i].ExteriorAccess));
                if (input.Fight)
                {
                    int x = i % input.Width, z = i / input.Width;
                    excluded |= DistanceSquared(x, z, input.FightX, input.FightZ) > input.FightRadius * input.FightRadius
                        || input.LeashRadius > 0 && DistanceSquared(x, z, input.LeashX, input.LeashZ) > input.LeashRadius * input.LeashRadius
                        || input.FightRoom > 0 && (input.Structure == null || raw.Room != input.FightRoom);
                }
                if (excluded) costs[i] = ushort.MaxValue;
            }
            cancellation.ThrowIfCancellationRequested();
            return costs;
        }
        private static int DistanceSquared(int x1, int z1, int x2, int z2) =>
            (x1 - x2) * (x1 - x2) + (z1 - z2) * (z1 - z2);
    }
}
