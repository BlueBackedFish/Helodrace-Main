using System;
using System.Collections.Generic;

namespace Helodrace.Ballistics
{
    /// <summary>Standard stage ids (design doc section 8). Mods insert their stages relative to these.</summary>
    public static class ImpactStageIds
    {
        public const string CollisionResolve = "CollisionResolve";
        public const string HitLocation = "HitLocation";
        public const string FrameTransform = "FrameTransform";
        public const string ImpactGeometry = "ImpactGeometry";
        public const string ArmorStackLookup = "ArmorStackLookup";
        public const string ReactiveStage = "ReactiveStage";
        public const string LayerInteraction = "LayerInteraction";
        public const string OutcomeClassify = "OutcomeClassify";
        public const string ResidualUpdate = "ResidualUpdate";
        public const string SecondaryGenerate = "SecondaryGenerate";
        public const string SecondaryPropagate = "SecondaryPropagate";
        public const string TargetDamage = "TargetDamage";
        public const string PermanentDamage = "PermanentDamage";
        public const string FunctionalAndEvents = "FunctionalAndEvents";

        public static readonly string[] Ordered =
        {
            CollisionResolve, HitLocation, FrameTransform, ImpactGeometry, ArmorStackLookup, ReactiveStage,
            LayerInteraction, OutcomeClassify, ResidualUpdate, SecondaryGenerate, SecondaryPropagate,
            TargetDamage, PermanentDamage, FunctionalAndEvents
        };
    }

    public interface IImpactStage
    {
        string Id { get; }
        /// <summary>False skips the stage for this impact (e.g. no reactive layer, HE has no layer interaction).</summary>
        bool Applies(ImpactContext context);
        void Run(ImpactContext context);
    }

    /// <summary>
    /// Mutable working state of one impact. Stages read and write only this object, never game objects, so the
    /// same pipeline runs in the regression executable. Contexts are pooled; <see cref="Reset"/> clears them.
    /// </summary>
    public sealed class ImpactContext
    {
        public ImpactEvent Event { get; private set; }
        public BallisticFrame TargetFrame;
        public ImpactGeometry Geometry;
        public readonly ArmorStack Stack = new ArmorStack();
        public PenetratorState Penetrator;
        public readonly ImpactOutcome Outcome = new ImpactOutcome();
        public readonly ImpactTrace Trace = new ImpactTrace();
        /// <summary>Secondary impacts requested by stages; the caller resolves them after this impact.</summary>
        public readonly List<ImpactEvent> ChildImpacts = new List<ImpactEvent>();
        /// <summary>Adapter-owned target data (humanoid or vehicle model). Stages cast to what they expect.</summary>
        public object Target;
        public bool Halted { get; private set; }
        public string HaltReason { get; private set; }
        public string CurrentStageId { get; internal set; }

        public void Reset(in ImpactEvent impact, BallisticFrame targetFrame, object target)
        {
            Event = impact;
            TargetFrame = targetFrame;
            Target = target;
            Geometry = default;
            Stack.Clear();
            Penetrator = impact.Penetrator;
            Outcome.Clear();
            ChildImpacts.Clear();
            Halted = false;
            HaltReason = null;
            CurrentStageId = null;
            Trace.Begin(impact);
        }

        /// <summary>Random stream owned by the current stage. Independent of other stages and of Verse.Rand.</summary>
        public BallisticRng StageRandom() => StageRandom(CurrentStageId ?? string.Empty);

        public BallisticRng StageRandom(string streamId) => BallisticSeed.Stream(Event.Seed, streamId);

        /// <summary>Stops the remaining stages (early termination when the result is already certain).</summary>
        public void Halt(string reason)
        {
            Halted = true;
            HaltReason = reason;
            Trace.Note("halt: " + reason);
        }

        /// <summary>Queues a secondary impact. Refused beyond the pipeline depth limit.</summary>
        public bool TryQueueChild(in ImpactEvent child, int maxDepth)
        {
            if (child.Depth > maxDepth)
            {
                Trace.Warn("child impact refused at depth " + child.Depth);
                return false;
            }
            ChildImpacts.Add(child);
            return true;
        }
    }

