using System;

namespace Helodrace
{
    public static class RaidSmokePolicy
    {
        public const int GrenadesPerCarrier = 3;
        public static int CarrierCount(int personnel) => Math.Max(0, (personnel + 2) / 3);
    }
}
