using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;
using Verse.AI;

namespace LGModularWeapons
{
    public class WorkGiver_ModifyWeapon : WorkGiver_Scanner
    {
        public override ThingRequest PotentialWorkThingRequest => ThingRequest.ForGroup(ThingRequestGroup.PotentialBillGiver);

        public override PathEndMode PathEndMode => PathEndMode.InteractionCell;

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            List<Thing> benches = pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.PotentialBillGiver);
            for (int i = 0; i < benches.Count; i++)
            {
                CompModularBench bench = benches[i].TryGetComp<CompModularBench>();
                if (bench != null && bench.OrderFor(pawn) != null) return false;
            }
            return true;
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompModularBench bench = t.TryGetComp<CompModularBench>();
            if (bench == null) return false;

            ModOrder order = bench.OrderFor(pawn);
            if (order == null || !order.StillValid) return false;

            if (order.workAmount <= 0f) return false;

            if (t.IsForbidden(pawn) || !pawn.CanReserve(t, 1, -1, null, forced)) return false;
            if (t.IsBurning()) return false;

            return FindIngredients(pawn, order, null);
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            CompModularBench bench = t.TryGetComp<CompModularBench>();
            ModOrder order = bench?.OrderFor(pawn);
            if (order == null) return null;

            List<Thing> found = new List<Thing>();
            List<int> counts = new List<int>();
            if (!FindIngredients(pawn, order, found, counts)) return null;

            Job job = JobMaker.MakeJob(LGMW_JobDefOf.LGMW_ModifyWeapon, t);
            job.targetQueueB = new List<LocalTargetInfo>();
            job.countQueue = new List<int>();

