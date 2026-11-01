using System;
using HarmonyLib;
using Helodrace;
using UnityEngine;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Verse;
using Verse.AI;

internal static class ShotgunMotionTests
{
    internal static void Run()
    {
        var offset = new Vector3(0f, 0f, 10f);
        foreach (var velocity in new[] { Vector3.Zero, new Vector3(0.1f, 0f, 0f), new Vector3(0f, 0f, 0.1f), new Vector3(0f, 0f, -0.1f) })
        {
            float time = ShotgunBallistics.InterceptTicks(offset, velocity, 1f);
            Program.Check(time > 0f, "Reachable targets must have a positive interception time.");
            Program.Near((offset + velocity * time).Length(), time, "The lead point must meet the projectile at its actual speed.");
        }
        Program.Near(ShotgunBallistics.InterceptTicks(offset, new Vector3(0f, 0f, 2f), 1f), 0f, "An unreachable fleeing target must not produce a negative interception.");
        Program.Near(ShotgunBallistics.InterceptTicks(offset, new Vector3(0f, 0f, -1f), 1f), 5f, "Equal-speed interception must handle the linear case.");
        Program.Near(ShotgunBallistics.InterceptTicks(offset, Vector3.Zero, 0f), 0f, "Zero projectile speed must not produce invalid coordinates.");

        Pawn pawn = Program.Pawn(new BodyDef());
        AccessTools.FieldRefAccess<Thing, IntVec3>("positionInt")(pawn) = new IntVec3(0, 0, 10);
        // No drawer exists in this fixture: reading render coordinates fails.
        Program.Check(ShotgunBallistics.Position(pawn) == new Vector3(0.5f, 0f, 10.5f), "A stationary target must use logical coordinates without creating a drawer.");
        var path = Program.Bare<Pawn_PathFollower>();
        pawn.pather = path;
        AccessTools.FieldRefAccess<Pawn_PathFollower, bool>("moving")(path) = true;
        AccessTools.FieldRefAccess<Pawn_PathFollower, float>("cachedMovePercentage")(path) = 0.5f;
        AccessTools.FieldRefAccess<Pawn_PathFollower, LocalTargetInfo>("destination")(path) = new IntVec3(20, 0, 10);
        path.nextCell = new IntVec3(1, 0, 10);
        path.nextCellCostTotal = 20f;
        Program.Check(ShotgunBallistics.Position(pawn) == new Vector3(1f, 0f, 10.5f), "Moving collision position must follow path progress.");
        Program.Near(ShotgunBallistics.Velocity(pawn).X, 0.05f, "Lead velocity must use actual path costs in tiles per tick.");
        Vector3 aim = ShotgunBallistics.AimPoint(pawn, new Vector3(1f, 0f, 0.5f), 1f);
        Program.Check(aim.X > 1.5f && aim.Z == 10.5f, "A strafing target must be led sideways.");
        AccessTools.FieldRefAccess<Pawn_PathFollower, LocalTargetInfo>("destination")(path) = new IntVec3(1, 0, 10);
        aim = ShotgunBallistics.AimPoint(pawn, new Vector3(1f, 0f, 0.5f), 1f);
        Program.Near(aim.X, 1.5f, "Lead must stop at a known destination.");
        AccessTools.FieldRefAccess<Pawn_PathFollower, bool>("cachedWillCollideNextCell")(path) = true;
        Program.Check(ShotgunBallistics.Velocity(pawn) == Vector3.Zero, "A blocked target must not retain a moving lead.");
        Program.Near(ShotgunBallistics.Position(pawn).X, 1f, "Blocking must not teleport a partially completed step back to its cell centre.");
        Program.Check(Program.Bare<Projectile_ShotgunPellet>().UpdateRateTicks == 1, "Pellets must register for per-tick updates before launch, even without a shell or camera.");
    }
}
