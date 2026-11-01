using Waylith.Math;

namespace Helodrace
{
    internal static class UiMath
    {
        // Preserve the rectangle's arithmetic order even for negative extents.
        internal static Rect Inset(Rect value, float margin)
            => new Rect(value.x + margin, value.y + margin,
                value.width - margin * 2f, value.height - margin * 2f);
    }
}
