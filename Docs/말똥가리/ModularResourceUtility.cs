using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace LGModularWeapons
{
    // Resource gate + consumption. The customization window aggregates a whole pending
    // configuration into a Dictionary<ThingDef,int> and pays it once, on confirmation.
    //
    // If you get CS1503 ("cannot convert Dictionary<ThingDef,int> to List<ThingDefCountClass>")
    // it means an older copy of this file is still in the project - the Dictionary overloads
    // below are the ones Window_ModularWeapon calls.
    public static class ModularResourceUtility
    {
        public static bool HasResources(Map map, Dictionary<ThingDef, int> cost)
        {
            if (cost == null || cost.Count == 0) return true;
            if (map == null) return false;
            foreach (KeyValuePair<ThingDef, int> kv in cost)
                if (CountAvailable(map, kv.Key) < kv.Value) return false;
            return true;
        }

        public static bool HasResources(Map map, List<ThingDefCountClass> cost)
        {
            if (cost.NullOrEmpty()) return true;
            if (map == null) return false;
            foreach (ThingDefCountClass c in cost)
                if (CountAvailable(map, c.thingDef) < c.count) return false;
            return true;
        }

        public static bool TryConsume(Map map, Dictionary<ThingDef, int> cost)
        {
            if (cost == null || cost.Count == 0) return true;
            if (!HasResources(map, cost)) return false;

            foreach (KeyValuePair<ThingDef, int> kv in cost)
                if (!ConsumeOne(map, kv.Key, kv.Value)) return false;
            return true;
        }

        public static bool TryConsume(Map map, List<ThingDefCountClass> cost)
        {
            if (cost.NullOrEmpty()) return true;
            if (!HasResources(map, cost)) return false;

            foreach (ThingDefCountClass c in cost)
                if (!ConsumeOne(map, c.thingDef, c.count)) return false;
            return true;
        }

        // Counted directly off the thing lister rather than map.resourceCounter.
        //
        // ResourceCounter is a tick-driven cache: while the game is PAUSED it never updates,
        // so the customization window reported stale stock and refused orders the colony
        // could actually afford. It also only counts items in storage, while the pawn can
        // haul anything reachable.
        public static int CountAvailable(Map map, ThingDef def)
        {
            if (map == null || def == null) return 0;

            int total = 0;
            List<Thing> things = map.listerThings.ThingsOfDef(def);
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t.Position.Fogged(map)) continue;
                if (t.IsForbidden(Faction.OfPlayer)) continue;
                total += t.stackCount;
            }
            return total;
        }

        // Drops salvaged materials next to a thing (the bench, or the weapon for the instant
        // path). Uses the normal near-drop so stacks merge and hauling picks them up.
        public static void SpawnRefund(Dictionary<ThingDef, int> refund, Thing near)
        {
            if (refund == null || refund.Count == 0) return;
            if (near == null || !near.Spawned || near.Map == null) return;

            foreach (var kv in refund)
            {
                int remaining = kv.Value;
                while (remaining > 0)
                {
                    Thing stack = ThingMaker.MakeThing(kv.Key);
                    stack.stackCount = Mathf.Min(remaining, kv.Key.stackLimit);
                    remaining -= stack.stackCount;

                    GenPlace.TryPlaceThing(stack, near.Position, near.Map, ThingPlaceMode.Near);
                }
            }
        }

        public static void SpawnRefund(List<ThingDefCountClass> refund, Thing near)
        {
            if (refund.NullOrEmpty()) return;

            var dict = new Dictionary<ThingDef, int>();
            foreach (ThingDefCountClass c in refund)
                dict[c.thingDef] = (dict.TryGetValue(c.thingDef, out var n) ? n : 0) + c.count;

            SpawnRefund(dict, near);
        }

        private static bool ConsumeOne(Map map, ThingDef def, int amount)
        {
            int remaining = amount;
            List<Thing> stacks = map.listerThings.ThingsOfDef(def);
            for (int i = stacks.Count - 1; i >= 0 && remaining > 0; i--)
            {
                Thing t = stacks[i];
                if (t.Position.Fogged(map)) continue;
                int take = Mathf.Min(remaining, t.stackCount);
                t.SplitOff(take).Destroy();
                remaining -= take;
            }
            return remaining <= 0;
        }
    }
}