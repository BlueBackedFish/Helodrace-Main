using System;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;
using RimWorld.Planet;
using Verse;

namespace Helodrace
{
    [HarmonyPatch(typeof(PawnGenerator), nameof(PawnGenerator.GeneratePawn), new[] { typeof(PawnGenerationRequest) })]
    internal static class Patch_TacticalAudit_NoRelations
    {
        internal static void Prefix(ref PawnGenerationRequest request)
        {
            // World-pawn enumeration can change which sampled relation succeeds,
            // consuming a different amount of RNG despite the same generation seed.
            // Only the synchronous benchmark formation generation is constrained.
            if (MapComponent_TacticalEngineAudit.GeneratingFixture)
                request.CanGeneratePawnRelations = false;
        }
    }
    internal static class RaidAuditSeed
    {
        internal static bool Enter()
        {
            if ((!GenCommandLine.TryGetCommandLineArg("hdRaidMovementAudit", out _)
                && !GenCommandLine.TryGetCommandLineArg("hdTacticalEngineAudit", out _))
                || !GenCommandLine.TryGetCommandLineArg("hdRaidMovementAuditSeed", out string seed)) return false;
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
            if ((GenCommandLine.TryGetCommandLineArg("hdRaidMovementAudit", out _)
                || GenCommandLine.TryGetCommandLineArg("hdTacticalEngineAudit", out _))
                && GenCommandLine.TryGetCommandLineArg("hdRaidMovementAuditSeed", out string seed)) seedString = seed;
        }
    }
}
