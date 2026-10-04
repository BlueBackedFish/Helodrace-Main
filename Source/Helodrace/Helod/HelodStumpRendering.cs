using Verse;

namespace Helodrace
{
    // Keep Helod's stump independent of HAR's PawnRenderNode_Stump patch.
    // That patch assumes its thread-local alien render data has been initialized.
    public sealed class PawnRenderNode_HelodStump : PawnRenderNode
    {
        public PawnRenderNode_HelodStump(Pawn pawn, PawnRenderNodeProperties props,
            PawnRenderTree tree) : base(pawn, props, tree) { }

        public override Graphic GraphicFor(Pawn pawn)
        {
            return pawn.health.hediffSet.HasHead ? null : base.GraphicFor(pawn);
        }
    }
}
