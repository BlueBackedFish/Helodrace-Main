using System.Collections.Generic;
using Verse;

namespace LGModularWeapons
{
    // A set of parts that, TOGETHER, block something. Any single member being installed is
    // harmless; the rule only fires when every member is present at once.
    //
    // Used by WeaponPartDef.conflictingCombinations - e.g. a long handguard that cannot be
    // fitted when a short barrel and a large gas block are both on the weapon.
    public class PartCombination
    {
        public List<WeaponPartDef> parts;

        // Shown to the player instead of the auto-generated part list, when the default
        // wording is not clear enough.
        public string reasonKey;

        public bool IsEmpty => parts.NullOrEmpty();

        public string Describe()
        {
            if (!reasonKey.NullOrEmpty()) return reasonKey.Translate();

            List<string> labels = new List<string>();
            for (int i = 0; i < parts.Count; i++)
                if (parts[i] != null) labels.Add(parts[i].LabelCap);
            return string.Join(" + ", labels.ToArray());
        }
    }
}
