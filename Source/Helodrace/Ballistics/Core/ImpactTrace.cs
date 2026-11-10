using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Helodrace.Ballistics
{
    public enum TraceStageStatus
    {
        Ran,
        Skipped,
        NotReached,
        Failed
    }

    public readonly struct TraceValue
    {
        public readonly string Key;
        public readonly double Value;
        public readonly EvidenceGrade Grade;

        public TraceValue(string key, double value, EvidenceGrade grade)
        {
            Key = key;
            Value = value;
            Grade = grade;
        }
    }

    public sealed class TraceStage
    {
        public string StageId;
        public TraceStageStatus Status;
        public string Note;
        public readonly List<TraceValue> Values = new List<TraceValue>();
    }

    /// <summary>
    /// Record of how one impact was resolved: per-stage status, numeric values with their evidence grade, and
    /// notes. Used by the dev overlay and exported as JSON to become regression cases.
    /// </summary>
    public sealed class ImpactTrace
    {
        public ulong Seed;
        public int Tick;
        public int TargetId;
        public string ProjectileDefName = string.Empty;
        public PenetrationMechanism Mechanism;
        public int Depth;
        public ImpactResult Result;
        public readonly List<TraceStage> Stages = new List<TraceStage>(14);
        public readonly List<string> Warnings = new List<string>();
        private TraceStage current;

        public void Begin(in ImpactEvent impact)
        {
            Stages.Clear();
            Warnings.Clear();
            current = null;
            Seed = impact.Seed;
            Tick = impact.Tick;
            TargetId = impact.TargetId;
            ProjectileDefName = impact.ProjectileDefName;
            Mechanism = impact.Mechanism;
            Depth = impact.Depth;
            Result = ImpactResult.None;
        }

        public void Enter(string stageId, TraceStageStatus status)
        {
            current = new TraceStage { StageId = stageId, Status = status };
            Stages.Add(current);
        }

        public void Leave()
        {
            current = null;
        }

        internal void MarkFailed(string message)
        {
            if (current == null) return;
            current.Status = TraceStageStatus.Failed;
            current.Note = message;
        }

        public void Value(string key, double value, EvidenceGrade grade)
        {
            current?.Values.Add(new TraceValue(key, value, grade));
        }

        public void Note(string note)
        {
            if (current == null) return;
            current.Note = string.IsNullOrEmpty(current.Note) ? note : current.Note + "; " + note;
        }

        /// <summary>Out-of-range model inputs, missing empirical sources, clamped values.</summary>
        public void Warn(string warning)
        {
            Warnings.Add(current == null ? warning : current.StageId + ": " + warning);
        }

        public TraceStage Find(string stageId)
        {
            foreach (TraceStage stage in Stages)
            {
                if (stage.StageId == stageId) return stage;
            }
            return null;
        }

        public string ToText()
        {
            var text = new StringBuilder();
            text.Append(ProjectileDefName).Append(" -> ").Append(TargetId.ToString(CultureInfo.InvariantCulture))
                .Append(" [").Append(Mechanism).Append("] seed=").Append(Seed.ToString("X16", CultureInfo.InvariantCulture))
                .Append(" tick=").Append(Tick.ToString(CultureInfo.InvariantCulture))
                .Append(" depth=").Append(Depth.ToString(CultureInfo.InvariantCulture))
                .Append(" result=").Append(Result).AppendLine();
            foreach (TraceStage stage in Stages)
            {
                text.Append("  ").Append(stage.StageId).Append(": ").Append(stage.Status);
                foreach (TraceValue value in stage.Values)
                {
                    text.Append(' ').Append(value.Key).Append('=')
                        .Append(value.Value.ToString("G6", CultureInfo.InvariantCulture))
                        .Append('[').Append(GradeTag(value.Grade)).Append(']');
                }
                if (!string.IsNullOrEmpty(stage.Note)) text.Append(" // ").Append(stage.Note);
                text.AppendLine();
            }
            foreach (string warning in Warnings) text.Append("  ! ").AppendLine(warning);
            return text.ToString();
        }

        public string ToJson()
        {
            var json = new StringBuilder();
            json.Append("{\"seed\":\"").Append(Seed.ToString("X16", CultureInfo.InvariantCulture)).Append('"')
                .Append(",\"tick\":").Append(Tick.ToString(CultureInfo.InvariantCulture))
                .Append(",\"target\":").Append(TargetId.ToString(CultureInfo.InvariantCulture))
                .Append(",\"projectile\":").Append(Quote(ProjectileDefName))
                .Append(",\"mechanism\":").Append(Quote(Mechanism.ToString()))
                .Append(",\"depth\":").Append(Depth.ToString(CultureInfo.InvariantCulture))
                .Append(",\"result\":").Append(Quote(Result.ToString()))
                .Append(",\"stages\":[");
            for (int i = 0; i < Stages.Count; i++)
            {
                TraceStage stage = Stages[i];
                if (i > 0) json.Append(',');
                json.Append("{\"id\":").Append(Quote(stage.StageId))
                    .Append(",\"status\":").Append(Quote(stage.Status.ToString()));
                if (!string.IsNullOrEmpty(stage.Note)) json.Append(",\"note\":").Append(Quote(stage.Note));
                json.Append(",\"values\":[");
                for (int j = 0; j < stage.Values.Count; j++)
                {
                    TraceValue value = stage.Values[j];
                    if (j > 0) json.Append(',');
                    json.Append("{\"key\":").Append(Quote(value.Key))
                        .Append(",\"value\":").Append(Number(value.Value))
                        .Append(",\"grade\":").Append(Quote(GradeTag(value.Grade))).Append('}');
                }
                json.Append("]}");
            }
            json.Append("],\"warnings\":[");
            for (int i = 0; i < Warnings.Count; i++)
            {
                if (i > 0) json.Append(',');
                json.Append(Quote(Warnings[i]));
            }
            return json.Append("]}").ToString();
        }

        public static string GradeTag(EvidenceGrade grade) =>
            grade == EvidenceGrade.Physical ? "physical" : grade == EvidenceGrade.Experimental ? "experimental" : "game";

        private static string Number(double value) =>
            double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : Quote(value.ToString(CultureInfo.InvariantCulture));

        internal static string Quote(string value)
        {
            var quoted = new StringBuilder(value.Length + 2).Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': quoted.Append("\\\""); break;
                    case '\\': quoted.Append("\\\\"); break;
                    case '\n': quoted.Append("\\n"); break;
                    case '\r': quoted.Append("\\r"); break;
                    case '\t': quoted.Append("\\t"); break;
                    default:
                        if (c < 0x20) quoted.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else quoted.Append(c);
                        break;
                }
            }
            return quoted.Append('"').ToString();
        }
    }

    /// <summary>Most recent traces, newest first. Not saved; debugging only.</summary>
    public sealed class ImpactTraceBuffer
    {
        private readonly ImpactTrace[] items;
        private int next;
        private int count;

        public ImpactTraceBuffer(int capacity = 64)
        {
            items = new ImpactTrace[Math.Max(1, capacity)];
        }

        public int Count => count;
        public int Capacity => items.Length;

        public void Add(ImpactTrace trace)
        {
            items[next] = trace;
            next = (next + 1) % items.Length;
            count = Math.Min(count + 1, items.Length);
        }

        /// <summary>0 = newest.</summary>
        public ImpactTrace this[int index]
        {
            get
            {
                if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index));
                return items[(next - 1 - index + items.Length) % items.Length];
            }
        }

        public void Clear()
        {
            Array.Clear(items, 0, items.Length);
            next = 0;
            count = 0;
        }
    }
}
