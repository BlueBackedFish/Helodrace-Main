using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Helodrace;
using Verse;

internal static class ShotgunDamageTests
{
    private static readonly List<Tuple<BodyPartRecord, float>> injuries = new List<Tuple<BodyPartRecord, float>>();
    private static int armorChecks;
    private static float armorPass = 1f;
    private static bool convertToBlunt;
    private static DamageDef blunt;
    private static bool FinalizeFixture(Pawn pawn, float totalDamage, DamageInfo dinfo, DamageWorker.DamageResult result, ref float __result)
    {
        injuries.Add(Tuple.Create(dinfo.HitPart, totalDamage));
        result.totalDamageDealt += totalDamage;
        __result = totalDamage;
        return false;
    }
    private static bool ArmorFixture(float amount, ref DamageDef damageDef, ref bool deflectedByMetalArmor, ref bool diminishedByMetalArmor, ref float __result)
    {
        armorChecks++;
        if (convertToBlunt) damageDef = blunt;
        deflectedByMetalArmor = armorPass == 0f;
        diminishedByMetalArmor = armorPass < 1f;
        __result = amount * armorPass;
        return false;
    }
    internal static void Run(Harmony harmony)
    {
        var headshot = AccessTools.Method(typeof(DamageWorker_AddInjury), "IsHeadshot");
        harmony.Patch(headshot, prefix: new HarmonyMethod(typeof(Program), nameof(Program.NoEngineEffects)));
        var finalize = AccessTools.Method(typeof(DamageWorker_AddInjury), "FinalizeAndAddInjury", new[] { typeof(Pawn), typeof(float), typeof(DamageInfo), typeof(DamageWorker.DamageResult) });
        var stub = new HarmonyMethod(typeof(ShotgunDamageTests), nameof(FinalizeFixture)) { priority = Priority.Last };
        harmony.Patch(finalize, prefix: stub);
        var armor = AccessTools.Method(typeof(ArmorUtility), nameof(ArmorUtility.GetPostArmorDamage));
        harmony.Patch(armor, prefix: new HarmonyMethod(typeof(ShotgunDamageTests), nameof(ArmorFixture)));
        var apply = AccessTools.Method(typeof(DamageWorker_AddInjury), "ApplyDamageToPart");
        var body = new BodyDef();
        var torso = Program.Part(body, "CustomShell", 0.7f);
        var organ = Program.Part(body, "CustomCore", 0.3f, torso, true);
        var pawn = Program.Pawn(body);
        var pellet = Program.Bare<Projectile_ShotgunPellet>();
        pellet.def = ShotgunLaunchTests.ProjectileDef();
        var damageDef = pellet.def.projectile.damageDef;
        blunt = new DamageDef { defName = "FixtureBlunt" };
        var worker = new DamageWorker_Stab { def = damageDef };
        var shell = new ShotgunShell { id = 450 };
        shell.CentreFor(pawn, damageDef);
        var damage = new DamageInfo(damageDef, 6f, 0.1f, angle: 0);
        ShotgunSpreadContext.Impact = new ShotgunImpact { pawn = pawn, pellet = pellet, shell = shell, damageDef = damageDef,
            relativeSpread = 0f, energy = 1f, incomingDamage = 6f };
        try
        {
            for (int i = 0; i < 9; i++) apply.Invoke(worker, new object[] { damage, pawn, new DamageWorker.DamageResult() });
            Program.Check(armorChecks == 9, "Nine pellets must check surface armor nine times, never once as a combined projectile.");
            Program.Near(injuries.Sum(value => value.Item2), 54f, "Surface plus internal damage must conserve the pellet damage budget.");
            Program.Check(injuries.Count == 18 && injuries.Count(value => value.Item1 == organ) == 9, "Each penetrating pellet must allocate damage to its own internal branch.");
            Program.Check(!ShotgunSpreadContext.Impact.internalDamage, "Penetration recursion state must be restored.");
            armorPass = 0f; injuries.Clear(); armorChecks = 0;
            apply.Invoke(worker, new object[] { damage, pawn, new DamageWorker.DamageResult() });
            Program.Check(injuries.Count == 0 && armorChecks == 1, "A stopped pellet must cause no internal damage.");
            armorPass = 0.5f; injuries.Clear(); armorChecks = 0;
            apply.Invoke(worker, new object[] { damage, pawn, new DamageWorker.DamageResult() });
            Program.Near(injuries.Sum(value => value.Item2), 3f, "Armor-reduced damage must not be restored by internal penetration.");
            Program.Check(armorChecks == 1, "Internal damage must not apply surface armor a second time.");
            convertToBlunt = true; armorPass = 1f; injuries.Clear();
            apply.Invoke(worker, new object[] { damage, pawn, new DamageWorker.DamageResult() });
            Program.Check(injuries.Count == 1 && injuries[0].Item1 == torso, "A blunt-converted pellet must not penetrate into organs.");
            var nested = new BodyDef();
            var surface = Program.Part(nested, "Outer", 0.6f);
            var bone = Program.Part(nested, "Barrier", 0.2f, surface, true);
            var brain = Program.Part(nested, "DeepOrgan", 0.2f, bone, true);
            Program.Check(ShotgunBodyProfile.InternalPath(surface, brain).SequenceEqual(new[] { bone, brain }), "Internal penetration must follow intervening structures in order.");
            Program.Check(!Patch_DamageWorker_ShotgunSmallPawn.Prefix(pawn), "Small-pawn propagation must not duplicate a pellet's damage on its parent.");
        }
        finally
        {
            ShotgunSpreadContext.Impact = null;
            harmony.Unpatch(finalize, stub.method);
            harmony.Unpatch(armor, HarmonyPatchType.Prefix, harmony.Id);
            harmony.Unpatch(headshot, HarmonyPatchType.Prefix, harmony.Id);
            armorPass = 1f; convertToBlunt = false;
        }
    }
}
