using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace
{
    public sealed class CrowsWeaponDef : Def
    {
        public ThingDef gunDef;
        public List<ThingDefCountClass> costList = new List<ThingDefCountClass>();
        public float workToInstall = 1800f;
        public float weaponMass;
        public int fuelCapacity = 100;
        public string frameTexPath, barrelTexPath, bodyTexPath, feedTexPath, iconTexPath;
        public bool barrelRecoil;

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (gunDef == null) yield return "CROWS weapon requires gunDef";
            if (costList.Count == 0 || costList.Any(c => c.thingDef == null || c.count <= 0)) yield return "CROWS weapon requires positive material costs";
            if (workToInstall <= 0 || fuelCapacity <= 0) yield return "CROWS weapon requires positive work and ammunition capacity";
            if (weaponMass <= 0) yield return "CROWS weapon requires a positive unloaded weapon mass";
            if (new[] { frameTexPath, barrelTexPath, bodyTexPath, feedTexPath, iconTexPath }.Any(string.IsNullOrEmpty)) yield return "CROWS weapon requires all graphic layers";
        }
    }

    public sealed partial class Building_Crows : IThingHolder
    {
        private CrowsWeaponDef installedWeapon, requestedWeapon;
        private ThingOwner<Thing> mountingMaterials;
        private float mountingWork;
        public CrowsWeaponDef InstalledWeapon => installedWeapon;
        public CrowsWeaponDef RequestedWeapon => requestedWeapon;
        public bool HasWeapon => installedWeapon != null;
        public bool InstallationPending => requestedWeapon != null && !HasWeapon;
        public float InstallationProgress => requestedWeapon == null ? 0f : Mathf.Clamp01(mountingWork / requestedWeapon.workToInstall);
        public ThingOwner<Thing> MountingMaterials => mountingMaterials ?? (mountingMaterials = new ThingOwner<Thing>(this));
        public ThingOwner GetDirectlyHeldThings() => MountingMaterials;
        public void GetChildHolders(List<IThingHolder> outChildren) => ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, MountingMaterials);

        public int MissingMaterial(ThingDef material) => !InstallationPending ? 0 : Math.Max(0,
            requestedWeapon.costList.Where(c => c.thingDef == material).Sum(c => c.count) - MountingMaterials.TotalStackCountOfDef(material));
        public bool MaterialsReady => InstallationPending && requestedWeapon.costList.All(c => MissingMaterial(c.thingDef) == 0);

        public bool RequestWeapon(CrowsWeaponDef weapon)
        {
            if (!Spawned || Faction != Faction.OfPlayer || HasWeapon || weapon == null || weapon == requestedWeapon) return false;
            if (!CancelWeaponInstallation()) return false;
            requestedWeapon = weapon;
            StopRemoteFire();
            return true;
        }

        public bool CancelWeaponInstallation()
        {
            // Stored materials stay physical items. Never erase them if a map
            // placement fails; the pending operation can be resumed or cancelled.
            if (MountingMaterials.Count > 0 && (!Spawned || !MountingMaterials.TryDropAll(Position, Map, ThingPlaceMode.Near))) return false;
            requestedWeapon = null;
            mountingWork = 0f;
            return true;
        }

        public int AcceptMountingMaterial(ThingOwner source, Thing material, int count)
        {
            int accepted = Math.Min(count, Math.Min(material.stackCount, MissingMaterial(material.def)));
            return accepted <= 0 ? 0 : source.TryTransferToContainer(material, MountingMaterials, accepted);
        }

        public bool AddMountingWork(float amount)
        {
            if (!MaterialsReady || amount <= 0f) return false;
            mountingWork = Math.Min(requestedWeapon.workToInstall, mountingWork + amount);
            if (mountingWork < requestedWeapon.workToInstall) return false;
            StopRemoteFire();
            // Make the selected gun first; no construction materials are lost
            // if its definition cannot be instantiated.
            Thing newGun = ThingMaker.MakeThing(requestedWeapon.gunDef);
            Thing oldGun = gun;
            gun = newGun;
            foreach (Verb verb in GunCompEq.AllVerbs) { verb.caster = this; verb.castCompleteCallback = BurstComplete; }
            oldGun?.Destroy();
            installedWeapon = requestedWeapon;
            requestedWeapon = null;
            mountingWork = 0f;
            MountingMaterials.ClearAndDestroyContents();
            burstCooldownTicksLeft = 0;
            GetComp<CompCrowsAmmo>()?.ConfigureWeapon();
            GetComp<CompCrowsGraphic>()?.NotifyWeaponChanged();
            return true;
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad = false)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            GetComp<CompCrowsAmmo>()?.ConfigureWeapon();
        }

        private void ExposeWeaponMountData()
        {
            Scribe_Defs.Look(ref installedWeapon, "installedCrowsWeapon");
            Scribe_Defs.Look(ref requestedWeapon, "requestedCrowsWeapon");
            Scribe_Values.Look(ref mountingWork, "crowsMountingWork", 0f);
            Scribe_Deep.Look(ref mountingMaterials, "crowsMountingMaterials", this);
        }

        private IEnumerable<Gizmo> WeaponMountGizmos()
        {
            if (HasWeapon) yield break;
            yield return new Command_Action
            {
                defaultLabel = "HD_Crows_SelectWeapon".Translate(),
                defaultDesc = "HD_Crows_SelectWeaponDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get(requestedWeapon?.iconTexPath ?? "UI/Commands/BuildCopy"),
                action = () => Find.WindowStack.Add(new FloatMenu(DefDatabase<CrowsWeaponDef>.AllDefsListForReading
                    .Select(weapon => new FloatMenuOption(weapon.LabelCap + " — " + CostLabel(weapon), () => RequestWeapon(weapon))).ToList()))
            };
            if (InstallationPending) yield return new Command_Action
            {
                defaultLabel = "HD_Crows_CancelInstall".Translate(),
                defaultDesc = "HD_Crows_CancelInstallDesc".Translate(),
                icon = ContentFinder<Texture2D>.Get("UI/Commands/Cancel"),
                action = () => CancelWeaponInstallation()
            };
        }

        private static string CostLabel(CrowsWeaponDef weapon) => string.Join(", ", weapon.costList.Select(c => c.thingDef.LabelCap + " ×" + c.count));
        private string WeaponMountInspectString()
        {
            if (HasWeapon) return "HD_Crows_InstalledWeapon".Translate(installedWeapon.LabelCap);
            if (!InstallationPending) return "HD_Crows_NoWeapon".Translate();
            string needs = string.Join(", ", requestedWeapon.costList.Where(c => MissingMaterial(c.thingDef) > 0)
                .Select(c => c.thingDef.LabelCap + " ×" + MissingMaterial(c.thingDef)));
            return "HD_Crows_Installing".Translate(requestedWeapon.LabelCap, InstallationProgress.ToStringPercent())
                + (needs.Length == 0 ? "" : "\n" + "HD_Crows_MissingMaterials".Translate(needs).ToString());
        }
    }

    public sealed class CompProperties_CrowsAmmo : CompProperties_Refuelable
    {
        public CompProperties_CrowsAmmo() { compClass = typeof(CompCrowsAmmo); }
        public CompProperties_CrowsAmmo Copy() => (CompProperties_CrowsAmmo)MemberwiseClone();
    }

    public sealed class CompCrowsAmmo : CompRefuelable
    {
        public override float GetStatOffset(StatDef stat) => base.GetStatOffset(stat)
            + (stat == StatDefOf.Mass ? (parent as Building_Crows)?.InstalledWeapon?.weaponMass ?? 0f : 0f);

        public override void Initialize(CompProperties properties)
        {
            // Refuelable props normally belong to the shared ThingDef. Every
            // station needs its own filter/capacity when a different gun is fitted.
            var own = ((CompProperties_CrowsAmmo)properties).Copy();
            own.fuelFilter = new ThingFilter();
            base.Initialize(own);
        }
        public void ConfigureWeapon()
        {
            var weapon = (parent as Building_Crows)?.InstalledWeapon;
            Props.fuelFilter.SetDisallowAll();
            Props.fuelCapacity = weapon?.fuelCapacity ?? 100;
            if (weapon != null) Props.fuelFilter.SetAllow(ThingDefOf.Steel, true);
        }
        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if ((parent as Building_Crows)?.HasWeapon != true) yield break;
            foreach (Gizmo gizmo in base.CompGetGizmosExtra()) yield return gizmo;
        }
        public override string CompInspectStringExtra() => (parent as Building_Crows)?.HasWeapon == true ? base.CompInspectStringExtra() : null;
    }
}
