using System.Numerics;
using System.Collections.Generic;
using Verse;

namespace Helodrace
{
    internal static class NativeVectorScribe
    {
        internal static void Look(ref List<Vector3> value, string label)
        {
            if (Scribe.mode != LoadSaveMode.Saving && Scribe.mode != LoadSaveMode.LoadingVars) return;
            List<float> components = null;
            if (Scribe.mode == LoadSaveMode.Saving && value != null)
            {
                components = new List<float>(value.Count * 3);
                foreach (Vector3 point in value)
                { components.Add(point.X); components.Add(point.Y); components.Add(point.Z); }
            }
            Scribe_Collections.Look(ref components, label, LookMode.Value);
            if (Scribe.mode != LoadSaveMode.LoadingVars) return;
            if (components == null) { value = null; return; }
            if (components.Count % 3 != 0) throw new System.FormatException("Incomplete native vector components: " + label);
            value = new List<Vector3>(components.Count / 3);
            for (int i = 0; i < components.Count; i += 3)
                value.Add(new Vector3(components[i], components[i + 1], components[i + 2]));
        }

        internal static void Look(ref Vector2 value, string label, Vector2 defaultValue = default)
        {
            if (!Scribe.EnterNode(label))
            {
                if (Scribe.mode == LoadSaveMode.LoadingVars) value = defaultValue;
                return;
            }
            try
            {
                float x = value.X, y = value.Y;
                Scribe_Values.Look(ref x, "x", defaultValue.X, forceSave: true);
                Scribe_Values.Look(ref y, "y", defaultValue.Y, forceSave: true);
                value = new Vector2(x, y);
            }
            finally { Scribe.ExitNode(); }
        }

        internal static void Look(ref Vector3 value, string label)
        {
            if (!Scribe.EnterNode(label))
            {
                if (Scribe.mode == LoadSaveMode.LoadingVars) value = Vector3.Zero;
                return;
            }
            try
            {
                float x = value.X, y = value.Y, z = value.Z;
                Scribe_Values.Look(ref x, "x", forceSave: true);
                Scribe_Values.Look(ref y, "y", forceSave: true);
                Scribe_Values.Look(ref z, "z", forceSave: true);
                value = new Vector3(x, y, z);
            }
            finally { Scribe.ExitNode(); }
        }
    }
}
