using UnityEngine;
using Verse;

namespace LGModularWeapons
{
    // Graphic class for modular weapons. Set it on the weapon's graphicData:
    //     <graphicClass>LGModularWeapons.Graphic_ModularWeapon</graphicClass>
    //
    // WHY THE OVERLAYS LIVE HERE RATHER THAN IN THE COMP:
    // Printing attachments from ThingComp.PostPrintOnto meant computing their world position
    // independently of the weapon body. That is fine on an open map, but Adaptive Storage
    // Framework prints stored items inside a transform scope - it temporarily rewrites
    // thing.Rotation and wraps the call in graphic.Transformed(...) - so the body moved and
    // rotated while the attachments stayed on the untransformed layout, which is what made
    // them look twisted in a storage unit.
    //
    // Printing from inside Graphic.Print puts the attachments in exactly the same scope as
    // the body: whatever transform, rotation or draw offset the caller applied to this
    // graphic applies to them too, with no knowledge of the caller required.
    public class Graphic_ModularWeapon : Graphic_Single
    {
        public override void Print(SectionLayer layer, Thing thing, float extraRotation)
        {
            CompWeaponModular comp = (thing as ThingWithComps)?.GetComp<CompWeaponModular>();

            // Negative layers first, then the receiver, then the rest - print order is what
            // decides depth in a section mesh.
            if (comp != null) comp.PrintOverlays(layer, thing, extraRotation, this, true);

            // Move the body by the same whole-weapon offset the overlays use. base.Print reads
            // the position from this graphic's DrawOffset, so shift it for the duration of the
            // call and restore it after - the overlays computed their own copy already.
            Vector3 saved = data != null ? data.drawOffset : Vector3.zero;
            bool shifted = false;

            if (comp != null && data != null)
            {
                Vector3 body = comp.GroundBodyShift(thing.Rotation, this);
                if (body != Vector3.zero)
                {
                    data.drawOffset = saved + body;
                    shifted = true;
                }
            }

            base.Print(layer, thing, extraRotation);

            if (shifted) data.drawOffset = saved;

            if (comp != null) comp.PrintOverlays(layer, thing, extraRotation, this, false);
        }

        public override Graphic GetColoredVersion(Shader newShader, Color newColor, Color newColorTwo)
        {
            return GraphicDatabase.Get<Graphic_ModularWeapon>(path, newShader, drawSize,
                newColor, newColorTwo, data);
        }
    }
}