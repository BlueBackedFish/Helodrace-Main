using RimWorld;
using Verse;

namespace Helodrace
{
    public class HonorGuardEquipmentExtension : DefModExtension
    {
    }

    public class ThoughtWorker_HonorGuardEquipment : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            int count = HonorGuardEquipmentUtility.CountEquippedHonorGuardItems(p);
            if (count <= 0)
            {
                return ThoughtState.Inactive;
            }

            return ThoughtState.ActiveAtStage(System.Math.Min(count, 3) - 1);
        }
    }

    public static class HonorGuardEquipmentUtility
    {
        public static int CountEquippedHonorGuardItems(Pawn pawn)
        {
            if (pawn == null)
            {
                return 0;
            }

            int count = 0;
            if (pawn.apparel?.WornApparel != null)
            {
                for (int i = 0; i < pawn.apparel.WornApparel.Count; i++)
                {
                    if (IsHonorGuardEquipment(pawn.apparel.WornApparel[i]?.def))
                    {
                        count++;
                    }
                }
            }

            if (IsHonorGuardEquipment(pawn.equipment?.Primary?.def))
            {
                count++;
            }

            return count;
        }

        private static bool IsHonorGuardEquipment(ThingDef def)
        {
            return def?.GetModExtension<HonorGuardEquipmentExtension>() != null;
        }
    }
}
