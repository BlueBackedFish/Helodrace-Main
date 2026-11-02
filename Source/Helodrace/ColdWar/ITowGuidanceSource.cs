using System.Numerics;

namespace Helodrace
{
    // Moving platforms supply their own launcher endpoint without a Vehicle Framework dependency here.
    public interface ITowGuidanceSource
    {
        bool CanGuideTow { get; }
        Vector3 TowWireOrigin { get; }
    }
}
