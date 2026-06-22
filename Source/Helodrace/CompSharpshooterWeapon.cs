using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;
using HarmonyLib;

namespace Helodrace
{
    public class CompProperties_SharpshooterWeapon : CompProperties
    {
        public int altVerbIndex = 1; 
        public float altCooldownMultiplier = 1.0f; 
        public float altAccuracyMultiplier = 1.0f; 
        public int switchTicks = 120; // Default 2 seconds to switch modes

        public CompProperties_SharpshooterWeapon()
        {
            this.compClass = typeof(CompSharpshooterWeapon);
        }
    }

    public class CompSharpshooterWeapon : ThingComp
    {
        public CompProperties_SharpshooterWeapon Props => (CompProperties_SharpshooterWeapon)props;
        
        public bool altModeActive = false;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref altModeActive, "altModeActive", false);
        }

        public Pawn Wielder
        {
            get
            {
                if (parent.ParentHolder is Pawn_EquipmentTracker equipmentTracker)
                {
                    return equipmentTracker.pawn;
                }
                if (parent.ParentHolder is Pawn_ApparelTracker apparelTracker)
                {
                    return apparelTracker.pawn;
                }
                if (parent.ParentHolder is Pawn_InventoryTracker inventoryTracker)
                {
                    return inventoryTracker.pawn;
                }
                return parent.ParentHolder as Pawn;
            }
        }

        public bool CanUseSharpshooterMode
        {
            get
            {
                Pawn wielder = Wielder;
                if (wielder == null)
                {
                    Log.Message("[Sharpshooter] CanUseSharpshooterMode: Wielder is null");
                    return false;
                }
                if (wielder.genes == null)
                {
                    Log.Message($"[Sharpshooter] CanUseSharpshooterMode: {wielder.Name?.ToStringShort ?? wielder.LabelShort} has null genes");
                    return false;
                }
                GeneDef geneDef = DefDatabase<GeneDef>.GetNamedSilentFail("HD_Gene_Sharpshooter");
                if (geneDef == null)
                {
                    Log.Message("[Sharpshooter] CanUseSharpshooterMode: HD_Gene_Sharpshooter def is null!");
                    return false;
                }
                bool active = wielder.genes.HasActiveGene(geneDef);
                Log.Message($"[Sharpshooter] CanUseSharpshooterMode: {wielder.Name?.ToStringShort ?? wielder.LabelShort} has active gene: {active}");
                return active;
            }
        }

        public Verb AltVerb
        {
            get
            {
                var eq = parent.GetComp<CompEquippable>();
                if (eq != null && eq.AllVerbs.Count > Props.altVerbIndex)
                {
                    return eq.AllVerbs[Props.altVerbIndex];
                }
                return null;
            }
        }

        public void PerformSwitch()
        {
            altModeActive = !altModeActive;
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo g in base.CompGetGizmosExtra())
            {
                yield return g;
            }
        }
    }

    public class JobDriver_SwitchSharpshooterMode : JobDriver
    {
        protected Thing Weapon => job.targetB.Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            var comp = Weapon?.TryGetComp<CompSharpshooterWeapon>();
            if (comp == null) yield break;

            Toil switchToil = new Toil();
            switchToil.initAction = delegate
            {
                pawn.pather.StopDead();
            };
            switchToil.tickAction = delegate
            {
                // Can add custom effects or sounds here while switching
            };
            switchToil.defaultCompleteMode = ToilCompleteMode.Delay;
            switchToil.defaultDuration = comp.Props.switchTicks;
            switchToil.WithProgressBarToilDelay(TargetIndex.A);
            yield return switchToil;

            yield return new Toil
            {
                initAction = delegate
                {
                    comp.PerformSwitch();
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }

    [HarmonyPatch(typeof(VerbTracker), "get_PrimaryVerb")]
    public static class Patch_VerbTracker_PrimaryVerb
    {
        public static void Postfix(VerbTracker __instance, ref Verb __result)
        {
            if (__instance.directOwner is CompEquippable eq && eq.parent != null)
            {
                var comp = eq.parent.TryGetComp<CompSharpshooterWeapon>();
                if (comp != null && comp.altModeActive && comp.CanUseSharpshooterMode && comp.AltVerb != null)
                {
                    __result = comp.AltVerb;
                }
            }
        }
    }

    [HarmonyPatch(typeof(StatExtension), "GetStatValue")]
    public static class Patch_StatExtension_GetStatValue
    {
        public static void Postfix(Thing thing, StatDef stat, ref float __result)
        {
            if (thing is Pawn pawn)
            {
                if (stat == StatDefOf.ShootingAccuracyPawn && pawn.equipment?.Primary != null)
                {
                    var comp = pawn.equipment.Primary.TryGetComp<CompSharpshooterWeapon>();
                    if (comp != null && comp.altModeActive && comp.CanUseSharpshooterMode)
                    {
                        // ShootingAccuracyPawn is a probability per tile between 0 and 1.0 (typically 0.95 - 0.99).
                        // Multiplying it directly can make it > 1.0, which grows exponentially when raised to the power of distance (resulting in 100,000%+ accuracy).
                        // Instead, we reduce the remaining inaccuracy (error rate) by the multiplier.
                        float multiplier = comp.Props.altAccuracyMultiplier;
                        if (multiplier > 0f)
                        {
                            float inaccuracy = 1.0f - __result;
                            float newInaccuracy = inaccuracy / multiplier;
                            __result = UnityEngine.Mathf.Clamp(1.0f - newInaccuracy, 0.0f, 0.999f);
                        }
                    }
                }
            }
            else
            {
                if (stat == StatDefOf.RangedWeapon_Cooldown)
                {
                    var comp = thing.TryGetComp<CompSharpshooterWeapon>();
                    if (comp != null && comp.altModeActive && comp.CanUseSharpshooterMode)
                    {
                        __result *= comp.Props.altCooldownMultiplier;
                    }
                }
            }
        }
    }

    [HarmonyPatch(typeof(CompEquippable), "get_PrimaryVerb")]
    public static class Patch_CompEquippable_PrimaryVerb
    {
        public static void Postfix(CompEquippable __instance, ref Verb __result)
        {
            if (__instance.parent != null)
            {
                var comp = __instance.parent.TryGetComp<CompSharpshooterWeapon>();
                if (comp != null && comp.altModeActive && comp.CanUseSharpshooterMode && comp.AltVerb != null)
                {
                    __result = comp.AltVerb;
                }
            }
        }
    }

    [HarmonyPatch(typeof(CompEquippable), nameof(CompEquippable.GetVerbsCommands))]
    public static class Patch_CompEquippable_GetVerbsCommands
    {
        public static void Postfix(CompEquippable __instance, ref IEnumerable<Command> __result)
        {
            if (__instance.parent != null)
            {
                var comp = __instance.parent.TryGetComp<CompSharpshooterWeapon>();
                if (comp != null)
                {
                    Log.Message($"[Sharpshooter] GetVerbsCommands called. altModeActive: {comp.altModeActive}, CanUseSharpshooterMode: {comp.CanUseSharpshooterMode}");
                    
                    var updated = new List<Command>();
                    bool shouldSwap = comp.altModeActive && comp.CanUseSharpshooterMode && comp.AltVerb != null;
                    
                    foreach (var command in __result)
                    {
                        if (shouldSwap && command is Command_VerbTarget cmdVerb)
                        {
                            cmdVerb.verb = comp.AltVerb;
                            Log.Message("[Sharpshooter] Swapped manual Attack command verb to AltVerb!");
                        }
                        updated.Add(command);
                    }

                    Pawn wielder = comp.Wielder;
                    bool isPlayerPawn = wielder != null && wielder.Faction != null && wielder.Faction.IsPlayer;

                    if (isPlayerPawn && comp.CanUseSharpshooterMode && comp.AltVerb != null)
                    {
                        var iconTex = ContentFinder<UnityEngine.Texture2D>.Get("Icon/HD_Sharpshooter", false);
                        if (iconTex == null)
                        {
                            iconTex = BaseContent.BadTex;
                        }

                        var switchCommand = new Command_Action
                        {
                            defaultLabel = "HD_SharpshooterMode_Label".Translate().Resolve() + (comp.altModeActive ? " (ON)" : " (OFF)"),
                            defaultDesc = "HD_SharpshooterMode_Desc".Translate().Resolve(),
                            icon = iconTex, 
                            action = () => 
                            {
                                Pawn pawn = comp.Wielder;
                                if (pawn != null)
                                {
                                    Job job = JobMaker.MakeJob(DefDatabase<JobDef>.GetNamed("HD_SwitchSharpshooterMode"), pawn, comp.parent);
                                    pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                                }
                            }
                        };
                        updated.Add(switchCommand);
                        Log.Message("[Sharpshooter] Appended switch mode button to GetVerbsCommands results!");
                    }
                    
                    __result = updated;
                }
            }
        }
    }
}