            for (int i = 0; i < found.Count; i++)
            {
                job.targetQueueB.Add(found[i]);
                job.countQueue.Add(counts[i]);
            }
            job.haulMode = HaulMode.ToCellNonStorage;
            return job;
        }

        private bool FindIngredients(Pawn pawn, ModOrder order,
            List<Thing> found, List<int> counts = null)
        {
            if (order.cost.NullOrEmpty()) return true;

            foreach (ThingDefCountClass need in order.cost)
            {
                int remaining = need.count;

                foreach (Thing candidate in pawn.Map.listerThings.ThingsOfDef(need.thingDef))
                {
                    if (remaining <= 0) break;
                    if (candidate.IsForbidden(pawn) || candidate.Position.Fogged(pawn.Map)) continue;
                    if (!pawn.CanReserve(candidate)) continue;
                    if (!pawn.CanReach(candidate, PathEndMode.ClosestTouch, pawn.NormalMaxDanger())) continue;

                    int take = Mathf.Min(remaining, candidate.stackCount);
                    if (found != null)
                    {
                        found.Add(candidate);
                        counts?.Add(take);
                    }
                    remaining -= take;
                }

                if (remaining > 0) return false;
            }
            return true;
        }
    }

    [DefOf]
    public static class LGMW_JobDefOf
    {
        public static JobDef LGMW_ModifyWeapon;

        static LGMW_JobDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(LGMW_JobDefOf));
        }
    }

    public class JobDriver_ModifyWeapon : JobDriver
    {
        private const TargetIndex BenchInd = TargetIndex.A;
        private const TargetIndex IngredientInd = TargetIndex.B;
        private const TargetIndex PlaceCellInd = TargetIndex.C;   // 재료를 내려놓을 셀 (매 트립마다 재계산)

        private float workLeft;

        private CompModularBench Bench => job.GetTarget(BenchInd).Thing?.TryGetComp<CompModularBench>();

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(job.GetTarget(BenchInd), job, 1, -1, null, errorOnFailed))
                return false;

            if (job.targetQueueB != null)
            {
                foreach (LocalTargetInfo t in job.targetQueueB)
                    if (!pawn.Reserve(t, job, 1, -1, null, errorOnFailed)) return false;
            }
            return true;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref workLeft, "workLeft", 0f);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDestroyedOrNull(BenchInd);
            this.FailOnBurningImmobile(BenchInd);
            this.FailOn(() =>
            {
                ModOrder o = Bench?.OrderFor(pawn);
                return o == null || !o.StillValid;
            });

            // ── 재료 운반 ──────────────────────────────────────────────
            // 재료가 없는 주문(무료 교체, 탈착)은 큐가 비어 있으므로 통째로 건너뜀.
            // ExtractNextTargetFromQueue는 빈 큐에서 즉시 잡을 종료시키기 때문에,
            // 그 전에 반드시 NullOrEmpty로 가드해야 한다.
            if (!job.targetQueueB.NullOrEmpty())
            {
                Toil extract = Toils_JobTransforms.ExtractNextTargetFromQueue(IngredientInd);
                yield return extract;

                // ── 재료 위치로 이동 ──
                Toil getToHaulTarget = Toils_Goto.GotoThing(IngredientInd, PathEndMode.ClosestTouch)
                    .FailOnDespawnedNullOrForbidden(IngredientInd);
                yield return getToHaulTarget;

                // subtractNumTakenFromJobCount = true:
                //   이미 들고 있는 스택이 있으면 그 수를 job.count에서 빼서,
                //   꽉 찬 손으로 StartCarryThing에 재진입하는 상황을 방지한다.
                yield return Toils_Haul.StartCarryThing(
                    IngredientInd,
                    putRemainderInQueue: true,
                    subtractNumTakenFromJobCount: true,
                    failIfStackCountLessThanJobCount: false);

                // 근처에 같은 재료 소형 스택이 있으면 한 번에 합쳐서 운반.
                // 점프 대상은 getToHaulTarget(goto)이어야 한다.
                // CheckForGetOpportunityDuplicate(extract, ...) 를 쓰면
                // ExtractNextTargetFromQueue가 방금 지정한 타깃을 덮어쓰므로 사용 금지.
                yield return Toils_Haul.JumpIfAlsoCollectingNextTargetInQueue(getToHaulTarget, IngredientInd);

                // ── 작업대로 이동 ──
                yield return Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell);

                // ── 내려놓을 셀 선택 (매 트립마다 재계산) ──
                // SetTargetToIngredientPlaceCell은 작업대 주변 셀을 순회하며
                // 스택을 받을 수 있는(꽉 차지 않은) 빈 셀을 PlaceCellInd에 설정한다.
                // 작업대가 IBillGiver가 아니어도 destination.Position 기준 반경 셀을 순회하므로 동작한다.
                Toil findPlaceCell = Toils_JobTransforms.SetTargetToIngredientPlaceCell(
                    BenchInd, IngredientInd, PlaceCellInd);
                yield return findPlaceCell;

                // ── 재료 내려놓기 ──
                // BUGFIX: 이전 코드는 PlaceHauledThingInCell(BenchInd, null, false)를 사용했음.
                //   → 대상이 작업대 중심 셀 하나로 고정되어, 첫 스택으로 해당 셀이 가득 차면
                //     이후 드롭이 조용히 실패한다(storageMode=false, nextToil=null → 아무 처리 없음).
                //   → 폰은 여전히 재료를 들고 JumpIfHaveTargetInQueue로 extract에 점프,
                //     StartCarryThing 재진입 시 availableStackSpace=0 → 예외 발생.
                //   → finish 톨에 도달하지 못하므로 RemoveOrder가 호출되지 않아 무한루프 발생.
                //
                // 수정: PlaceCellInd를 대상으로 하고, 드롭 실패 시 findPlaceCell로 되돌아가
                //   다른 셀을 재선택한다.
                yield return Toils_Haul.PlaceHauledThingInCell(PlaceCellInd, findPlaceCell, storageMode: false);

                yield return Toils_Jump.JumpIfHaveTargetInQueue(IngredientInd, extract);
            }

            // 재료가 없는 주문도 여기서 작업대에 도착한다.
            yield return Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell);

            // ── 작업 ──────────────────────────────────────────────────
            Toil work = ToilMaker.MakeToil("ModifyWeapon");
            work.initAction = () =>
            {
                ModOrder o = Bench?.OrderFor(pawn);
                workLeft = o?.workAmount ?? 600f;
            };
            work.tickAction = () =>
            {
                workLeft -= pawn.GetStatValue(StatDefOf.WorkSpeedGlobal);
                pawn.skills?.Learn(SkillDefOf.Crafting, 0.05f);
                if (workLeft <= 0f) ReadyForNextToil();
            };
            work.defaultCompleteMode = ToilCompleteMode.Never;
            work.WithProgressBar(BenchInd, () =>
            {
                ModOrder o = Bench?.OrderFor(pawn);
                float total = o?.workAmount ?? 600f;
                return 1f - workLeft / total;
            });
            work.activeSkill = () => SkillDefOf.Crafting;
            yield return work;

            // ── 적용 ──────────────────────────────────────────────────
            Toil finish = ToilMaker.MakeToil("ApplyModification");
            finish.initAction = () =>
            {
                CompModularBench bench = Bench;
                ModOrder o = bench?.OrderFor(pawn);
                if (o == null || !o.StillValid) return;

                Thing benchThing = job.GetTarget(BenchInd).Thing;
                ConsumeIngredientsAt(benchThing, o.cost);

                ModularResourceUtility.SpawnRefund(o.refund, benchThing);

                CompWeaponModular comp = o.weapon.GetComp<CompWeaponModular>();
                comp?.SetConfiguration(o.config);

                bench.RemoveOrder(o);

                ModularSounds.PlayCompleteAt(job.GetTarget(BenchInd).Thing);
                Messages.Message("LGMW_Applied".Translate(o.weapon.LabelCap),
                    o.weapon, MessageTypeDefOf.PositiveEvent, false);
            };
            finish.defaultCompleteMode = ToilCompleteMode.Instant;
            yield return finish;
        }

        // 작업대 주변 셀에 내려진 재료를 소비한다.
        //
        // 주의: SetTargetToIngredientPlaceCell은 작업대 중심에서 반경 방향으로 셀을 탐색하므로,
        // 작업대가 크거나 IngredientStackCells 범위가 넓으면 OccupiedRect().ExpandedBy(1)보다
        // 더 먼 셀에 재료가 놓일 수 있다.
        // 현재 구현은 ExpandedBy(1) 스캔으로, 1×1 작업대에서는 문제없다.
        // 다칸 작업대를 지원한다면 스캔 반경을 늘리거나,
        // PlaceHauledThingInCell이 실제로 선택한 셀을 잡에 기록하고 그 셀만 소비하는 방식을 쓸 것.
        private void ConsumeIngredientsAt(Thing bench, List<ThingDefCountClass> cost)
        {
            if (bench == null || cost.NullOrEmpty()) return;
            Map map = bench.Map;
            if (map == null) return;

            foreach (ThingDefCountClass need in cost)
            {
                int remaining = need.count;

                foreach (IntVec3 cell in bench.OccupiedRect().ExpandedBy(1))
                {
                    if (remaining <= 0) break;
                    if (!cell.InBounds(map)) continue;

                    List<Thing> things = cell.GetThingList(map);
                    for (int i = things.Count - 1; i >= 0 && remaining > 0; i--)
                    {
                        Thing t = things[i];
                        if (t.def != need.thingDef) continue;

                        int take = Mathf.Min(remaining, t.stackCount);
                        t.SplitOff(take).Destroy();
                        remaining -= take;
                    }
                }
            }
        }
    }
}