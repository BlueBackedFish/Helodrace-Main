using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using Verse.Sound;

namespace Helodrace
{
    public enum HelodDogCue { Idle, Growl, Hurt, Whine, Panting, Death }

    [DefOf]
    public static class HelodDogSoundDefOf
    {
        public static SoundDef HD_HelodDogIdle;
        public static SoundDef HD_HelodDogGrowl;
        public static SoundDef HD_HelodDogHurt;
        public static SoundDef HD_HelodDogWhine;
        public static SoundDef HD_HelodDogPanting;
        public static SoundDef HD_HelodDogDeath;

        static HelodDogSoundDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(HelodDogSoundDefOf));
    }

    public static class HelodDogSounds
    {
        public const float Chance = 0.05f;
        public const int AttemptIntervalTicks = 600;
        public const int PawnSoundIntervalTicks = 1800;
        public const int MapSoundIntervalTicks = 180;

        private sealed class Timing
        {
            public Timing() { }
            public int nextAttempt;
            public int nextSound;
        }

        // Cosmetic state follows the lifetime of its pawn/map and is not saved.
        private static readonly ConditionalWeakTable<Pawn, Timing> pawnTiming = new ConditionalWeakTable<Pawn, Timing>();
        private static readonly ConditionalWeakTable<Map, Timing> mapTiming = new ConditionalWeakTable<Map, Timing>();

        public static bool TryPlay(Pawn pawn, HelodDogCue cue, TargetInfo? deathLocation = null)
        {
            if (!HelodRace.IsHelod(pawn) || Find.TickManager == null) return false;
            bool death = cue == HelodDogCue.Death;
            if (death ? !pawn.Dead || !deathLocation.HasValue
                : !pawn.Spawned || pawn.Dead || !pawn.Awake()) return false;
            TargetInfo location = death ? deathLocation.Value : new TargetInfo(pawn.Position, pawn.Map);
            if (location.Map == null || location.Map != Find.CurrentMap || location.Cell.Fogged(location.Map)) return false;
            int now = Find.TickManager.TicksGame;
            Timing individual = pawnTiming.GetOrCreateValue(pawn);
            Timing map = mapTiming.GetOrCreateValue(location.Map);
            if (now < individual.nextAttempt || now < individual.nextSound || now < map.nextSound) return false;
            individual.nextAttempt = now + AttemptIntervalTicks;

            // Easter eggs must not consume the simulation's combat/social random sequence.
            Rand.PushState(Gen.HashCombineInt(Gen.HashCombineInt(pawn.thingIDNumber, now), (int)cue));
            try
            {
                if (!Rand.Chance(Chance)) return false;
                SoundFor(cue).PlayOneShot(SoundInfo.InMap(location));
                individual.nextSound = now + PawnSoundIntervalTicks;
                map.nextSound = now + MapSoundIntervalTicks;
                return true;
            }
            finally { Rand.PopState(); }
        }

        public static SoundDef SoundFor(HelodDogCue cue)
        {
            switch (cue)
            {
                case HelodDogCue.Growl: return HelodDogSoundDefOf.HD_HelodDogGrowl;
                case HelodDogCue.Hurt: return HelodDogSoundDefOf.HD_HelodDogHurt;
                case HelodDogCue.Whine: return HelodDogSoundDefOf.HD_HelodDogWhine;
                case HelodDogCue.Panting: return HelodDogSoundDefOf.HD_HelodDogPanting;
                case HelodDogCue.Death: return HelodDogSoundDefOf.HD_HelodDogDeath;
                default: return HelodDogSoundDefOf.HD_HelodDogIdle;
            }
        }

        internal static HelodDogCue? CueFor(JobDef job)
        {
            switch (job?.defName)
            {
                case "HD_HelodMediate":
                case "SocialRelax":
                case "Ingest":
                case "Wait_Wander":
                case "GotoWander":
                case "Play_Chess":
                case "Play_Billiards":
                    return HelodDogCue.Idle;
                case "Mine":
                case "HaulToCell":
                    return HelodDogCue.Panting;
                case "Vomit":
                    return HelodDogCue.Whine;
                default: return null;
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_JobTracker), nameof(Pawn_JobTracker.StartJob))]
    public static class Patch_HelodDogJobSound
    {
        public static void Postfix(Pawn_JobTracker __instance, Pawn ___pawn, Job newJob)
        {
            if (!HelodRace.IsHelod(___pawn) || newJob == null || __instance.curJob != newJob) return;
            HelodDogCue? cue = HelodDogSounds.CueFor(newJob.def);
            if (cue.HasValue) HelodDogSounds.TryPlay(___pawn, cue.Value);
        }
    }

    [HarmonyPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState))]
    public static class Patch_HelodDogMentalSound
    {
        public static void Postfix(Pawn ___pawn, MentalStateDef stateDef, bool __result)
        {
            if (__result && (stateDef == HelodConfrontationDefOf.HD_HelodConfrontation
                || stateDef == MentalStateDefOf.SocialFighting))
                HelodDogSounds.TryPlay(___pawn, HelodDogCue.Growl);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.PostApplyDamage))]
    public static class Patch_HelodDogHurtSound
    {
        public static void Postfix(Pawn __instance, DamageInfo dinfo, float totalDamageDealt)
        {
            if (totalDamageDealt > 0f && dinfo.Def.ExternalViolenceFor(__instance))
                HelodDogSounds.TryPlay(__instance, HelodDogCue.Hurt);
        }
    }

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Patch_HelodDogDeathSound
    {
        public static void Prefix(Pawn __instance, out TargetInfo? __state)
        {
            __state = HelodRace.IsHelod(__instance) && __instance.Spawned && !__instance.Dead
                ? new TargetInfo? (new TargetInfo(__instance.Position, __instance.Map)) : null;
        }

        public static void Postfix(Pawn __instance, TargetInfo? __state)
        {
            if (__state.HasValue && __instance.Dead)
                HelodDogSounds.TryPlay(__instance, HelodDogCue.Death, __state);
        }
    }
}
