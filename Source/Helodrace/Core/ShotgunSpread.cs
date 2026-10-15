using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    internal static class ShotgunSpreadRules
    {
        internal static IEnumerable<BodyPartRecord> Candidates(BodyPartRecord hitPart, int spreadFactor)
        {
            if (hitPart == null) yield break;
            yield return hitPart;
            BodyPartRecord current = hitPart;
            for (int level = 0; level < spreadFactor; level++)
            {
                BodyPartRecord parent = current.parent;
                // The core has no parent: use its own children at the first level.
                if (parent == null)
                {
                    if (level == 0)
                        foreach (BodyPartRecord child in current.parts) yield return child;
                    yield break;
                }
                // Exactly one downward link from this parent, never a recursive
                // walk through its children. Spread 1 therefore hits siblings.
                foreach (BodyPartRecord child in parent.parts)
                    if (child != hitPart) yield return child;
                current = parent;
            }
        }

        internal static bool IsPresent(Pawn pawn, BodyPartRecord part)
        {
            for (BodyPartRecord current = part; current != null; current = current.parent)
                if (pawn.health.hediffSet.PartIsMissing(current)) return false;
            return true;
        }

        internal static BodyPartRecord Choose(Pawn pawn, BodyPartRecord anchor, int spreadFactor, DamageInfo damage)
        {
            List<BodyPartRecord> candidates = Candidates(anchor, spreadFactor)
                .Where(part => IsPresent(pawn, part) && part.coverageAbs > 0f
                    && (damage.Depth == BodyPartDepth.Undefined || part.depth == damage.Depth))
                .ToList();
            List<BodyPartRecord> atHeight = candidates
                .Where(part => damage.Height == BodyPartHeight.Undefined || part.height == damage.Height).ToList();
            if (atHeight.Count > 0) candidates = atHeight;
            if (candidates.TryRandomElementByWeight(
                part => part.coverageAbs * part.def.GetHitChanceFactorFor(damage.Def), out BodyPartRecord result))
                return result;
            candidates.TryRandomElementByWeight(part => part.coverageAbs, out result);
            // An exhausted region must not fall back to an unrelated body part.
            return result;
        }
    }

    public sealed class ShotgunHitAnchor : IExposable
    {
        public Pawn pawn;
        public BodyPartRecord part;

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_BodyParts.Look(ref part, "part");
        }
    }

    public sealed class ShotgunShell : IExposable
    {
        public int spreadFactor = 1;
        public List<Projectile> projectiles = new List<Projectile>();
        private List<ShotgunHitAnchor> anchors = new List<ShotgunHitAnchor>();

        public ShotgunShell() { }
        public ShotgunShell(int spreadFactor) { this.spreadFactor = Math.Max(0, spreadFactor); }

        internal ShotgunHitAnchor AnchorFor(Pawn pawn) => anchors.Find(anchor => anchor.pawn == pawn);

        internal void Remember(Pawn pawn, BodyPartRecord part)
        {
            if (part != null && AnchorFor(pawn) == null)
                anchors.Add(new ShotgunHitAnchor { pawn = pawn, part = part });
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref spreadFactor, "spreadFactor", 1);
            Scribe_Collections.Look(ref projectiles, "projectiles", LookMode.Reference);
            Scribe_Collections.Look(ref anchors, "anchors", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                projectiles = projectiles ?? new List<Projectile>();
                anchors = anchors ?? new List<ShotgunHitAnchor>();
                anchors.RemoveAll(anchor => anchor?.pawn == null || anchor.part == null);
                spreadFactor = Math.Max(0, spreadFactor);
            }
        }
    }

    // Own the shared shell once so that in-flight pellets retain their common
    // anchors after save/load, including pellets that strike different pawns.
    public sealed class MapComponent_ShotgunShells : MapComponent
    {
        private List<ShotgunShell> shells = new List<ShotgunShell>();
        private readonly Dictionary<Projectile, ShotgunShell> byProjectile = new Dictionary<Projectile, ShotgunShell>();

        public MapComponent_ShotgunShells(Map map) : base(map) { }

        internal void Register(Projectile projectile, ShotgunShell shell)
        {
            if (byProjectile.ContainsKey(projectile)) return;
            if (shell.projectiles.Count == 0) shells.Add(shell);
            shell.projectiles.Add(projectile);
            byProjectile.Add(projectile, shell);
        }

        internal ShotgunShell ShellFor(Projectile projectile) =>
            byProjectile.TryGetValue(projectile, out ShotgunShell shell) ? shell : null;

        private void Prune()
        {
            foreach (ShotgunShell shell in shells)
                shell.projectiles.RemoveAll(projectile =>
                {
                    if (projectile != null && !projectile.Destroyed) return false;
                    if (projectile != null) byProjectile.Remove(projectile);
                    return true;
                });
            shells.RemoveAll(shell => shell.projectiles.Count == 0);
        }

        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 60 == 0 && shells.Count > 0) Prune();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving) Prune();
            Scribe_Collections.Look(ref shells, "shotgunShells", LookMode.Deep);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                shells = shells ?? new List<ShotgunShell>();
                shells.RemoveAll(shell => shell == null);
                Prune();
                byProjectile.Clear();
                foreach (ShotgunShell shell in shells)
                    foreach (Projectile projectile in shell.projectiles) byProjectile[projectile] = shell;
            }
        }
    }

    internal sealed class ShotgunImpact
    {
        internal ShotgunShell shell;
        internal Pawn pawn;
        internal DamageDef damageDef;
    }

    internal static class ShotgunSpreadContext
    {
        [ThreadStatic] internal static ShotgunShell Launching;
        [ThreadStatic] internal static ShotgunImpact Impact;
    }

    [HarmonyPatch(typeof(Projectile), nameof(Projectile.Launch), new[]
    {
        typeof(Thing), typeof(Vector3), typeof(LocalTargetInfo), typeof(LocalTargetInfo),
        typeof(ProjectileHitFlags), typeof(bool), typeof(Thing), typeof(ThingDef)
    })]
    public static class Patch_Projectile_Launch_ShotgunShell
    {
        public static void Postfix(Projectile __instance)
        {
            ShotgunShell shell = ShotgunSpreadContext.Launching;
            if (shell != null && __instance is Bullet && __instance.Map != null)
                __instance.Map.GetComponent<MapComponent_ShotgunShells>().Register(__instance, shell);
        }
    }

    [HarmonyPatch(typeof(Bullet), "Impact")]
    public static class Patch_Bullet_Impact_ShotgunShell
    {
        internal static void Prefix(Bullet __instance, Thing hitThing, bool blockedByShield, out ShotgunImpact __state)
        {
            __state = ShotgunSpreadContext.Impact;
            ShotgunShell shell = __instance.Map?.GetComponent<MapComponent_ShotgunShells>().ShellFor(__instance);
            ShotgunSpreadContext.Impact = !blockedByShield && shell != null && hitThing is Pawn pawn
                ? new ShotgunImpact { shell = shell, pawn = pawn, damageDef = __instance.DamageDef }
                : null;
        }

        internal static void Finalizer(ShotgunImpact __state)
        {
            ShotgunSpreadContext.Impact = __state;
        }
    }

    // Patch the exact-part lookup so an exhausted region can return null without
    // ChooseHitPart's vanilla warning or a fallback to an unrelated part.
    [HarmonyPatch(typeof(DamageWorker_AddInjury), "GetExactPartFromDamageInfo")]
    public static class Patch_DamageWorker_ShotgunHitPart
    {
        public static bool Prefix(DamageInfo dinfo, Pawn pawn, ref BodyPartRecord __result)
        {
            ShotgunImpact impact = ShotgunSpreadContext.Impact;
            if (impact?.pawn != pawn || impact.damageDef != dinfo.Def || dinfo.HitPart != null) return true;
            ShotgunHitAnchor anchor = impact.shell.AnchorFor(pawn);
            if (anchor == null) return true;
            __result = ShotgunSpreadRules.Choose(pawn, anchor.part, impact.shell.spreadFactor, dinfo);
            return false;
        }

        public static void Postfix(DamageInfo dinfo, Pawn pawn, BodyPartRecord __result)
        {
            ShotgunImpact impact = ShotgunSpreadContext.Impact;
            if (impact?.pawn == pawn && impact.damageDef == dinfo.Def)
                impact.shell.Remember(pawn, __result);
        }
    }
}
