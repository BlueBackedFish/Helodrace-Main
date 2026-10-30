using System.Numerics;
using Verse;

namespace Helodrace
{
    internal static class NativeVectorScribe
    {
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
