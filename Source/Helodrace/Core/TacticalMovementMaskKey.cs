using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Helodrace
{
    internal sealed class TacticalMaskVector : IEquatable<TacticalMaskVector>
    {
        internal static readonly TacticalMaskVector Empty = new TacticalMaskVector(Array.Empty<int>());
        internal readonly int[] Items;
        private readonly int hash;
        internal TacticalMaskVector(IEnumerable<int> items)
        {
            Items = items.Distinct().OrderBy(value => value).ToArray();
            unchecked { int value = 17; foreach (int item in Items) value = value * 31 + item; hash = value; }
        }
        public bool Equals(TacticalMaskVector other) => ReferenceEquals(this, other)
            || other != null && hash == other.hash && Items.Length == other.Items.Length && Items.SequenceEqual(other.Items);
        public override bool Equals(object other) => Equals(other as TacticalMaskVector);
        public override int GetHashCode() => hash;
    }

    // Effective permissions, never plan/unit identity. Input is captured once
    // and owned by this key; workers/cache entries cannot observe later edits.
    internal sealed class TacticalMovementMaskKey : IEquatable<TacticalMovementMaskKey>
    {
        internal readonly TacticalMovementMaskInput Input;
        private readonly TacticalMaskVector rooms, portals, cells;
        private readonly int hash;
        internal TacticalMovementMaskKey(TacticalMovementMaskInput source, TacticalMaskVector roomVector = null,
            TacticalMaskVector portalVector = null, TacticalMaskVector cellVector = null)
        {
            rooms = source.RestrictRooms ? roomVector ?? new TacticalMaskVector(source.AllowedRooms) : TacticalMaskVector.Empty;
            portals = source.RestrictRooms || source.RestrictPortals
                ? portalVector ?? new TacticalMaskVector(source.AllowedPortals) : TacticalMaskVector.Empty;
            cells = source.RestrictCells ? cellVector ?? new TacticalMaskVector(source.AllowedCells) : TacticalMaskVector.Empty;
            bool roomDependent = source.Reactive || source.ExteriorOnly || source.RestrictRooms;
            bool structureDependent = roomDependent || source.ExcludedRoom > 0 || source.SelectedOpeningOnly
                || source.RestrictPortals || source.Fight && source.FightRoom > 0;
            Input = new TacticalMovementMaskInput {
                Width = source.Width, Height = source.Height, Structure = structureDependent ? source.Structure : null,
                Reactive = source.Reactive, ExteriorOnly = source.ExteriorOnly,
                InitialRoom = roomDependent ? source.InitialRoom : 0,
                ExcludedRoom = source.ExcludedRoom > 0 ? source.ExcludedRoom : 0,
                SelectedOpeningOnly = source.SelectedOpeningOnly,
                BreachIndex = source.SelectedOpeningOnly || source.RestrictCells && !source.RestrictPortals ? source.BreachIndex : -1,
                RestrictRooms = source.RestrictRooms, AllowedRooms = rooms.Items,
                RestrictPortals = source.RestrictPortals, AllowedPortals = portals.Items,
                RestrictCells = source.RestrictCells, AllowedCells = cells.Items,
                Fight = source.Fight, FightX = source.Fight ? source.FightX : 0, FightZ = source.Fight ? source.FightZ : 0,
                FightRadius = source.Fight ? source.FightRadius : 0, FightRoom = source.Fight ? source.FightRoom : 0,
                LeashRadius = source.Fight && source.LeashRadius > 0 ? source.LeashRadius : 0,
                LeashX = source.Fight && source.LeashRadius > 0 ? source.LeashX : 0,
                LeashZ = source.Fight && source.LeashRadius > 0 ? source.LeashZ : 0
            };
            unchecked
            {
                int value = Input.Structure == null ? 0 : RuntimeHelpers.GetHashCode(Input.Structure);
                void Add(int item) { value = value * 397 ^ item; }
                Add(Input.Width); Add(Input.Height); Add(Input.Reactive ? 1 : 0); Add(Input.ExteriorOnly ? 1 : 0);
                Add(Input.InitialRoom); Add(Input.ExcludedRoom); Add(Input.SelectedOpeningOnly ? 1 : 0); Add(Input.BreachIndex);
                Add(Input.RestrictRooms ? 1 : 0); Add(Input.RestrictPortals ? 1 : 0); Add(Input.RestrictCells ? 1 : 0);
                Add(Input.Fight ? 1 : 0); Add(Input.FightX); Add(Input.FightZ); Add(Input.FightRadius.GetHashCode()); Add(Input.FightRoom);
                Add(Input.LeashX); Add(Input.LeashZ); Add(Input.LeashRadius.GetHashCode());
                Add(rooms.GetHashCode()); Add(portals.GetHashCode()); Add(cells.GetHashCode()); hash = value;
            }
        }
        public override int GetHashCode() => hash;
        public override bool Equals(object other) => Equals(other as TacticalMovementMaskKey);
        public bool Equals(TacticalMovementMaskKey other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other == null || hash != other.hash) return false;
            TacticalMovementMaskInput a = Input, b = other.Input;
            return a.Structure == b.Structure && a.Width == b.Width && a.Height == b.Height
                && a.Reactive == b.Reactive && a.ExteriorOnly == b.ExteriorOnly && a.InitialRoom == b.InitialRoom
                && a.ExcludedRoom == b.ExcludedRoom && a.SelectedOpeningOnly == b.SelectedOpeningOnly && a.BreachIndex == b.BreachIndex
                && a.RestrictRooms == b.RestrictRooms && a.RestrictPortals == b.RestrictPortals && a.RestrictCells == b.RestrictCells
                && a.Fight == b.Fight && a.FightX == b.FightX && a.FightZ == b.FightZ && a.FightRadius.Equals(b.FightRadius)
                && a.FightRoom == b.FightRoom && a.LeashX == b.LeashX && a.LeashZ == b.LeashZ && a.LeashRadius.Equals(b.LeashRadius)
                && rooms.Equals(other.rooms) && portals.Equals(other.portals) && cells.Equals(other.cells);
        }
    }
}
