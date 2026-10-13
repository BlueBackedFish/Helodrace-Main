using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.Tactics
{
    public sealed partial class TacticalMemberCommand
    {
        public int FieldShotAt = -1, FieldObservationAt;
    }
    public sealed partial class MapComponent_TacticalCommands
    {
        internal void NativeFieldShot(Pawn pawn, Verb verb)
        {
            // Notify_UsedVerb runs after a successful native shot, not merely
            // after warmup admission. Keep CQB/custom shot bookkeeping separate.
            if (verb?.EquipmentSource == null || verb.IsMeleeAttack || pawn.CurJobDef != JobDefOf.Wait_Combat
                || !byPawn.TryGetValue(pawn, out var command) || command.FieldResponse == null) return;
            var member = command.Members.Find(m => m.Pawn == pawn);
            if (member?.Job != pawn.CurJob) return;
            int tick = GenTicks.TicksGame;
            member.FieldShotAt = tick; command.FieldResponse.LastEngaged = tick; FieldShots++;
            if (tick < member.FieldObservationAt || !(verb.CurrentTarget.Thing is Pawn enemy)
                || enemy.Dead || enemy.Downed || !TacticalThreatEligibility.Ranged(enemy)) return;
            member.FieldObservationAt = tick + 90;
            // The successful firing code already established its shooting LOS.
            // Do not repeat range/LOS or find an enemy through a map-wide scan.
            RecordContact(command, enemy.thingIDNumber, enemy.Position, pawn.Position, tick);
            var field = command.FieldResponse;
            if (field.EnemyId != enemy.thingIDNumber && tick - field.LastSeen >= TacticalContactMemory.FreshTicks)
            { field.EnemyId = enemy.thingIDNumber; field.Focus = enemy.Position; field.FocusAt = tick; }
            if (field.EnemyId == enemy.thingIDNumber) field.LastSeen = tick;
        }
    }
    [NewTactical, HarmonyPatch(typeof(Pawn), nameof(Pawn.Notify_UsedVerb))]
    public static class Patch_NewTactical_NativeFieldShot
    {
        public static void Postfix(Pawn __instance, Verb verb) => __instance.Map?
            .GetComponent<MapComponent_TacticalCommands>()?.NativeFieldShot(__instance, verb);
    }
}
