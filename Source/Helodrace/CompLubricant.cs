using Verse;

namespace Helodrace
{
    public class CompProperties_Lubricant : CompProperties
    {
        public float lubeMtbHours = 6f;

        public CompProperties_Lubricant()
        {
            this.compClass = typeof(CompLubricant);
        }
    }

    public class CompLubricant : ThingComp
    {
        public CompProperties_Lubricant Props => (CompProperties_Lubricant)props;
    }
}