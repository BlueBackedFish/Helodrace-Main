using RimWorld;
using Verse;

namespace Helodrace
{
    public class CompProperties_ANFORainSensitivity : CompProperties
    {
        public float mtbHours = 1f;

        public CompProperties_ANFORainSensitivity()
        {
            compClass = typeof(CompANFORainSensitivity);
        }
    }

    public class CompANFORainSensitivity : ThingComp
    {
        private bool disabledByRain;

        private CompProperties_ANFORainSensitivity Props =>
            (CompProperties_ANFORainSensitivity)props;

        public bool CanDetonate => !disabledByRain;

        private bool DirectlyExposedToRain =>
            parent.Spawned
            && parent.Map.weatherManager.RainRate > 0.01f
            && !parent.Position.Roofed(parent.Map);

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            StopWickIfDisabled();
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref disabledByRain, "anfoDisabledByRain", false);
        }

        public override void CompTick()
        {
            base.CompTick();

            if (disabledByRain)
            {
                StopWickIfDisabled();
                return;
            }

            if (DirectlyExposedToRain
                && Rand.MTBEventOccurs(Props.mtbHours, GenDate.TicksPerHour, 1f))
            {
                DisableFromRain();
            }
        }

        public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
        {
            base.PostPreApplyDamage(ref dinfo, out absorbed);
            StopDamageTriggeredWickIfNeeded();
        }

        public override void PostPostApplyDamage(DamageInfo dinfo, float totalDamageDealt)
        {
            base.PostPostApplyDamage(dinfo, totalDamageDealt);
            StopDamageTriggeredWickIfNeeded();
        }

        public override string CompInspectStringExtra()
        {
            if (disabledByRain)
                return "HD_ANFO_RainDisabled".Translate();

            return null;
        }

        private void DisableFromRain()
        {
            if (disabledByRain)
                return;

            disabledByRain = true;
            StopWickIfDisabled();

            if (parent.Faction == Faction.OfPlayer)
            {
                Messages.Message(
                    "HD_ANFO_RainDisabledMessage".Translate(parent.LabelCap),
                    parent,
                    MessageTypeDefOf.NegativeEvent,
                    false);
            }
        }

        private void StopWickIfDisabled()
        {
            if (!disabledByRain)
                return;

            parent.TryGetComp<CompExplosive>()?.StopWick();
        }

        private void StopDamageTriggeredWickIfNeeded()
        {
            if (!disabledByRain && !DirectlyExposedToRain)
                return;

            parent.TryGetComp<CompExplosive>()?.StopWick();
        }
    }
}
