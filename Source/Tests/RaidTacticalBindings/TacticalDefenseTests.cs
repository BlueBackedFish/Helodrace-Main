using System;
using System.Runtime.CompilerServices;
using Helodrace.Tactics;
using RimWorld;
using Verse.AI.Group;

internal static class TacticalDefenseTests
{
    public static void Run()
    {
        LordJob job = (LordJob)RuntimeHelpers.GetUninitializedObject(typeof(LordJob_DefendBase));
        foreach (Type type in new[] { typeof(LordToil_DefendBase), typeof(LordToil_DefendPoint) })
        {
            LordToil guard = (LordToil)RuntimeHelpers.GetUninitializedObject(type);
            if (!GameComponent_TacticalCommands.IsDefensePhase(guard)
                || GameComponent_TacticalCommands.IsAssaultPhase(job, guard))
                throw new Exception("Native fixed defense must not be treated as an assault.");
        }
        LordToil attack = (LordToil)RuntimeHelpers.GetUninitializedObject(typeof(LordToil_AssaultColony));
        if (GameComponent_TacticalCommands.IsDefensePhase(attack)
            || !GameComponent_TacticalCommands.IsAssaultPhase(job, attack))
            throw new Exception("A DefendBase Lord must become an assault command when its native graph switches to assault.");
        LordToil exit = (LordToil)RuntimeHelpers.GetUninitializedObject(typeof(LordToil_ExitMap));
        if (GameComponent_TacticalCommands.IsDefensePhase(exit) || GameComponent_TacticalCommands.IsAssaultPhase(job, exit)
            || GameComponent_TacticalCommands.IsDefensePhase(null) || GameComponent_TacticalCommands.IsAssaultPhase(null, attack))
            throw new Exception("Exited or missing native duties cannot retain tactical ownership.");
        if (!TacticalMedicalPolicy.SafePhase(TacticalCommandPhase.Defending, true, null)
            || TacticalMedicalPolicy.SafePhase(TacticalCommandPhase.Defending, true, TacticalFieldStage.Moving))
            throw new Exception("Fixed idle defense can offer care, moving field defense cannot.");
        if (!GameComponent_TacticalCommands.CanControlMission(true, false, false)
            || GameComponent_TacticalCommands.CanControlMission(false, false, false)
            || !GameComponent_TacticalCommands.CanControlMission(false, true, false)
            || !GameComponent_TacticalCommands.CanControlMission(false, false, true))
            throw new Exception("Neutral defenders cannot acquire the player's bed as an attack goal without authorization after a native duty switch.");
        Console.WriteLine("PASS: native Defend/DefendBase, graph transition to assault, relinquished exit and safe fixed-defense care boundaries.");
    }
}
