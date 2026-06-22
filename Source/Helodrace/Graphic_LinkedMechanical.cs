using UnityEngine;
using Verse;

namespace Helodrace
{
    public class Graphic_LinkedMechanical : Graphic_Linked
    {
        public override bool ShouldLinkWith(IntVec3 c, Thing parent)
        {
            if (!c.InBounds(parent.Map))
                return false;
            
            var thingList = c.GetThingList(parent.Map);
            for (int i = 0; i < thingList.Count; i++)
            {
                Thing thing = thingList[i];
                // Link if the other thing has any kind of mechanical node (Transmitter, Emitter, or User)
                if (thing.TryGetComp<CompMechanicalNode>() != null)
                {
                    return true;
                }
            }
            return false;
        }

        public override void Print(SectionLayer layer, Thing thing, float extraRotation)
        {
            // The base Print method handles drawing the correct atlas piece based on ShouldLinkWith
            base.Print(layer, thing, extraRotation);
        }
    }
}