using Verse;
using System.Collections.Generic;

namespace Helodrace
{
    public class BackstoryPersonalityDirectionExtension : DefModExtension
    {
        public float Assertive;
        public float Passive;
        public float Cooperative;
        public float Independent;
        public float PTSD;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors())
            {
                yield return error;
            }

            if (Assertive < 0f || Assertive > 1f) yield return "Assertive must be between 0 and 1.";
            if (Passive < 0f || Passive > 1f) yield return "Passive must be between 0 and 1.";
            if (Cooperative < 0f || Cooperative > 1f) yield return "Cooperative must be between 0 and 1.";
            if (Independent < 0f || Independent > 1f) yield return "Independent must be between 0 and 1.";
            if (PTSD < 0f || PTSD > 1f) yield return "PTSD must be between 0 and 1.";
        }
    }
}
