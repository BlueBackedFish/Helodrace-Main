namespace Helodrace
{
    // Shortest-arc animation operations absent from the native scalar API.
    // Angles remain unwrapped, matching callers that normalize at their boundary.
    internal static class AngleMath
    {
        internal static float Interpolate(float from, float to, float fraction)
            => from + Mathf.DeltaAngle(from, to) * Mathf.Clamp01(fraction);

        internal static float MoveTowards(float current, float target, float maximumDelta)
        {
            float delta = Mathf.DeltaAngle(current, target);
            if (-maximumDelta < delta && delta < maximumDelta) return target;
            return Mathf.MoveTowards(current, current + delta, maximumDelta);
        }
    }
}
