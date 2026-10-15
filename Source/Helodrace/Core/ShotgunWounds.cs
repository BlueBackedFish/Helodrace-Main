using System.Collections.Generic;
using HarmonyLib;
using Verse;

namespace Helodrace
{
    public sealed class ShotgunWound : IExposable
    {
        public Hediff_Injury injury;
        public int shellId;
        public int pelletCount;
        public int lastPelletId = -1;
        public void ExposeData()
        {
            Scribe_References.Look(ref injury, "injury");
            Scribe_Values.Look(ref shellId, "shellId");
            Scribe_Values.Look(ref pelletCount, "pelletCount");
            Scribe_Values.Look(ref lastPelletId, "lastPelletId", -1);
        }
    }

    // Keep ordinary, mod-compatible injury hediffs. Only provenance and the
    // displayed pellet count live here, including wounds on travelling pawns.
    public sealed class GameComponent_ShotgunWounds : GameComponent
    {
        private List<ShotgunWound> wounds = new List<ShotgunWound>();
        private readonly Dictionary<Hediff_Injury, ShotgunWound> byInjury = new Dictionary<Hediff_Injury, ShotgunWound>();
        internal static GameComponent_ShotgunWounds Current => Verse.Current.Game?.GetComponent<GameComponent_ShotgunWounds>();
        public GameComponent_ShotgunWounds(Game game) { }
        internal ShotgunWound For(Hediff_Injury injury) => byInjury.TryGetValue(injury, out ShotgunWound wound) ? wound : null;
        internal void Register(Hediff_Injury injury, int shellId, int pelletId)
        {
            if (!Present(injury)) return;
            if (!byInjury.TryGetValue(injury, out ShotgunWound wound))
            {
                wound = new ShotgunWound { injury = injury, shellId = shellId };
                byInjury.Add(injury, wound);
                wounds.Add(wound);
            }
            if (wound.shellId == shellId && wound.lastPelletId != pelletId)
            {
                wound.pelletCount++;
                wound.lastPelletId = pelletId;
            }
        }
        private static bool Present(Hediff_Injury injury) => injury?.pawn != null && !injury.pawn.Destroyed
            && injury.pawn.health.hediffSet.hediffs.Contains(injury);
        private void Prune()
        {
            wounds.RemoveAll(wound => wound == null || !Present(wound.injury));
            byInjury.Clear();
            foreach (ShotgunWound wound in wounds) byInjury[wound.injury] = wound;
        }
        public override void GameComponentTick()
        {
            if (wounds.Count > 0 && Find.TickManager.TicksGame % 600 == 0) Prune();
        }
        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving) Prune();
            Scribe_Collections.Look(ref wounds, "shotgunWounds", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                wounds = wounds ?? new List<ShotgunWound>();
                Prune();
            }
        }
    }

    [HarmonyPatch(typeof(HediffWithComps), nameof(HediffWithComps.LabelBase), MethodType.Getter)]
    public static class Patch_Hediff_ShotgunPelletCount
    {
        public static void Postfix(HediffWithComps __instance, ref string __result)
        {
            if (!(__instance is Hediff_Injury injury)) return;
            ShotgunWound wound = GameComponent_ShotgunWounds.Current?.For(injury);
            if (wound != null && wound.pelletCount > 0)
                __result += " (" + "HD_Shotgun_PelletCount".Translate(wound.pelletCount).Resolve() + ")";
        }
    }
}
