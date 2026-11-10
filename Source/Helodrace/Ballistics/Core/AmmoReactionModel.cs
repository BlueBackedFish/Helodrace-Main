using System;

namespace Helodrace.Ballistics
{
    public enum AmmoExposure
    {
        None,
        /// <summary>Ordinary fragments or spall into the stowage (mechanical damage, hot fragments).</summary>
        Fragments,
        /// <summary>A residual KE penetrator through the stowage.</summary>
        Penetrator,
        /// <summary>A shaped-charge jet through the stowage: the strongest igniter.</summary>
        Jet,
        /// <summary>Incendiary fragments or a fire reaching the stowage.</summary>
        Incendiary,
        /// <summary>Interior blast load on the stowage.</summary>
        Blast
    }

    public enum AmmoReactionStage
    {
        Intact,
        /// <summary>Propellant ignited; burning starts after the ignition delay.</summary>
        Ignited,
        Burning,
        Detonated,
        Spent
    }

    /// <summary>One stowage location (design doc 7.4). Static data; every value a game value [game].</summary>
    public sealed class AmmoStowage
    {
        public string Id = string.Empty;
        public float PropellantKg;
        /// <summary>High explosive in the stowed rounds (HE fillers); 0 for inert or KE-only stowage.</summary>
        public float ExplosiveKg;
        /// <summary>Isolated magazine behind a door, venting through blow-out panels.</summary>
        public bool Isolated;
        /// <summary>Insensitive munitions or combustible-case-free rounds: lower ignition odds.</summary>
        public bool Insensitive;
        public float MagazineVolumeM3 = 1f;
        public float BlowOutAreaM2;
    }

    /// <summary>
    /// Ammunition reaction as a consequence of damage to a stowage (design doc 7.4), never a fixed per-hit roll.
    /// Ignition odds, stage times and spread are game values [game]: quantitative vulnerability data is not public.
    /// The gas a burning stowage releases uses the compartment pressure model of phase 9.
    /// </summary>
    public static class AmmoReactionModel
    {
        /// <summary>Propellant energy relative to TNT for the gas-pressure model. [game]</summary>
        public const float PropellantTntEquivalence = 0.4f;
        public const int IgnitionDelayTicks = 45;
        public const int BurnTicks = 600;
        /// <summary>A burning stowage reaches another one after this long, with the odds below. [game]</summary>
        public const int SpreadTicks = 240;
        public const float SpreadChance = 0.6f;
        /// <summary>HE fillers in a burning stowage detonate after this long, with these odds per check. [game]</summary>
        public const int DetonationCheckTicks = 300;
        public const float DetonationChance = 0.35f;
        /// <summary>Share of an isolated magazine's gas that reaches the crew past a closed door. [game]</summary>
        public const float IsolatedLeakShare = 0.01f;

        public static float IgnitionChance(AmmoExposure exposure, int fragments, bool insensitive)
        {
            float p;
            switch (exposure)
            {
                case AmmoExposure.Jet: p = 0.85f; break;
                case AmmoExposure.Incendiary: p = 0.6f; break;
                case AmmoExposure.Penetrator: p = 0.45f; break;
                case AmmoExposure.Blast: p = 0.25f; break;
                case AmmoExposure.Fragments: p = 1f - MathF.Pow(1f - 0.01f, Math.Max(0, fragments)); break;
                default: return 0f;
            }
            return insensitive ? p * 0.3f : p;
        }

        /// <summary>Gas load share the crew compartment takes from a burning stowage.</summary>
        public static float CrewGasShare(AmmoStowage stowage, bool doorOpen)
        {
            if (!stowage.Isolated || doorOpen) return 1f;
            // Blow-out panels vent the magazine; only leakage past the door reaches the crew.
            return IsolatedLeakShare;
        }

        /// <summary>Quasi-static gas pressure the stowage's propellant adds to a volume, kPa. [physical approximation x game equivalence]</summary>
        public static float BurnPressureKPa(AmmoStowage stowage, float volumeM3) =>
            BlastModel.QuasiStaticPressureKPa(stowage.PropellantKg * PropellantTntEquivalence, volumeM3);
    }
}