    public sealed class ImpactPipeline
    {
        public const int DefaultMaxDepth = 2;

        private readonly IImpactStage[] stages;
        public readonly int MaxDepth;

        public ImpactPipeline(IEnumerable<IImpactStage> orderedStages, int maxDepth = DefaultMaxDepth)
        {
            var list = new List<IImpactStage>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (IImpactStage stage in orderedStages)
            {
                if (stage == null || string.IsNullOrEmpty(stage.Id))
                    throw new ArgumentException("Impact stage without id");
                if (!ids.Add(stage.Id))
                    throw new ArgumentException("Duplicate impact stage id: " + stage.Id);
                list.Add(stage);
            }
            stages = list.ToArray();
            MaxDepth = Math.Max(0, maxDepth);
        }

        public IReadOnlyList<IImpactStage> Stages => stages;

        public void Run(ImpactContext context)
        {
            if (context.Event.Depth > MaxDepth)
            {
                context.Trace.Warn("impact depth " + context.Event.Depth + " exceeds " + MaxDepth);
                return;
            }
            foreach (IImpactStage stage in stages)
            {
                context.CurrentStageId = stage.Id;
                if (context.Halted)
                {
                    context.Trace.Enter(stage.Id, TraceStageStatus.NotReached);
                    context.Trace.Leave();
                    continue;
                }
                if (!stage.Applies(context))
                {
                    context.Trace.Enter(stage.Id, TraceStageStatus.Skipped);
                    context.Trace.Leave();
                    continue;
                }
                context.Trace.Enter(stage.Id, TraceStageStatus.Ran);
                try
                {
                    stage.Run(context);
                }
                catch (Exception exception)
                {
                    context.Trace.MarkFailed(exception.GetType().Name + ": " + exception.Message);
                    context.Trace.Leave();
                    context.Trace.Result = context.Outcome.Result;
                    context.CurrentStageId = null;
                    throw;
                }
                context.Trace.Leave();
            }
            context.CurrentStageId = null;
            context.Trace.Result = context.Outcome.Result;
        }
    }

    /// <summary>Ordered stage list that other modules (Lobos vehicles, compatibility) extend by id.</summary>
    public sealed class ImpactPipelineBuilder
    {
        private readonly List<IImpactStage> stages = new List<IImpactStage>();

        public ImpactPipelineBuilder Add(IImpactStage stage)
        {
            stages.Add(stage);
            return this;
        }

        public ImpactPipelineBuilder InsertBefore(string anchorId, IImpactStage stage)
        {
            stages.Insert(IndexOf(anchorId), stage);
            return this;
        }

        public ImpactPipelineBuilder InsertAfter(string anchorId, IImpactStage stage)
        {
            stages.Insert(IndexOf(anchorId) + 1, stage);
            return this;
        }

        public ImpactPipelineBuilder Replace(string id, IImpactStage stage)
        {
            stages[IndexOf(id)] = stage;
            return this;
        }

        public bool Contains(string id) => stages.FindIndex(s => s.Id == id) >= 0;

        public ImpactPipeline Build(int maxDepth = ImpactPipeline.DefaultMaxDepth) => new ImpactPipeline(stages, maxDepth);

        private int IndexOf(string id)
        {
            int index = stages.FindIndex(s => s.Id == id);
            if (index < 0) throw new ArgumentException("Unknown impact stage id: " + id);
            return index;
        }
    }

    /// <summary>Reuses contexts to keep impact resolution allocation-free in large fights.</summary>
    public sealed class ImpactContextPool
    {
        private readonly Stack<ImpactContext> free = new Stack<ImpactContext>();

        public ImpactContext Rent() => free.Count > 0 ? free.Pop() : new ImpactContext();

        /// <summary>The trace object is owned by the context; copy it out (or keep the context) before returning.</summary>
        public void Return(ImpactContext context)
        {
            if (context != null) free.Push(context);
        }
    }
}
