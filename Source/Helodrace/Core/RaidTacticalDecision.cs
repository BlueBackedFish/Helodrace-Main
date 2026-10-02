using System;
using System.Collections.Generic;
using System.Linq;

namespace Helodrace
{
    public enum RaidTacticalManeuver
    {
        DirectAssault,
        FlankAttack,
        SmokeAdvance,
        CoordinatedEntry,
        HoldAndCounterattack,
        CautiousAdvance,
        Regroup
    }

    public enum RaidTacticalDoctrine
    {
        Low,
        High
    }

    public sealed class RaidTacticalSituation
    {
        public RaidTacticalDoctrine Doctrine;
        public bool Defending;
        public bool FlankAvailable;
        public bool EntryAvailable;
        public bool BreachToolAvailable;
        public bool SmokeAvailable;
        public bool LethalGrenadeAvailable;
        public bool NonlethalGrenadeAvailable;
        public bool FriendlyInsideObjective;
        public bool IndoorObjective;
        public float DirectThreat;
        public float FlankThreat;
        public float CasualtyFraction;
        public float CommandEfficiency = 1f;
    }

    public sealed class RaidTacticalOption
    {
        public RaidTacticalManeuver Maneuver;
        public float Score;
        public string Reason;
    }

    public static class RaidTacticalDecision
    {
        public static List<RaidTacticalOption> Rank(RaidTacticalSituation situation)
        {
            if (situation == null) throw new ArgumentNullException(nameof(situation));
            float direct = Math.Max(0f, situation.DirectThreat);
            float flank = Math.Max(0f, situation.FlankThreat);
            float casualties = Math.Max(0f, Math.Min(1f, situation.CasualtyFraction));
            var options = new List<RaidTacticalOption>();

            if (situation.Defending)
            {
                options.Add(new RaidTacticalOption
                {
                    Maneuver = RaidTacticalManeuver.HoldAndCounterattack,
                    Score = 76f + direct * 0.15f - casualties * 12f,
                    Reason = "Hold the front and keep a response group in reserve."
                });
            }

            options.Add(new RaidTacticalOption
            {
                Maneuver = RaidTacticalManeuver.DirectAssault,
                Score = 64f - direct * 0.50f - casualties * 28f,
                Reason = "Advance together along the shortest approach."
            });
            options.Add(new RaidTacticalOption
            {
                Maneuver = RaidTacticalManeuver.CautiousAdvance,
                Score = 52f - direct * 0.28f - casualties * 8f,
                Reason = "Keep the group together and probe the approach before committing."
            });
            options.Add(new RaidTacticalOption
            {
                Maneuver = RaidTacticalManeuver.Regroup,
                Score = 36f + casualties * 35f + direct * 0.10f,
                Reason = "Re-form the group, cover casualties and reassess the front."
            });
            if (situation.FlankAvailable)
            {
                options.Add(new RaidTacticalOption
                {
                    Maneuver = RaidTacticalManeuver.FlankAttack,
                    Score = 58f + (direct - flank) * 0.65f - flank * 0.15f - casualties * 16f,
                    Reason = "Bypass the stronger firing axis and strike from the flank."
                });
            }
            if (situation.SmokeAvailable)
            {
                options.Add(new RaidTacticalOption
                {
                    Maneuver = RaidTacticalManeuver.SmokeAdvance,
                    Score = 55f + direct * 0.30f - casualties * 15f,
                    Reason = "Screen the approach before the whole group advances."
                });
            }
            if (situation.EntryAvailable && situation.IndoorObjective)
            {
                bool safeThrow = !situation.FriendlyInsideObjective;
                float support = situation.Doctrine == RaidTacticalDoctrine.High
                    ? (situation.NonlethalGrenadeAvailable ? 10f : 0f)
                    : (safeThrow && situation.LethalGrenadeAvailable ? 10f : 0f);
                options.Add(new RaidTacticalOption
                {
                    Maneuver = RaidTacticalManeuver.CoordinatedEntry,
                    Score = 59f + direct * 0.22f + support
                        + (situation.BreachToolAvailable ? 8f : 0f) - casualties * 19f,
                    Reason = situation.Doctrine == RaidTacticalDoctrine.High
                        ? "Stage outside the fatal funnel, inspect the entry and clear together."
                        : safeThrow && situation.LethalGrenadeAvailable
                            ? "Stage outside the blast radius, throw and enter together."
                            : "Stage outside the entry and clear together without lethal grenades."
                });
            }

            return options.OrderByDescending(option => option.Score)
                .ThenBy(option => option.Maneuver).Take(3).ToList();
        }

        public static RaidTacticalOption Select(IReadOnlyList<RaidTacticalOption> ranked, float commandEfficiency)
        {
            if (ranked == null || ranked.Count == 0) return null;
            int index = commandEfficiency < 0.5f ? 2 : commandEfficiency < 0.8f ? 1 : 0;
            return ranked[Math.Min(index, ranked.Count - 1)];
        }

        public static string EntrySupport(RaidTacticalManeuver maneuver,
            RaidTacticalSituation situation)
        {
            if (maneuver == RaidTacticalManeuver.SmokeAdvance && situation.SmokeAvailable)
                return "Smoke grenade before crossing the front";
            if (maneuver != RaidTacticalManeuver.CoordinatedEntry) return "None";
            if (situation.Doctrine == RaidTacticalDoctrine.High)
                return situation.NonlethalGrenadeAvailable ? "Nonlethal grenade after identification"
                    : "Hold grenades until occupants are identified";
            if (situation.FriendlyInsideObjective) return "No lethal grenade: friendly inside";
            return situation.LethalGrenadeAvailable ? "Lethal grenade, then coordinated entry"
                : "Coordinated entry without grenade";
        }
    }
}
