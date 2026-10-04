// Minimal game boundary for running the real planning, command, and persistence code
// without launching Unity. These are not a substitute for an in-game smoke test.
using System;
using System.Collections.Generic;
using System.Linq;

namespace RimWorld
{
    public class Faction { }
    public class SkillDef : Verse.Def { }
    public class Skills
    {
        public int level;
        public SkillRecord GetSkill(SkillDef def) => new SkillRecord { Level = level };
    }
    public class SkillRecord { public int Level; }
}
namespace Helodrace.ModernWar
{
    public class ModularWeaponPresetDef : Verse.Def { }
}

namespace Verse
{
    public class Def
    {
        public string defName;
        public string label;
        public virtual IEnumerable<string> ConfigErrors() { yield break; }
    }
    public class DefModExtension
    {
        public virtual IEnumerable<string> ConfigErrors() { yield break; }
    }
    public class ThingDef : Def { public bool IsApparel; }
    public class PawnKindDef : Def { public float combatPower; }
    public class Thing { public ThingDef def; }
    public class Equipment { public List<Thing> AllEquipmentListForReading = new List<Thing>(); }
    public class Apparel { public List<Thing> WornApparel = new List<Thing>(); }
    public class Inventory
    {
        public List<Thing> contents = new List<Thing>();
        public int Count(ThingDef def) => contents.Count(thing => thing.def == def);
    }
    public class Pawn
    {
        public bool Dead, Downed, Destroyed, InMentalState, IsPrisoner, IsSlave;
        public int thingIDNumber;
        public RimWorld.Faction Faction;
        public RimWorld.Skills skills = new RimWorld.Skills();
        public Equipment equipment = new Equipment();
        public Apparel apparel = new Apparel();
        public Inventory inventory = new Inventory();
    }
    public interface IExposable { void ExposeData(); }
    public enum LookMode { Deep, Def, Reference }
    public enum LoadSaveMode { Inactive, Saving, LoadingVars, PostLoadInit }
    public static class Scribe
    {
        public static LoadSaveMode mode;
        public static Dictionary<string, object> node;
        public static Dictionary<string, object> Save(IExposable value)
        {
            var before = node;
            node = new Dictionary<string, object>();
            value.ExposeData();
            var result = node;
            node = before;
            return result;
        }
        public static void Read(IExposable value, Dictionary<string, object> saved)
        {
            var before = node;
            node = saved;
            value.ExposeData();
            node = before;
        }
        public static T RoundTrip<T>(T value) where T : IExposable, new()
        {
            mode = LoadSaveMode.Saving;
            var snapshot = Save(value);
            var copy = new T();
            mode = LoadSaveMode.LoadingVars;
            Read(copy, snapshot);
            mode = LoadSaveMode.PostLoadInit;
            Read(copy, snapshot);
            mode = LoadSaveMode.Inactive;
            return copy;
        }
    }
    public static class Scribe_Values
    {
        public static void Look<T>(ref T value, string key, T defaultValue = default)
        {
            if (Scribe.mode == LoadSaveMode.Saving) Scribe.node[key] = value;
            if (Scribe.mode == LoadSaveMode.LoadingVars)
                value = Scribe.node.TryGetValue(key, out var saved) ? (T)saved : defaultValue;
        }
    }
    public static class Scribe_References
    {
        public static void Look<T>(ref T value, string key) => Scribe_Values.Look(ref value, key);
    }
    public static class Scribe_Defs
    {
        public static void Look<T>(ref T value, string key) => Scribe_Values.Look(ref value, key);
    }
    public static class Scribe_Collections
    {
        public static void Look<T>(ref List<T> value, string key, LookMode mode)
        {
            if (mode != LookMode.Deep)
            {
                if (Scribe.mode == LoadSaveMode.Saving) Scribe.node[key] = value?.ToList();
                if (Scribe.mode == LoadSaveMode.LoadingVars) value = ((List<T>)Scribe.node[key])?.ToList();
                return;
            }
            if (Scribe.mode == LoadSaveMode.Saving)
                Scribe.node[key] = value.Select(item => Scribe.Save((IExposable)item)).ToList();
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                value = new List<T>();
                foreach (var snapshot in (List<Dictionary<string, object>>)Scribe.node[key])
                {
                    T item = Activator.CreateInstance<T>();
                    Scribe.Read((IExposable)item, snapshot);
                    value.Add(item);
                }
            }
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                var snapshots = (List<Dictionary<string, object>>)Scribe.node[key];
                for (int i = 0; i < value.Count; i++) Scribe.Read((IExposable)value[i], snapshots[i]);
            }
        }
    }
}
