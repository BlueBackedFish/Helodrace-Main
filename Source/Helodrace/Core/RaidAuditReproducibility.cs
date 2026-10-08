using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Helodrace
{
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    internal static class Patch_TacticalAudit_PawnRequest
    {
        internal static void Prefix(ref PawnGenerationRequest request)
        {
            if (!MapComponent_TacticalEngineAudit.GeneratingFixture) return;
            // The benchmark is a controlled population, not a test of random
            // biography/trait generation or world-pawn name/relation pools.
            int index = MapComponent_TacticalEngineAudit.FixturePawnIndex++;
            request.CanGeneratePawnRelations = false;
            request.FixedBiologicalAge = 18 + index % 18;
            request.FixedChronologicalAge = request.FixedBiologicalAge;
            request.FixedGender = Gender.Female;
            request.ForceNoBackstory = true;
            request.ForceNoIdeo = true; request.FixedIdeo = null;
            var xenotypes = HelodRace.HelodXenotypes.OrderBy(def => def.defName, StringComparer.Ordinal).ToArray();
            request.ForcedXenotype = xenotypes[index % xenotypes.Length];
        }
    }
    [HarmonyPatch(typeof(PawnGenerator), "GenerateTraits")]
    internal static class Patch_TacticalAudit_NoRandomTraits
    {
        internal static bool Prefix() => !MapComponent_TacticalEngineAudit.GeneratingFixture;
    }
    [HarmonyPatch(typeof(PawnBioAndNameGenerator), nameof(PawnBioAndNameGenerator.GiveAppropriateBioAndNameTo))]
    internal static class Patch_TacticalAudit_FixedBio
    {
        internal static bool Prefix(Pawn pawn)
        {
            if (!MapComponent_TacticalEngineAudit.GeneratingFixture) return true;
            pawn.Name = new NameTriple("Audit", "Audit", pawn.thingIDNumber.ToString());
            return false;
        }
    }
    internal static class RaidAuditSeed
    {
        internal static bool Enter()
        {
            if (!GenCommandLine.TryGetCommandLineArg("hdTacticalEngineAudit", out _)
                || !GenCommandLine.TryGetCommandLineArg("hdTacticalAuditSeed", out string seed)) return false;
            uint hash = 2166136261;
            foreach (char value in seed) hash = unchecked((hash ^ value) * 16777619);
            Rand.PushState(unchecked((int)hash)); return true;
        }
        internal static Exception Leave(Exception error, bool entered) { if (entered) Rand.PopState(); return error; }
        internal static string Fingerprint(Map map)
        {
            var text = new StringBuilder().Append(map.Size);
            foreach (IntVec3 cell in map.AllCells)
                text.Append('|').Append(map.terrainGrid.TerrainAt(cell).defName).Append(':')
                    .Append(cell.GetEdifice(map)?.def.defName).Append(':').Append(map.roofGrid.RoofAt(cell)?.defName);
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "");
        }
    }
    [HarmonyPatch(typeof(Root_Play), nameof(Root_Play.SetupForQuickTestPlay))]
    internal static class Patch_RaidAudit_QuickSeed
    {
        private static void Prefix(out bool __state) => __state = RaidAuditSeed.Enter();
        private static Exception Finalizer(Exception __exception, bool __state) => RaidAuditSeed.Leave(__exception, __state);
    }
    [HarmonyPatch(typeof(MapGenerator), nameof(MapGenerator.GenerateMap))]
    internal static class Patch_RaidAudit_MapSeed
    {
        private static void Prefix(out bool __state) => __state = RaidAuditSeed.Enter();
        private static Exception Finalizer(Exception __exception, bool __state) => RaidAuditSeed.Leave(__exception, __state);
    }
    [HarmonyPatch(typeof(WorldGenerator), nameof(WorldGenerator.GenerateWorld))]
    internal static class Patch_RaidAudit_WorldSeed
    {
        private static void Prefix(ref string seedString)
        {
            if (GenCommandLine.TryGetCommandLineArg("hdTacticalEngineAudit", out _)
                && GenCommandLine.TryGetCommandLineArg("hdTacticalAuditSeed", out string seed)) seedString = seed;
        }
    }
}
