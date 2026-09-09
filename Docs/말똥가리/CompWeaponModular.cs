using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace LGModularWeapons
{
    // Per-weapon-instance state: which part sits in which slot, plus cheap caches
    // so the hot-path stat hooks stay O(1).
    public class CompWeaponModular : ThingComp
    {
        // slot -> installed part (only non-empty slots are stored)
        private Dictionary<WeaponPartSlotDef, WeaponPartDef> installed =
            new Dictionary<WeaponPartSlotDef, WeaponPartDef>();

        // Rebuilt only on config change (dirty pattern). GetStatOffset/Factor just read these.
        private readonly Dictionary<StatDef, float> offsetCache = new Dictionary<StatDef, float>();
        private readonly Dictionary<StatDef, float> factorCache = new Dictionary<StatDef, float>();
        private HashSet<AbilityDef> resolvedAbilities;
        private bool abilitiesResolved;
        private List<AbilityDef> directlyGranted = new List<AbilityDef>();
        private List<Tool> resolvedExtraTools;
        private int lastToolSignature = -1;

        private EffecterDef resolvedMuzzleEffecter;
        private float resolvedMuzzleDistance;
        private float resolvedMuzzleScale = 1f;
        private bool muzzleFlashSuppressed;

        private int resolvedBurstCountOffset;
        private float resolvedBurstCountFactor = 1f;
        private float resolvedBurstSpeedFactor = 1f;
        private SoundDef resolvedSoundCast;
        private SoundDef resolvedSoundCastTail;
        private ThingDef resolvedProjectile;
        private int resolvedProjectilePriority;
        private int resolvedSoundPriority;

        // Combat Extended magazine capacity, resolved from whichever fitted part carries a
        // non-zero ceMagSizeOverride (normally just the magazine slot's part). 0 = no part
        // wants an override - see ApplyCEAmmoConfig.
        private int resolvedCEMagSize;
        private int resolvedCEMagSizePriority;

        // Combat Extended caliber (AmmoSetDef defName), resolved from whichever fitted part
        // carries a non-empty ceAmmoSetDefName. Null = no part wants a caliber conversion.
        private string resolvedCEAmmoSetDefName;
        private int resolvedCEAmmoSetPriority;

        // Lazily resolved - see the CEAmmoUserComp getter for why a "not found" result must
        // not be cached.
        private ThingComp ceAmmoUserComp;

        // The weapon's own, XML-authored CompProperties_AmmoUser, captured the first time we
        // see this comp - both so a caliber conversion has something correct to clone from,
        // and so removing every overriding part has something correct to revert to. Typed as
        // the vanilla Verse.CompProperties base class on purpose: that lets us hold and
        // reassign it without ever naming CombatExtended.CompProperties_AmmoUser, which this
        // project has no compile-time reference to.
        private CompProperties baseAmmoUserProps;

        // What is actually applied to the CE comp right now, compared against the freshly
        // resolved values every Recache so an unrelated part swap (a barrel, an optic...)
        // never touches the magazine. Deliberately NOT Scribed: CompProperties are never
        // saved by RimWorld either, so after a load these start blank and get silently
        // reconciled (no eject, see ApplyCEAmmoConfig) the first time Recache runs.
        private int appliedCEMagSize;
        private string appliedCEAmmoSetDefName;
        private bool ceAmmoConfigInitialized;

        // scratch lists for Scribe_Collections
        private List<WeaponPartSlotDef> scribeSlots;
        private List<WeaponPartDef> scribeParts;

        public CompProperties_WeaponModular Props => (CompProperties_WeaponModular)props;

        // Null unless a fitted part explicitly overrides the sound. The weapon's own
        // <soundCast> in XML is what plays otherwise - see Patch_Verb_TryCastNextBurstShot.
        // True when some fitted part unlocks this ability. Checked by the patch on
        // CompEquippableAbility.AbilityForReading.
        // False until the first Recache has run. CompEquippableAbility.Initialize dereferences
        // AbilityForReading, so the gate must not null it before the configuration is known -
        // that would throw during comp setup on load.
        public bool AbilitiesResolved => abilitiesResolved;

        public bool GrantsAbility(AbilityDef def)
        {
            return def != null && resolvedAbilities != null && resolvedAbilities.Contains(def);
        }

        // Two ways a part can grant an ability, chosen automatically by what the WEAPON def
        // declares:
        //
        //  * The weapon has a CompEquippableAbility(Reloadable) for this AbilityDef ->
        //    GATED mode. The comp stays in the def permanently (so the comp list, and with it
        //    save/load ordering, never changes) and the patch on AbilityForReading switches
        //    it on and off. This is the only mode that supports charges, ammo and reloading,
        //    because that machinery is driven by IReloadableComp on the weapon Thing.
        //
        //  * No such comp -> DIRECT mode. The ability is simply added to the pawn while the
        //    weapon is equipped and the part is fitted. No comp on the weapon at all, but
        //    also no charges or reload job - use it for cooldown-only abilities.
        private bool AbilityHandledByComp(AbilityDef def)
        {
            List<ThingComp> comps = parent.AllComps;
            for (int i = 0; i < comps.Count; i++)
            {
                CompEquippableAbility ea = comps[i] as CompEquippableAbility;
                if (ea == null) continue;

                CompProperties_EquippableAbility p = ea.props as CompProperties_EquippableAbility;
                if (p != null && p.abilityDef == def) return true;
            }
            return false;
        }

        private Pawn HolderPawn => (parent.ParentHolder as Pawn_EquipmentTracker)?.pawn;

        // Brings the pawn's ability list in line with the current configuration. Idempotent,
        // so it is safe to call on equip, on load and after every part change.
        private void SyncDirectAbilities()
        {
            Pawn holder = HolderPawn;

            if (holder?.abilities == null)
            {
                // Nothing to sync against; the list is re-applied when it is next equipped.
                directlyGranted.Clear();
                return;
            }

            var wanted = new List<AbilityDef>();
            if (resolvedAbilities != null)
            {
                foreach (AbilityDef def in resolvedAbilities)
                    if (!AbilityHandledByComp(def)) wanted.Add(def);
            }

            // Revoke what this weapon granted and no longer should.
            for (int i = directlyGranted.Count - 1; i >= 0; i--)
            {
                AbilityDef def = directlyGranted[i];
                if (wanted.Contains(def)) continue;
                holder.abilities.RemoveAbility(def);
                directlyGranted.RemoveAt(i);
            }

            foreach (AbilityDef def in wanted)
            {
                if (directlyGranted.Contains(def)) continue;
                holder.abilities.GainAbility(def);
                directlyGranted.Add(def);
            }
        }

        public override void Notify_Equipped(Pawn pawn)
        {
            base.Notify_Equipped(pawn);
            SyncDirectAbilities();
        }

        public override void Notify_Unequipped(Pawn pawn)
        {
            base.Notify_Unequipped(pawn);

            // The ability belongs to the weapon, not the pawn - strip it on the way out.
            if (pawn?.abilities != null)
                foreach (AbilityDef def in directlyGranted)
                    pawn.abilities.RemoveAbility(def);

            directlyGranted.Clear();
        }

        // Extra melee tools contributed by fitted parts; null when there are none.
        public List<Tool> ExtraTools => resolvedExtraTools;

        // def.tools + part tools, built once per configuration change. The Tools getter is
        // hit constantly, so it must not allocate a new list on every call.
        private List<Tool> cachedCombinedTools;

        public List<Tool> CombinedTools => cachedCombinedTools;

        public EffecterDef MuzzleFlashEffecter =>
            muzzleFlashSuppressed ? null : resolvedMuzzleEffecter;

        // Scaled with the body so a shrunken weapon's flash sits at the shorter barrel tip.
        public float MuzzleFlashDistance => resolvedMuzzleDistance * BodyScaleFactor;

        public float MuzzleFlashScale => resolvedMuzzleScale * BodyScaleFactor;

        public int BurstCountOffset => resolvedBurstCountOffset;
        public float BurstCountFactor => resolvedBurstCountFactor;
        public float BurstSpeedFactor => resolvedBurstSpeedFactor;

        public SoundDef SoundCastOverride => resolvedSoundCast;
        public SoundDef SoundCastTailOverride => resolvedSoundCastTail;
        public ThingDef ProjectileOverride => resolvedProjectile;

        // What the currently fitted magazine part says CE's magazine capacity should be.
        // 0 = no override (CE falls back to the weapon's own base magazineSize).
        public int ResolvedCEMagSize => resolvedCEMagSize;

        // What the currently fitted magazine part says CE's AmmoSetDef (caliber) should be.
        // Null = no override (CE falls back to the weapon's own base ammoSet).
        public string ResolvedCEAmmoSetDefName => resolvedCEAmmoSetDefName;

        // ---- Combat Extended soft-compat ----
        // LGModularWeapons has no compile-time reference to CombatExtended.dll - CE may not
        // even be installed alongside it. Detected purely by comp type name so this keeps
        // working regardless of CE's own assembly/version, and a game without CE pays no cost
        // beyond one lookup (that finds nothing) per weapon instance.
        //
        // Deliberately does NOT cache a "not found" result: ThingWithComps.InitializeComps()
        // creates comps one at a time and calls Initialize() on each IMMEDIATELY (not
        // "create all, then Initialize all"), so if CompProperties_WeaponModular is listed
        // before CompProperties_AmmoUser in the ThingDef's <comps> - which it is here - this
        // getter's very first call (from our own Initialize()) runs before CompAmmoUser even
        // exists yet. Caching that as a permanent "no CE on this weapon" would silently
        // disable every CE hook below for the weapon's entire lifetime. Once actually found,
        // the result is permanent (a Thing's comp list never changes after construction), so
        // there is no repeated-scan cost once past that early window.
        private ThingComp CEAmmoUserComp
        {
            get
            {
                if (ceAmmoUserComp == null)
                {
                    List<ThingComp> comps = parent.AllComps;
                    for (int i = 0; i < comps.Count; i++)
                    {
                        if (comps[i].GetType().FullName == "CombatExtended.CompAmmoUser")
                        {
                            ceAmmoUserComp = comps[i];
                            break;
                        }
                    }
                }
                return ceAmmoUserComp;
            }
        }

        // True while this weapon's ammo/caliber is governed by Combat Extended's own
        // CompAmmoUser. While true, CanFitPart refuses any part with a projectileOverride
        // UNLESS that part also sets ceAmmoSetDefName: CE already has its own FMJ/AP/JHP/
        // Tracer AmmoDefs per caliber, selected through its own reload UI from what is
        // actually loaded/carried, and Legion's vanilla projectileOverride mechanism cannot
        // participate in that at all (Verb_ShootCE overrides the vanilla Projectile getter
        // outright, reading CompAmmoUser.CurrentAmmo instead) - so a part with no CE-side
        // caliber target would just be silently inert. A part that DOES set
        // ceAmmoSetDefName is the one case where the conversion is real under CE too, see
        // ApplyCEAmmoConfig.
        public bool CEGovernsAmmo => CEAmmoUserComp != null;

        // ---- Combat Extended: magazine capacity + caliber, applied together ----
        // CE has no per-instance "override" field for either of these in this CE version -
        // MagSize and CurAmmoSet both read straight off CompAmmoUser.Props (a
        // CompProperties_AmmoUser shared by every instance of the ThingDef), and
        // MagSizeOverride turned out to be an unrelated, read-only stat (ammo GENERATION
        // count for loot/pawn generation, not firing capacity - do not confuse the two).
        // So both are pushed by cloning CompAmmoUser.props and swapping the clone in, exactly
        // mirroring what CE's own CompAmmosetSwitcher does when it flips a weapon to its
        // underbarrel and back (CompAmmo.props = ...).
        //
        // Subtlety: CE's own MagazineCapacity StatWorker (StatWorker_Magazine.
        // GetValueUnfinalized) only reads the swapped Props.magazineSize when Props.ammoSet
        // is a DIFFERENT reference from the weapon's original, unmodified ammoSet -
        // otherwise it silently falls back to the original ThingDef's authored value
        // regardless of what our instance Props say. So even a same-caliber capacity change
        // (no ceAmmoSetDefName involved at all) still clones the current ammoSet object
        // (same content, fresh reference) purely to satisfy that check; reload behaviour is
        // unaffected since the ammoTypes list inside is the exact same list, just reached
        // through a different wrapper object.
        private void ApplyCEAmmoConfig()
        {
            ThingComp ammoUser = CEAmmoUserComp;
            if (ammoUser == null) return;

            if (baseAmmoUserProps == null) baseAmmoUserProps = ammoUser.props;

            int targetMagSize = resolvedCEMagSize;
            string targetAmmoSetName = resolvedCEAmmoSetDefName;

            bool firstRun = !ceAmmoConfigInitialized;
            bool changed = firstRun || targetMagSize != appliedCEMagSize
                || targetAmmoSetName != appliedCEAmmoSetDefName;
            if (!changed) return;

            // A load reconciles silently. CurMagCount/CurrentAmmo were saved already
            // consistent with this configuration, and Props are never Scribed by RimWorld -
            // they always come back as the weapon's raw XML defaults right after a load, so
            // this method looks "changed" the moment Recache first runs post-load even
            // though nothing the player actually did changed. Only a LIVE part swap (Scribe
            // inactive) should eject anything or touch the current ammo selection.
            bool liveChange = !firstRun && Scribe.mode == LoadSaveMode.Inactive;

            if (liveChange)
            {
                // Mirrors CE's own CompAmmosetSwitcher.SwitchToUB/SwithToB: force-eject
                // whatever is chambered BEFORE the capacity/caliber underneath it changes,
                // so rounds that no longer fit (a bigger-to-smaller magazine) or no longer
                // belong (a caliber conversion) never ride along silently.
                Traverse.Create(ammoUser)
                    .Method("TryUnload", new[] { typeof(bool) }, new object[] { true })
                    .GetValue();
            }

            bool wantsCaliberChange = !string.IsNullOrEmpty(targetAmmoSetName);
            object newAmmoSetObj = null;

            if (targetMagSize <= 0 && !wantsCaliberChange)
            {
                ammoUser.props = baseAmmoUserProps;
            }
            else
            {
                if (wantsCaliberChange)
                {
                    newAmmoSetObj = LookupAmmoSetDef(targetAmmoSetName);
                    if (newAmmoSetObj == null)
                    {
                        Log.Warning("[LGModularWeapons] '" + parent.def.defName + "' has a "
                            + "fitted part requesting CE AmmoSetDef '" + targetAmmoSetName
                            + "', which does not exist - ignoring the caliber conversion.");
                        targetAmmoSetName = null;
                        wantsCaliberChange = false;
                    }
                }

                if (!wantsCaliberChange)
                {
                    object baseAmmoSet = Traverse.Create(baseAmmoUserProps).Field("ammoSet").GetValue();
                    newAmmoSetObj = ShallowClone(baseAmmoSet);
                }

                object clonedProps = ShallowClone(baseAmmoUserProps);
                Traverse.Create(clonedProps).Field("ammoSet").SetValue(newAmmoSetObj);
                if (targetMagSize > 0)
                    Traverse.Create(clonedProps).Field("magazineSize").SetValue(targetMagSize);

                ammoUser.props = (CompProperties)clonedProps;

                // Only a LIVE caliber change forces a fresh default ammo selection - the old
                // CurrentAmmo/SelectedAmmo belonged to a different AmmoSetDef entirely and
                // TryUnload already cleared the magazine above. A post-load reconciliation
                // must NOT touch this, or whatever ammo the pawn actually had chambered/
                // selected would get silently reset to the ammoSet's default on every load.
                if (liveChange && wantsCaliberChange)
                {
                    object firstAmmo = FirstAmmoDefOf(newAmmoSetObj);
                    if (firstAmmo != null)
                    {
                        Traverse.Create(ammoUser).Property("CurrentAmmo").SetValue(firstAmmo);
                        Traverse.Create(ammoUser).Property("SelectedAmmo").SetValue(firstAmmo);
                    }
                }
            }

            // MagazineCapacity is marked <cacheable>true</cacheable> - without this the new
            // value would only show up once that cache naturally expires (~4 seconds).
            ClearCEMagazineCapacityCache();

            appliedCEMagSize = targetMagSize;
            appliedCEAmmoSetDefName = targetAmmoSetName;
            ceAmmoConfigInitialized = true;
        }

        private static readonly Dictionary<string, object> ammoSetLookupCache =
            new Dictionary<string, object>();
        private static Type ammoSetDefType;
        private static bool ammoSetTypeSearched;

        // Resolved once per defName and cached: a null result (CE not installed, or a typo'd
        // defName) is cached too, so a missing AmmoSetDef only logs/looks up once, not every
        // Recache.
        private static object LookupAmmoSetDef(string defName)
        {
            if (string.IsNullOrEmpty(defName)) return null;
            if (ammoSetLookupCache.TryGetValue(defName, out object cached)) return cached;

            if (!ammoSetTypeSearched)
            {
                ammoSetTypeSearched = true;
                ammoSetDefType = GenTypes.GetTypeInAnyAssembly("CombatExtended.AmmoSetDef");
            }

            object result = null;
            if (ammoSetDefType != null)
            {
                Type dbType = typeof(DefDatabase<>).MakeGenericType(ammoSetDefType);
                MethodInfo getNamed = dbType.GetMethod("GetNamedSilentFail",
                    BindingFlags.Public | BindingFlags.Static);
                result = getNamed?.Invoke(null, new object[] { defName });
            }

            ammoSetLookupCache[defName] = result;
            return result;
        }

        // First AmmoDef in an AmmoSetDef.ammoTypes (a List<AmmoLink>, walked non-generically
        // since AmmoLink is CE-internal too) - a sane default to load when a live caliber
        // conversion needs to pick SOMETHING to start with.
        private static object FirstAmmoDefOf(object ammoSetDefObj)
        {
            if (ammoSetDefObj == null) return null;
            object ammoTypes = Traverse.Create(ammoSetDefObj).Field("ammoTypes").GetValue();
            if (ammoTypes is IEnumerable enumerable)
            {
                foreach (object link in enumerable)
                    return Traverse.Create(link).Field("ammo").GetValue();
            }
            return null;
        }

        private static object ShallowClone(object source)
        {
            if (source == null) return null;
            MethodInfo cloneMethod = typeof(object).GetMethod("MemberwiseClone",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return cloneMethod.Invoke(source, null);
        }

        private static StatDef ceMagazineCapacityStat;
        private static bool ceMagazineCapacityStatSearched;

        private void ClearCEMagazineCapacityCache()
        {
            if (!ceMagazineCapacityStatSearched)
            {
                ceMagazineCapacityStatSearched = true;
                ceMagazineCapacityStat = DefDatabase<StatDef>.GetNamedSilentFail("MagazineCapacity");
            }
            ceMagazineCapacityStat?.Worker?.ClearCacheForThing(parent);
        }

        public WeaponPartDef PartInSlot(WeaponPartSlotDef slot)
        {
            if (slot == null) return null;
            return installed.TryGetValue(slot, out var part) ? part : slot.defaultPart;
        }

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            ApplyDefaultsIfEmpty();
            Recache();
        }

        // ThingWithComps.PostMake() calls InitializeComps() - which constructs every comp and
        // calls each one's Initialize() IMMEDIATELY, one at a time, in <comps> list order -
        // and only once that whole loop is done does it call PostPostMake() on every comp in
        // a second pass. CompProperties_WeaponModular is listed before CE's
        // CompProperties_AmmoUser on every weapon here, so during our own Initialize() call
        // above, CompAmmoUser does not exist in parent.AllComps yet: CEAmmoUserComp resolves
        // to null and ApplyCEAmmoConfig (inside that first Recache()) silently no-ops,
        // leaving a freshly made weapon on CE's raw, unmodified magazineSize/ammoSet
        // regardless of which magazine part is actually fitted. By PostPostMake() every comp
        // - CompAmmoUser included - is guaranteed to exist AND have already run its own
        // Initialize(), so recaching here is both possible and safe.
        public override void PostPostMake()
        {
            base.PostPostMake();
            Recache();
        }

        private void ApplyDefaultsIfEmpty()
        {
            if (Props.defaultParts.NullOrEmpty()) return;
            foreach (var part in Props.defaultParts)
            {
                if (part?.slot == null) continue;
                if (!installed.ContainsKey(part.slot))
                    installed[part.slot] = part;
            }
        }

        // ---- dynamic slots ----
        // Base slots come from the weapon. Installed parts may grant more via
        // WeaponPartDef.addsSlots, and those granted slots can hold parts that grant
        // still more, so expansion runs until it stops changing.
        private List<WeaponPartSlotDef> activeSlots;

        public List<WeaponPartSlotDef> ActiveSlots => activeSlots ?? Props.slots;

        private const int MaxSlotExpansionPasses = 8;

        public List<WeaponPartSlotDef> ComputeActiveSlots(
            Dictionary<WeaponPartSlotDef, WeaponPartDef> config)
        {
            var result = new List<WeaponPartSlotDef>(Props.slots);

            for (int pass = 0; pass < MaxSlotExpansionPasses; pass++)
            {
                bool changed = false;

                for (int i = 0; i < result.Count; i++)
                {
                    WeaponPartSlotDef slot = result[i];

                    WeaponPartDef part = null;
                    if (config != null) config.TryGetValue(slot, out part);
                    if (part == null) part = slot.defaultPart;
                    if (part == null || part.addsSlots.NullOrEmpty()) continue;

                    foreach (var granted in part.addsSlots)
                    {
                        // Contains() guards against a cycle (A grants B, B grants A).
                        if (granted == null || result.Contains(granted)) continue;
                        result.Add(granted);
                        changed = true;
                    }
                }

                if (!changed) break;
            }
            return result;
        }

        // Drops entries whose slot is no longer available - i.e. cascade-removes the
        // foregrip when the handguard that exposed its slot is taken off.
        public static bool PruneOrphans(
            Dictionary<WeaponPartSlotDef, WeaponPartDef> config, List<WeaponPartSlotDef> active)
        {
            if (config == null || config.Count == 0) return false;

            List<WeaponPartSlotDef> orphans = null;
            foreach (var kv in config)
            {
                if (!active.Contains(kv.Key))
                {
                    if (orphans == null) orphans = new List<WeaponPartSlotDef>();
                    orphans.Add(kv.Key);
                }
            }
            if (orphans == null) return false;

            foreach (var slot in orphans) config.Remove(slot);
            return true;
        }

        // Which slots does this part bring with it, given the rest of the configuration?
        public List<WeaponPartSlotDef> SlotsGrantedBy(WeaponPartDef part)
        {
            if (part == null || part.addsSlots.NullOrEmpty()) return null;
            return part.addsSlots;
        }

        public bool CanInstall(WeaponPartDef part, out string reason)
        {
            return CanFitPart(part, installed, ActiveSlots, out reason);
        }

        // ---- compatibility rules ----
        // One place defines what may sit alongside what; the customization window, save
        // loading and AI weapon generation all validate through here so they can't drift.
        public bool CanFitPart(WeaponPartDef part,
            Dictionary<WeaponPartSlotDef, WeaponPartDef> config,
            List<WeaponPartSlotDef> slotSet, out string reason)
        {
            reason = null;
            if (part == null || part.slot == null) { reason = "LGMW_InvalidPart".Translate(); return false; }
            if (!slotSet.Contains(part.slot)) { reason = "LGMW_SlotNotOnWeapon".Translate(); return false; }
            if (!part.AppliesTo(parent.def)) { reason = "LGMW_Incompatible".Translate(); return false; }
            if (!part.ResearchDone) { reason = "LGMW_ResearchMissing".Translate(); return false; }

            // --- Combat Extended: ammo-type-only parts (no CE caliber target) are refused
            //     outright once CE governs this weapon's ammo. CE already owns ammo-type
            //     switching (its own FMJ/AP/JHP/Tracer AmmoDefs, chosen through its own reload
            //     UI from what is actually carried) - Legion's vanilla projectileOverride
            //     mechanism cannot participate in that at all (Verb_ShootCE overrides the
            //     vanilla Projectile getter outright and never calls into it), so a part like
            //     this would silently do nothing while claiming to convert the weapon.
            //     A part that ALSO sets ceAmmoSetDefName is exempt - that is the one case
            //     where the conversion is real and CE-recognized, see ApplyCEAmmoConfig.
            //     Magazine-CAPACITY-only parts (ceMagSizeOverride, no projectileOverride at
            //     all) are unaffected either way. ---
            if (part.projectileOverride != null && string.IsNullOrEmpty(part.ceAmmoSetDefName)
                && CEGovernsAmmo)
            {
                reason = "이 무기는 Combat Extended 탄약 시스템이 적용되어 있어 구경/탄종을 바꾸는 부품을 장착할 수 없습니다. 탄약 종류는 CE 자체 재장전 UI에서 고르세요.";
                return false;
            }

            // --- conflicts: checked against every OTHER fitted part. The part occupying
            //     this same slot is ignored, since fitting replaces it. ---
            foreach (var slot in slotSet)
            {
                if (slot == part.slot) continue;
                WeaponPartDef other = ResolveFrom(config, slot);
                if (other == null || other == part) continue;

                if (PartsConflict(part, other))
                {
                    reason = "LGMW_ConflictsWith".Translate(other.LabelCap);
                    return false;
                }
            }

            // --- combination conflicts: every member of the group installed at once ---
            if (!part.conflictingCombinations.NullOrEmpty())
            {
                foreach (PartCombination combo in part.conflictingCombinations)
                {
                    if (combo == null || combo.IsEmpty) continue;

                    bool allPresent = true;
                    foreach (WeaponPartDef member in combo.parts)
                    {
                        if (member == null) continue;
                        if (!ConfigHasPart(config, slotSet, member, part.slot))
                        {
                            allPresent = false;
                            break;
                        }
                    }

                    if (allPresent)
                    {
                        reason = "LGMW_ConflictsWithCombo".Translate(combo.Describe());
                        return false;
                    }
                }
            }

            // --- prerequisites ---
            if (!part.requiresParts.NullOrEmpty())
            {
                foreach (var req in part.requiresParts)
                {
                    if (!ConfigHasPart(config, slotSet, req, part.slot))
                    {
                        reason = "LGMW_RequiresPart".Translate(req.LabelCap);
                        return false;
                    }
                }
            }

            if (!part.requiresAnyOf.NullOrEmpty())
            {
                bool any = false;
                foreach (var req in part.requiresAnyOf)
                {
                    if (ConfigHasPart(config, slotSet, req, part.slot)) { any = true; break; }
                }
                if (!any)
                {
                    reason = "LGMW_RequiresAnyOf".Translate(
                        string.Join(", ", part.requiresAnyOf.Select(r => r.LabelCap.ToString()).ToArray()));
                    return false;
                }
            }

            if (!part.requiresTags.NullOrEmpty())
            {
                foreach (var tag in part.requiresTags)
                {
                    if (!ConfigHasTag(config, slotSet, tag, part.slot))
                    {
                        reason = "LGMW_RequiresTag".Translate(tag);
                        return false;
                    }
                }
            }

            return true;
        }

        // Conflicts are symmetric: declaring it on either side is enough.
        public static bool PartsConflict(WeaponPartDef a, WeaponPartDef b)
        {
            if (a == null || b == null || a == b) return false;

            if (!a.conflictsWith.NullOrEmpty() && a.conflictsWith.Contains(b)) return true;
            if (!b.conflictsWith.NullOrEmpty() && b.conflictsWith.Contains(a)) return true;

            if (!a.conflictTags.NullOrEmpty())
                foreach (var t in a.conflictTags)
                    if (b.HasTag(t)) return true;

            if (!b.conflictTags.NullOrEmpty())
                foreach (var t in b.conflictTags)
                    if (a.HasTag(t)) return true;

            return false;
        }

        private static WeaponPartDef ResolveFrom(
            Dictionary<WeaponPartSlotDef, WeaponPartDef> config, WeaponPartSlotDef slot)
        {
            WeaponPartDef p = null;
            if (config != null) config.TryGetValue(slot, out p);
            if (p == null) p = slot.defaultPart;
            return p;
        }

        private static bool ConfigHasPart(Dictionary<WeaponPartSlotDef, WeaponPartDef> config,
            List<WeaponPartSlotDef> slotSet, WeaponPartDef target, WeaponPartSlotDef ignoreSlot)
        {
            if (target == null) return true;
            foreach (var slot in slotSet)
            {
                if (slot == ignoreSlot) continue;
                if (ResolveFrom(config, slot) == target) return true;
            }
            return false;
        }

        private static bool ConfigHasTag(Dictionary<WeaponPartSlotDef, WeaponPartDef> config,
            List<WeaponPartSlotDef> slotSet, string tag, WeaponPartSlotDef ignoreSlot)
        {
            foreach (var slot in slotSet)
            {
                if (slot == ignoreSlot) continue;
                WeaponPartDef p = ResolveFrom(config, slot);
                if (p != null && p.HasTag(tag)) return true;
            }
            return false;
        }

        // Removes parts whose requirements stopped being met - e.g. the suppressor that
        // needed a threaded barrel, after that barrel was swapped out. Repeats because one
        // removal can invalidate another.
        public bool PruneInvalid(Dictionary<WeaponPartSlotDef, WeaponPartDef> config,
            List<WeaponPartSlotDef> slotSet)
        {
            if (config == null || config.Count == 0) return false;

            bool removedAny = false;
            for (int pass = 0; pass < MaxSlotExpansionPasses; pass++)
            {
                WeaponPartSlotDef bad = null;
                foreach (var kv in config)
                {
                    if (kv.Value == null) continue;
                    if (!slotSet.Contains(kv.Key)) continue;
                    string ignored;
                    if (!CanFitPart(kv.Value, config, slotSet, out ignored)) { bad = kv.Key; break; }
                }
                if (bad == null) break;
                config.Remove(bad);
                removedAny = true;
            }
            return removedAny;
        }

        // Fit or clear a slot. Resource cost is handled by the caller (window / bench).
        // Diagnostics: incremented by the render hooks so the dev dump can prove whether
        // they are firing at all.
        public static int GroundDrawCalls;
        public static int GroundPrintCalls;

        // Altitude granularity per layer step. Positive layer = above the weapon,
        // negative = below it.
        public const float LayerAltitudeStep = 0.0042f;
        public static int HeldDrawCalls;
        public static int HeldPatchCalls;

        // Applies a whole configuration in ONE step.
        //
        // Applying slot-by-slot was wrong: each SetPart triggered a full Recache, so
        // validation ran against a half-applied config and pruned parts whose prerequisites
        // hadn't been installed yet. Swap the dictionary first, validate once.
        public void SetConfiguration(Dictionary<WeaponPartSlotDef, WeaponPartDef> config)
        {
            // Dev tuning was authored against the previous configuration; keeping it would
            // hold the old parts at offsets that no longer match the new barrel/handguard.
            tuningOverrides = null;

            installed.Clear();
            if (config != null)
            {
                foreach (var kv in config)
                {
                    if (kv.Key == null || kv.Value == null) continue;
                    installed[kv.Key] = kv.Value;
                }
            }
            Recache();
        }

        public string DebugStateReport()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("[LGModularWeapons] " + parent.LabelCap + " (" + parent.def.defName + ")");
            sb.AppendLine("  spawned=" + parent.Spawned + "  holder=" + (parent.ParentHolder?.ToString() ?? "none"));
            sb.AppendLine("  Props.slots=" + Props.slots.Count + "  ActiveSlots=" + ActiveSlots.Count);

            // Body-scale linkage: if the factor reads 1.00 while you expected parts to shrink,
            // partScaleReference is missing from this weapon's CompProperties_WeaponModular.
            GraphicData gd = parent.def.graphicData;
            string drawSizeStr = gd != null ? gd.drawSize.ToString() : "(no graphicData)";
            sb.AppendLine("  weapon drawSize=" + drawSizeStr
                        + "  partScaleReference=" + Props.partScaleReference.ToString("0.###")
                        + "  -> part scale factor=" + BodyScaleFactor.ToString("0.###"));
            if (Props.partScaleReference <= 0.01f)
                sb.AppendLine("    (partScaleReference is 0/unset -> parts do NOT scale with "
                            + "the weapon body; add it to CompProperties_WeaponModular)");
            sb.AppendLine("  installed entries=" + installed.Count);
            if (tuningOverrides != null && tuningOverrides.Count > 0)
                sb.AppendLine("  DEV TUNING ACTIVE for " + tuningOverrides.Count
                            + " part(s) - these override all resolved offsets");

            foreach (var slot in ActiveSlots)
            {
                var part = PartInSlot(slot);
                sb.Append("   - " + (slot != null ? slot.defName : "NULL SLOT") + ": ");
                if (part == null) { sb.AppendLine("(empty)"); continue; }

                sb.Append(part.defName);
                if (part.graphicData == null) sb.Append("  [no graphicData -> nothing to draw]");
                else if (part.Graphic == null) sb.Append("  [graphicData set but Graphic is NULL]");
                else sb.Append("  [graphic OK: " + part.graphicData.texPath + "]");

                PartDrawData d = ResolveDrawData(part);
                sb.Append("  offset=" + d.offset + " scale=" + d.scale.ToString("0.##")
                        + " layer=" + d.layer.ToString("0.##")
                        + (d.layer < 0f ? " (UNDER gun)" : " (over gun)"));
                sb.AppendLine();
            }

            sb.AppendLine("  render hooks: graphicPrint=" + GroundPrintCalls
                        + "  DrawAt(carried)=" + GroundDrawCalls
                        + "  heldPatchEntered=" + HeldPatchCalls
                        + "  heldOverlayDrawn=" + HeldDrawCalls
                        + "  (0 means the hook never ran)");
            return sb.ToString();
        }

        public void SetPart(WeaponPartSlotDef slot, WeaponPartDef part)
        {
            if (slot == null) return;
            tuningOverrides = null;   // see SetConfiguration
            if (part == null) installed.Remove(slot);
            else installed[slot] = part;
            Recache();
        }

        // Stat contribution of an ARBITRARY configuration, without touching this weapon.
        // The customization window uses it to show what a pending build would do.
        public void ComputeModifiers(
            Dictionary<WeaponPartSlotDef, WeaponPartDef> config, List<WeaponPartSlotDef> slotSet,
            Dictionary<StatDef, float> offsets, Dictionary<StatDef, float> factors,
            out int burstOffset, out float burstFactor, out float burstSpeed)
        {
            offsets.Clear();
            factors.Clear();
            burstOffset = 0;
            burstFactor = 1f;
            burstSpeed = 1f;

            foreach (var slot in slotSet)
            {
                WeaponPartDef part = ResolveFrom(config, slot);
                if (part == null) continue;

                if (!part.statOffsets.NullOrEmpty())
                    foreach (var m in part.statOffsets)
                        offsets[m.stat] = (offsets.TryGetValue(m.stat, out var o) ? o : 0f) + m.value;

                if (!part.statFactors.NullOrEmpty())
                    foreach (var m in part.statFactors)
                        factors[m.stat] = (factors.TryGetValue(m.stat, out var f) ? f : 1f) * m.value;

                burstOffset += part.burstShotCountOffset;
                burstFactor *= part.burstShotCountMultiplier;
                burstSpeed *= part.burstShotSpeedMultiplier;
            }
        }

        // What this weapon currently contributes, for the "before" side of the comparison.
        public void CurrentModifiers(
            Dictionary<StatDef, float> offsets, Dictionary<StatDef, float> factors,
            out int burstOffset, out float burstFactor, out float burstSpeed)
        {
            ComputeModifiers(installed, ActiveSlots, offsets, factors,
                out burstOffset, out burstFactor, out burstSpeed);
        }

        // ---- hot-path stat hooks: StatWorker calls these on every stat calculation,
        //      for every comp on the thing. Keep them to a single dictionary lookup. ----
        public override float GetStatOffset(StatDef stat)
            => offsetCache.TryGetValue(stat, out var v) ? v : 0f;

        public override float GetStatFactor(StatDef stat)
            => factorCache.TryGetValue(stat, out var v) ? v : 1f;

        private void Recache()
        {
            // Slot list is dynamic: parts can open further slots (a RIS handguard exposing
            // rail slots, for example), so resolve it before anything reads it.
            activeSlots = ComputeActiveSlots(installed);
            for (int pass = 0; pass < MaxSlotExpansionPasses; pass++)
            {
                bool changed = PruneOrphans(installed, activeSlots);
                changed |= PruneInvalid(installed, activeSlots);
                if (!changed) break;
                activeSlots = ComputeActiveSlots(installed);
            }

            offsetCache.Clear();
            factorCache.Clear();
            resolvedExtraTools = null;
            resolvedAbilities = null;
            abilitiesResolved = true;

            resolvedMuzzleEffecter = Props.muzzleFlashEffecter;
            resolvedMuzzleDistance = Props.muzzleFlashDistance;
            resolvedMuzzleScale = Props.muzzleFlashScale;
            muzzleFlashSuppressed = false;

            resolvedBurstCountOffset = 0;
            resolvedBurstCountFactor = 1f;
            resolvedBurstSpeedFactor = 1f;

            // Start empty: no part fitted means the weapon keeps its authored XML sound.
            resolvedSoundCast = null;
            resolvedSoundCastTail = null;
            resolvedProjectile = null;
            resolvedProjectilePriority = int.MinValue;
            resolvedSoundPriority = int.MinValue;

            resolvedCEMagSize = 0;
            resolvedCEMagSizePriority = int.MinValue;
            resolvedCEAmmoSetDefName = null;
            resolvedCEAmmoSetPriority = int.MinValue;

            foreach (var slot in ActiveSlots)
            {
                var part = PartInSlot(slot);
                if (part == null) continue;
                Accumulate(part);
            }

            // Independent of drawing/verbs below - this pushes straight into CE's own comp,
            // there is no local cache to dirty here (ApplyCEAmmoConfig clears CE's own
            // MagazineCapacity stat cache itself).
            ApplyCEAmmoConfig();

            // Only flag it: the actual build needs the main thread (see FittedPartsForDrawing).
            drawCacheDirty = true;

            // Rebuilding verbs is expensive and disruptive (it recreates Verb objects), so
            // only do it when the tool set genuinely changed.
            int toolSig = ComputeToolSignature();
            RebuildCombinedTools();
            if (toolSig != lastToolSignature)
            {
                lastToolSignature = toolSig;
                RebuildVerbs();
            }

            NotifyAbilitiesChanged();
            DirtyOverlays();
        }

        // An ability appearing or disappearing has to reach the pawn's gizmo bar, which is
        // cached until something tells it otherwise.
        private void NotifyAbilitiesChanged()
        {
            SyncDirectAbilities();
            InitializeGatedAbilityCharges();
            HolderPawn?.abilities?.Notify_TemporaryAbilitiesChanged();
        }

        // CompEquippableAbilityReloadable sets up its charges in Notify_PropsChanged, called
        // from PostPostMake:
        //
        //     AbilityForReading.maxCharges = MaxCharges;
        //     RemainingCharges = MaxCharges;
        //
        // If the gate had the ability nulled at that moment - the launcher part was not
        // fitted yet - that whole block is skipped, so maxCharges stays 0. Ability.UsesCharges
        // is then false and the grenade fires forever without consuming anything. Once the
        // part unlocks the ability we have to run that setup ourselves.
        //
        // Guarded on maxCharges being wrong so this never silently refills a partly-used
        // launcher on an unrelated part swap.
        private void InitializeGatedAbilityCharges()
        {
            Pawn holder = HolderPawn;

            List<ThingComp> comps = parent.AllComps;
            for (int i = 0; i < comps.Count; i++)
            {
                CompEquippableAbility abilityComp = comps[i] as CompEquippableAbility;
                if (abilityComp == null) continue;

                CompProperties_EquippableAbility p =
                    abilityComp.props as CompProperties_EquippableAbility;
                if (p?.abilityDef == null || !GrantsAbility(p.abilityDef)) continue;

                Ability ability = abilityComp.AbilityForReading;
                if (ability == null) continue;

                // CRITICAL: wire the ability to its wielder.
                //
                // CompEquippableAbility.Notify_Equipped only does this when AbilityForReading
                // is non-null AT THAT MOMENT:
                //
                //     if (AbilityForReading != null) { AbilityForReading.pawn = pawn; ... }
                //
                // AI weapon generation equips the weapon FIRST and fits parts afterwards, so
                // the gate was still closed during Notify_Equipped and the ability never got
                // its pawn. It then unlocked with pawn == null and threw every tick from
                // Ability.Casting. Doing it here covers any order of events.
                if (holder != null && ability.pawn != holder)
                {
                    ability.pawn = holder;
                    if (ability.verb != null) ability.verb.caster = holder;
                }

                CompEquippableAbilityReloadable reloadable =
                    abilityComp as CompEquippableAbilityReloadable;
                if (reloadable == null) continue;

                // Charges are set up in Notify_PropsChanged, which PostPostMake skipped for
                // the same reason - the gate was shut. Without it maxCharges stays 0 and the
                // ability fires forever.
                if (ability.maxCharges != reloadable.MaxCharges)
                    reloadable.Notify_PropsChanged();
            }
        }

        // Fitting or removing a part that carries <tools> changes the weapon's melee verbs,
        // and VerbTracker caches them - so force a rebuild.
        //
        // CRITICAL: InitVerbsFromZero() discards the existing Verb objects, and freshly
        // created verbs have caster == null. VerbProperties.AdjustedRange dereferences the
        // caster (attacker.MapHeld), so leaving it null makes every ranged check throw the
        // moment the pawn is drafted. Re-attach the holder exactly like equipping does.
        private int ComputeToolSignature()
        {
            if (resolvedExtraTools == null) return 0;
            int sig = resolvedExtraTools.Count;
            for (int i = 0; i < resolvedExtraTools.Count; i++)
                sig = sig * 31 + (resolvedExtraTools[i]?.GetHashCode() ?? 0);
            return sig;
        }

        private void RebuildCombinedTools()
        {
            if (resolvedExtraTools == null || resolvedExtraTools.Count == 0)
            {
                cachedCombinedTools = null;   // patch falls through to def.tools
                return;
            }

            // Never mutate def.tools - it is shared by every copy of this weapon.
            var combined = new List<Tool>();
            if (parent.def.tools != null) combined.AddRange(parent.def.tools);
            combined.AddRange(resolvedExtraTools);
            cachedCombinedTools = combined;
        }

        private void RebuildVerbs()
        {
            // NEVER during save loading. CompEquippable.PostExposeData restores the saved
            // verbTracker, and its verbs get registered in the loaded-object directory by
            // ID (Verb_CompEquippable_<thingId>_<n>). Calling InitVerbsFromZero here builds a
            // second set with those same IDs, which is the "Id already used by ..." error.
            // The saved tracker was created with the same tools anyway, so it is already
            // correct - there is nothing to rebuild.
            if (Scribe.mode != LoadSaveMode.Inactive) return;

            CompEquippable eq = parent.GetComp<CompEquippable>();
            if (eq == null || eq.verbTracker == null) return;

            Pawn holder = (parent.ParentHolder as Pawn_EquipmentTracker)?.pawn;

            eq.verbTracker.InitVerbsFromZero();

            if (holder == null) return;
            List<Verb> verbs = eq.AllVerbs;
            for (int i = 0; i < verbs.Count; i++)
            {
                verbs[i].caster = holder;
                verbs[i].Notify_PickedUp();
            }
        }

        // The map mesh is cached, so a changed configuration only reappears once the cell is
        // marked dirty. Mirrors what vanilla Thing.Notify_ColorChanged does.
        private void DirtyOverlays()
        {
            if (parent == null || !parent.Spawned || parent.Map == null) return;
            if (parent.def.drawerType == DrawerType.MapMeshOnly
                || parent.def.drawerType == DrawerType.MapMeshAndRealTime)
            {
                parent.Map.mapDrawer.MapMeshDirty(parent.Position, MapMeshFlagDefOf.Things);
            }
        }

        private void Accumulate(WeaponPartDef part)
        {
            if (!part.statOffsets.NullOrEmpty())
                foreach (var m in part.statOffsets)
                    offsetCache[m.stat] = (offsetCache.TryGetValue(m.stat, out var o) ? o : 0f) + m.value;

            if (!part.statFactors.NullOrEmpty())
                foreach (var m in part.statFactors)
                    factorCache[m.stat] = (factorCache.TryGetValue(m.stat, out var f) ? f : 1f) * m.value;

            if (part.grantsAbility != null)
            {
                if (resolvedAbilities == null) resolvedAbilities = new HashSet<AbilityDef>();
                resolvedAbilities.Add(part.grantsAbility);
            }

            if (!part.tools.NullOrEmpty())
            {
                if (resolvedExtraTools == null) resolvedExtraTools = new List<Tool>();
                resolvedExtraTools.AddRange(part.tools);
            }

            if (part.muzzleFlashEffecterOverride != null)
                resolvedMuzzleEffecter = part.muzzleFlashEffecterOverride;
            resolvedMuzzleDistance += part.muzzleFlashDistanceOffset;
            resolvedMuzzleScale *= part.muzzleFlashScaleFactor;
            if (part.suppressMuzzleFlash) muzzleFlashSuppressed = true;

            resolvedBurstCountOffset += part.burstShotCountOffset;
            resolvedBurstCountFactor *= part.burstShotCountMultiplier;
            resolvedBurstSpeedFactor *= part.burstShotSpeedMultiplier;

            if ((part.soundCastOverride != null || part.soundCastTailOverride != null)
                && part.overridePriority >= resolvedSoundPriority)
            {
                if (part.soundCastOverride != null) resolvedSoundCast = part.soundCastOverride;
                if (part.soundCastTailOverride != null)
                    resolvedSoundCastTail = part.soundCastTailOverride;
                resolvedSoundPriority = part.overridePriority;
            }
            // Highest priority wins rather than "whichever slot happens to come last".
            if (part.projectileOverride != null
                && part.overridePriority >= resolvedProjectilePriority)
            {
                resolvedProjectile = part.projectileOverride;
                resolvedProjectilePriority = part.overridePriority;
            }

            if (part.ceMagSizeOverride > 0
                && part.overridePriority >= resolvedCEMagSizePriority)
            {
                resolvedCEMagSize = part.ceMagSizeOverride;
                resolvedCEMagSizePriority = part.overridePriority;
            }

            if (!string.IsNullOrEmpty(part.ceAmmoSetDefName)
                && part.overridePriority >= resolvedCEAmmoSetPriority)
            {
                resolvedCEAmmoSetDefName = part.ceAmmoSetDefName;
                resolvedCEAmmoSetPriority = part.overridePriority;
            }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Collections.Look(ref installed, "installed",
                LookMode.Def, LookMode.Def, ref scribeSlots, ref scribeParts);
            Scribe_Collections.Look(ref directlyGranted, "directlyGranted", LookMode.Def);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (installed == null)
                    installed = new Dictionary<WeaponPartSlotDef, WeaponPartDef>();
                if (directlyGranted == null) directlyGranted = new List<AbilityDef>();

                // MIGRATION: weapons that existed BEFORE this comp was added to their def
                // have no "installed" node, so the dictionary comes back empty and wipes the
                // defaults Initialize() put there. Without this, every pre-update rifle in an
                // old save loads with no barrel - an empty required slot - and reads as
                // non-functional.
                ApplyDefaultsIfEmpty();

                Recache();

                // Recache ran with Scribe active, so RebuildVerbs was skipped on purpose.
                // Record the signature it produced so the first in-game Recache does not
                // then decide the tools "changed" and rebuild for no reason.
                lastToolSignature = ComputeToolSignature();
            }
        }

        // On-ground / in-storage overlay. Held-weapon rendering is a separate hook -
        // see the note in HarmonyPatches.cs (DrawEquipmentAiming) since it varies by version.
        // Overlay for a weapon being CARRIED (hauled by a pawn, or drawn by anything that
        // calls DrawNowAt with an explicit position).
        //
        // Must use the drawLoc that is handed in, not parent.DrawPos: once the weapon is in
        // a carry tracker it is no longer spawned, and Thing.Position still reports the cell
        // it was last on. Using DrawPos left every attachment sitting on the ground while
        // the pawn walked off with the receiver.
        public override void DrawAt(Vector3 drawLoc, bool flip = false)
        {
            // Whole-weapon offset. No aim angle in the carry pose, so only the flip matters.
            Vector3 body = BodyDrawOffset;
            if (flip) body.x = -body.x;
            drawLoc += body;

            base.DrawAt(drawLoc, flip);
            if (!HasDrawableParts) return;

            // Graphic_RandomRotated.DrawWorker tilts the carried weapon by the same
            // thingIDNumber-derived angle it uses on the ground, so the overlays have to
            // match or the attachments float upright beside a tilted gun.
            float bodyAngle = GroundExtraRotation();

            foreach (var kv in FittedPartsForDrawing())
            {
                GroundDrawCalls++;

                WeaponPartDef part = kv.Key;
                PartDrawData data = kv.Value;

                Vector3 local = data.offset;
                if (flip) local.x = -local.x;

                // Offsets are authored in sprite space: rotate them with the body.
                Vector3 pos = drawLoc + local.RotatedBy(bodyAngle);
                pos.y += data.layer * LayerAltitudeStep;

                Graphic g = part.Graphic;
                Vector2 size = g.drawSize * data.scale;
                Material mat = g.MatSingleFor(parent);
                if (mat == null) continue;

                Matrix4x4 matrix = Matrix4x4.TRS(
                    pos,
                    Quaternion.AngleAxis(bodyAngle + data.angleOffset, Vector3.up),
                    new Vector3(size.x, 0f, size.y));

                Graphics.DrawMesh(MeshPool.plane10, matrix, mat, 0);
            }
        }
        // Called from Graphic_ModularWeapon.Print - i.e. inside the weapon body's own print
        // pass. Position, rotation and scale all come from the graphic and rotation handed
        // in, so whatever transform the caller applied to the body applies to the
        // attachments too. That is what keeps them aligned inside Adaptive Storage display
        // racks, which print stored items inside a transform scope.
        // Weapons set graphicData.onGroundRandomRotateAngle, which wraps their graphic in
        // Graphic_RandomRotated and tilts each one by a thingIDNumber-derived angle. The
        // print path receives that angle as extraRotation, but the realtime carried path
        // (DrawAt) has to work it out itself.
        private float GroundExtraRotation()
        {
            Graphic_RandomRotated rr = parent.Graphic as Graphic_RandomRotated;
            if (rr == null) return 0f;

            if (parent.def.IsWeapon && parent.Spawned)
            {
                IntVec3 pos = parent.Position;
                if (pos.InBounds(parent.Map) && pos.GetEdifice(parent.Map) != null
                    && pos.GetItemCount(parent.Map) >= 2)
                {
                    return parent.def.rotateInShelves ? -90f : 0f;
                }
            }

            float maxAngle = parent.def.graphicData != null
                ? parent.def.graphicData.onGroundRandomRotateAngle : 0f;
            if (maxAngle <= 0.01f) return 0f;

            return 0f - maxAngle + (float)(parent.thingIDNumber * 542) % (maxAngle * 2f);
        }

        // The whole-weapon offset expressed in ground/section space. On the ground the gun is
        // drawn either upright or rotated flat, so the sprite-space (x = along barrel) offset
        // has to be turned to match whichever the body graphic is using - otherwise the body
        // and overlays would shift in different directions when the item lies rotated.
        public Vector3 GroundBodyShift(Rot4 rot, Graphic bodyGraphic)
        {
            Vector3 o = BodyDrawOffset;
            if (o == Vector3.zero) return Vector3.zero;

            if (bodyGraphic.ShouldDrawRotated)
            {
                float a = rot.AsAngle + bodyGraphic.DrawRotatedExtraAngleOffset;
                if ((rot == Rot4.West && bodyGraphic.WestFlipped)
                    || (rot == Rot4.East && bodyGraphic.EastFlipped))
                    a += 180f;
                return o.RotatedBy(a);
            }

            // Upright: x maps to world x, z (sprite forward) maps to world z.
            return new Vector3(o.x, 0f, o.z);
        }

        public void PrintOverlays(SectionLayer layer, Thing thing, float extraRotation,
            Graphic bodyGraphic, bool under)
        {
            if (!HasDrawableParts) return;

            Rot4 rot = thing.Rotation;
            Vector3 baseCenter = thing.TrueCenter() + bodyGraphic.DrawOffset(rot)
                                 + GroundBodyShift(rot, bodyGraphic);


            foreach (var kv in FittedPartsForDrawing())
            {
                PartDrawData data = kv.Value;

                bool isUnder = data.layer < 0f;
                if (isUnder != under) continue;

                GroundPrintCalls++;

                WeaponPartDef part = kv.Key;
                Graphic g = part.Graphic;

                Material mat = g.MatAt(rot, thing);
                if (mat == null) continue;

                float angle;
                Vector2 size;
                bool flip = false;

                if (bodyGraphic.ShouldDrawRotated)
                {
                    size = g.drawSize * data.scale;
                    angle = rot.AsAngle + bodyGraphic.DrawRotatedExtraAngleOffset;
                    if ((rot == Rot4.West && bodyGraphic.WestFlipped)
                        || (rot == Rot4.East && bodyGraphic.EastFlipped))
                        angle += 180f;
                }
                else
                {
                    size = (rot.IsHorizontal ? g.drawSize.Rotated() : g.drawSize)
                         * data.scale;
                    angle = 0f;
                    flip = (rot == Rot4.West && bodyGraphic.WestFlipped)
                        || (rot == Rot4.East && bodyGraphic.EastFlipped);
                }

                // extraRotation already carries the per-thing ground tilt (passed down by
                // Graphic_RandomRotated) plus anything the caller added, so it must not be
                // recomputed here - that would apply the tilt twice.
                // Vanilla Graphic.Print shrinks the body when a cell holds several items -
                // storage racks always do - so attachments must shrink identically or they
                // end up a quarter too large, which is how they overflowed the shelf.
                if (thing.MultipleItemsPerCellDrawn()) size *= 0.8f;

                angle += extraRotation + data.angleOffset;

                // ...and vanilla adds flipExtraRotation on a flipped body. Weapons commonly
                // set 180 there, so skipping it left attachments upside down relative to the
                // gun they are bolted to.
                if (flip && bodyGraphic.data != null)
                    angle += bodyGraphic.data.flipExtraRotation;

                // The same 0.8 applies to distances, or attachments shrink but drift away
                // from the receiver they are meant to sit on.
                Vector3 local = data.offset;
                if (thing.MultipleItemsPerCellDrawn()) local *= 0.8f;
                if (flip) local.x = -local.x;
                Vector3 center = baseCenter + local.RotatedBy(angle);
                center.y += data.layer * LayerAltitudeStep;

                // Items are baked into a shared texture atlas. Vanilla Graphic.Print resolves
                // the atlas tile and passes the matching UV rect; printing the raw material
                // with default 0-1 UVs samples the wrong region of the atlas, which is why
                // attachments came out with their tops sliced off inside storage.
                Material printMat = mat;
                Vector2[] uvs = null;
                Color32 vertexColor = new Color32(255, 255, 255, 255);
                Graphic.TryGetTextureAtlasReplacementInfo(mat, thing.def.category.ToAtlasGroup(),
                    flip, true, out printMat, out uvs, out vertexColor);

                Printer_Plane.PrintPlane(layer, center, size, printMat, angle, flip, uvs,
                    new Color32[4] { vertexColor, vertexColor, vertexColor, vertexColor });
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (var g in base.CompGetGizmosExtra()) yield return g;

            if (Prefs.DevMode)
            {
                yield return new Command_Action
                {
                    defaultLabel = "DEV: dump modular state",
                    defaultDesc = "Logs installed parts, whether each has a usable graphic, "
                                + "resolved offsets, and whether the render hooks are firing.",
                    action = () => Log.Message(DebugStateReport())
                };

                yield return new Command_Action
                {
                    defaultLabel = "DEV: tune part offsets",
                    defaultDesc = "Live-adjust the draw offset, scale and angle of every fitted " +
                                  "part, then copy the result as a <drawOverrides> XML block.",
                    action = () => Find.WindowStack.Add(new Window_PartOffsetTuner(this))
                };
            }

            // The intended route is the bench gizmo (pick a colonist, they haul the
            // materials and do the work). This direct, instant version stays available only
            // in dev mode for testing.
            if (!Prefs.DevMode) yield break;

            yield return new Command_Action
            {
                defaultLabel = "DEV: customize directly",
                defaultDesc = "Apply a configuration instantly, ignoring materials and work.",
                action = () => Find.WindowStack.Add(new Window_ModularWeapon(this))
            };
        }

        // ---- dev-only live tuning (not serialized) ----
        private Dictionary<WeaponPartDef, PartDrawData> tuningOverrides;
        private Vector3? bodyOffsetTuning;

        public bool HasTuning => tuningOverrides != null && tuningOverrides.Count > 0;

        public void SetTuning(WeaponPartDef part, PartDrawData data)
        {
            if (tuningOverrides == null)
                tuningOverrides = new Dictionary<WeaponPartDef, PartDrawData>();
            tuningOverrides[part] = data;

            drawCacheDirty = true;
            // The ground overlay lives in the cached map mesh, so without this the tuner
            // sliders only appeared to work on a held weapon.
            DirtyOverlays();
        }

        public void ClearTuning()
        {
            tuningOverrides = null;
            bodyOffsetTuning = null;
            drawCacheDirty = true;
            DirtyOverlays();
        }

        // ---- placement resolution: weapon override beats the part's own default ----
        public PartDrawData ResolveDrawData(WeaponPartDef part)
        {
            return ResolveDrawData(part, installed, ActiveSlots);
        }

        // Overload resolving against an ARBITRARY configuration. The customization window
        // passes its pending config so that swapping to a longer barrel moves the muzzle
        // device in the preview immediately, instead of only after the order is applied.
        public PartDrawData ResolveDrawData(WeaponPartDef part,
            Dictionary<WeaponPartSlotDef, WeaponPartDef> config, List<WeaponPartSlotDef> slotSet)
        {
            // Dev tuner wins while it's open, so edits show up live on the held weapon.
            // Gated on DevMode at READ time as well as write time: these entries are keyed by
            // WeaponPartDef, so a leaked one pins that part's offset forever and only
            // "unsticks" when a different part is fitted - which is exactly how a stale
            // tuning entry looks in play.
            if (Prefs.DevMode && tuningOverrides != null
                && tuningOverrides.TryGetValue(part, out var tuned))
                return tuned;

            // Candidates, most specific first. Nulls are simply skipped.
            WeaponPartDrawOverride weaponExact = null, weaponSlot = null;
            WeaponPartDrawOverride childExact = null, childSlot = null;

            // Deltas from every additive entry that matches, regardless of source.
            Vector3 addOffset = Vector3.zero;
            Vector3 addLaser = Vector3.zero;
            float addLaserWidth = 0f;
            float addLaserDot = 0f;
            Vector3 addLight = Vector3.zero;
            float addLightCone = 0f;
            float addLightRadius = 0f;
            float addLightCircle = 0f;
            float addScale = 0f;
            float addAngle = 0f;
            float addLayer = 0f;

            // --- weapon-level overrides ---
            if (!Props.drawOverrides.NullOrEmpty())
            {
                foreach (var ov in Props.drawOverrides)
                {
                    if (!ov.Matches(part)) continue;
                    if (ov.additive) { AccumulateDelta(ov, ref addOffset, ref addScale, ref addAngle, ref addLayer, ref addLaser, ref addLaserWidth, ref addLaserDot, ref addLight, ref addLightCone, ref addLightRadius, ref addLightCircle); continue; }
                    if (ov.part != null) { if (weaponExact == null) weaponExact = ov; }
                    else if (weaponSlot == null) weaponSlot = ov;
                }
            }

            // --- part-to-part: every OTHER part in this configuration may place this one ---
            foreach (var slot in slotSet)
            {
                var other = ResolveFrom(config, slot);
                if (other == null || other == part || other.childOffsets.NullOrEmpty()) continue;

                foreach (var ov in other.childOffsets)
                {
                    if (!ov.Matches(part)) continue;
                    if (ov.additive) { AccumulateDelta(ov, ref addOffset, ref addScale, ref addAngle, ref addLayer, ref addLaser, ref addLaserWidth, ref addLaserDot, ref addLight, ref addLightCone, ref addLightRadius, ref addLightCircle); continue; }
                    if (ov.part != null) { if (childExact == null) childExact = ov; }
                    else if (childSlot == null) childSlot = ov;
                }
            }

            // --- pick the replacement, then apply accumulated deltas ---
            WeaponPartDrawOverride winner = weaponExact ?? childExact ?? childSlot ?? weaponSlot;

            PartDrawData data;
            if (winner != null)
                data = new PartDrawData
                {
                    offset = winner.offset,
                    scale = winner.scale,
                    angleOffset = winner.angleOffset,
                    // unset <layer> inherits the part's own drawLayer
                    layer = winner.HasLayer ? winner.layer : part.drawLayer,
                    laserOffset = winner.HasLaserOffset
                                              ? winner.laserOffset : part.laserOffset,
                    laserWidth = winner.HasLaserWidth
                                              ? winner.laserWidth : DefBeamWidth(part),
                    laserDot = winner.HasLaserDot
                                              ? winner.laserDot : DefDotSize(part),
                    lightOffset = winner.HasLightOffset
                                              ? winner.lightOffset : part.flashlightOffset,
                    lightCone = winner.HasLightCone
                                              ? winner.lightCone : DefCone(part),
                    lightRadius = winner.HasLightRadius
                                              ? winner.lightRadius : DefCircle(part),
                    lightCircle = winner.HasLightCircle
                                              ? winner.lightCircle : DefPool(part)
                };
            else
                data = new PartDrawData
                {
                    offset = part.drawOffset,
                    scale = 1f,
                    angleOffset = 0f,
                    layer = part.drawLayer,
                    laserOffset = part.laserOffset,
                    laserWidth = DefBeamWidth(part),
                    laserDot = DefDotSize(part),
                    lightOffset = part.flashlightOffset,
                    lightCone = DefCone(part),
                    lightRadius = DefCircle(part),
                    lightCircle = DefPool(part)
                };

            data.offset += addOffset;
            data.scale += addScale;
            data.angleOffset += addAngle;
            data.layer += addLayer;
            data.laserOffset += addLaser;
            data.laserWidth += addLaserWidth;
            data.laserDot += addLaserDot;
            data.lightOffset += addLight;
            data.lightCone += addLightCone;
            data.lightRadius += addLightRadius;
            data.lightCircle += addLightCircle;

            // Scale the whole assembly with the weapon body, if the weapon opted in.
            // Applied last so it affects the final resolved values, and applied to the
            // offset too - otherwise a shrunken gun keeps its attachments at the old
            // distance and they float off the barrel.
            float bodyFactor = BodyScaleFactor;
            if (!Mathf.Approximately(bodyFactor, 1f))
            {
                data.offset *= bodyFactor;
                data.scale *= bodyFactor;
                data.laserOffset *= bodyFactor;   // emitter moves with the shrunken body
                data.lightOffset *= bodyFactor;
            }
            return data;
        }

        // Ratio between the weapon's current drawSize and the size the offsets were
        // authored against (CompProperties.partScaleReference). 1 = no scaling.
        public float BodyScaleFactor
        {
            get
            {
                float reference = Props.partScaleReference;
                if (reference <= 0.01f) return 1f;

                GraphicData gd = parent.def.graphicData;
                if (gd == null) return 1f;

                float current = Mathf.Max(gd.drawSize.x, gd.drawSize.y);
                if (current <= 0.01f) return 1f;

                return current / reference;
            }
        }

        // Beam look falls back to whatever the part's <laser> block declares.
        private static float DefBeamWidth(WeaponPartDef part)
        {
            return part.laser != null ? part.laser.beamWidth : 0f;
        }

        private static float DefDotSize(WeaponPartDef part)
        {
            return part.laser != null ? part.laser.dotSize : 0f;
        }

        // Near width of the trapezoid shaft.
        private static float DefCone(WeaponPartDef part)
        {
            return part.flashlight != null ? part.flashlight.nearWidth : 0f;
        }

        // Far width of the trapezoid shaft.
        private static float DefCircle(WeaponPartDef part)
        {
            return part.flashlight != null ? part.flashlight.farWidth : 0f;
        }

        // Radius of the round pool texture at the far end.
        private static float DefPool(WeaponPartDef part)
        {
            return part.flashlight != null ? part.flashlight.circleRadius : 0f;
        }

        private static void AccumulateDelta(WeaponPartDrawOverride ov,
            ref Vector3 offset, ref float scale, ref float angle, ref float layer,
            ref Vector3 laser, ref float laserWidth, ref float laserDot,
            ref Vector3 light, ref float lightCone, ref float lightRadius,
            ref float lightCircle)
        {
            if (ov.HasLightCircle) lightCircle += ov.lightCircle;
            if (ov.HasLightOffset) light += ov.lightOffset;
            if (ov.HasLightCone) lightCone += ov.lightCone;
            if (ov.HasLightRadius) lightRadius += ov.lightRadius;
            offset += ov.offset;
            if (ov.HasLaserOffset) laser += ov.laserOffset;
            if (ov.HasLaserWidth) laserWidth += ov.laserWidth;
            if (ov.HasLaserDot) laserDot += ov.laserDot;
            scale += ov.scale - 1f;   // scale defaults to 1, so the delta is (scale - 1)
            angle += ov.angleOffset;
            if (ov.HasLayer) layer += ov.layer;
        }

        // Enumerates every fitted part together with its resolved placement.
        // Ordered by layer so overlays stack predictably (low first, high on top).
        //
        // CACHED: this is read every frame by the held-weapon renderer, by the map-mesh
        // print, and once per icon in every inventory/trade list. Rebuilding and sorting a
        // List on each of those calls was the bulk of the mod's frame cost, so the list is
        // built only when the configuration (or dev tuning) actually changes.
        private List<KeyValuePair<WeaponPartDef, PartDrawData>> cachedDrawParts;
        private bool drawCacheDirty = true;

        private static readonly List<KeyValuePair<WeaponPartDef, PartDrawData>> EmptyDrawParts =
            new List<KeyValuePair<WeaponPartDef, PartDrawData>>();

        // Fast path for the patches: skip all work when this weapon draws nothing.
        public bool HasDrawableParts
        {
            get
            {
                if (drawCacheDirty) RebuildDrawCache();
                return cachedDrawParts != null && cachedDrawParts.Count > 0;
            }
        }

        // Built LAZILY, on first use from a render path.
        //
        // Recache() runs during save loading, which happens on a WORKER THREAD, and touching
        // WeaponPartDef.Graphic there hits GraphicDatabase - main-thread only. It returned
        // null, this cache recorded "no drawable parts", and every attachment silently
        // disappeared until something else forced a rebuild. Deferring the build to the
        // first draw call guarantees it happens on the main thread.
        // Whole-weapon offset in sprite space, already scaled to the body. Rotation is left
        // to the caller because each draw path has the angle in a different form.
        public Vector3 BodyDrawOffset
        {
            get
            {
                Vector3 o = bodyOffsetTuning ?? Props.bodyDrawOffset;
                if (o == Vector3.zero) return Vector3.zero;
                return o * BodyScaleFactor;
            }
        }

        // Raw (unscaled) body offset the tuner is editing - the XML value, not the
        // body-scaled one used for drawing.
        public Vector3 BodyOffsetRaw => bodyOffsetTuning ?? Props.bodyDrawOffset;

        public void SetBodyOffsetTuning(Vector3 offset)
        {
            bodyOffsetTuning = offset;
            drawCacheDirty = true;
            DirtyOverlays();
        }

        public List<KeyValuePair<WeaponPartDef, PartDrawData>> FittedPartsForDrawing()
        {
            if (drawCacheDirty) RebuildDrawCache();
            return cachedDrawParts ?? EmptyDrawParts;
        }

        // Parts that emit a beam. Kept separate from the draw cache because a laser part is
        // still a laser part even if it ships no overlay texture yet.
        // Parts that emit light, resolved the same way as lasers.
        public List<KeyValuePair<WeaponPartDef, PartDrawData>> FittedFlashlights()
        {
            var list = new List<KeyValuePair<WeaponPartDef, PartDrawData>>();
            foreach (var slot in ActiveSlots)
            {
                var part = PartInSlot(slot);
                if (part?.flashlight == null) continue;
                list.Add(new KeyValuePair<WeaponPartDef, PartDrawData>(part, ResolveDrawData(part)));
            }
            return list;
        }

        public List<KeyValuePair<WeaponPartDef, PartDrawData>> FittedLasers()
        {
            var list = new List<KeyValuePair<WeaponPartDef, PartDrawData>>();
            foreach (var slot in ActiveSlots)
            {
                var part = PartInSlot(slot);
                if (part?.laser == null) continue;
                list.Add(new KeyValuePair<WeaponPartDef, PartDrawData>(part, ResolveDrawData(part)));
            }
            return list;
        }

        private void RebuildDrawCache()
        {
            drawCacheDirty = false;

            var list = new List<KeyValuePair<WeaponPartDef, PartDrawData>>();
            foreach (var slot in ActiveSlots)
            {
                var part = PartInSlot(slot);
                if (part == null) continue;

                if (part.graphicData == null || part.Graphic == null)
                {
                    WarnMissingGraphicOnce(part);
                    continue;
                }
                list.Add(new KeyValuePair<WeaponPartDef, PartDrawData>(part, ResolveDrawData(part)));
            }
            list.Sort((a, b) => a.Value.layer.CompareTo(b.Value.layer));
            cachedDrawParts = list;
        }

        // A part fitted but invisible is the single most confusing failure mode, so say so
        // once per part instead of silently drawing nothing.
        private static HashSet<WeaponPartDef> warnedNoGraphic = new HashSet<WeaponPartDef>();

        private static void WarnMissingGraphicOnce(WeaponPartDef part)
        {
            if (!warnedNoGraphic.Add(part)) return;
            if (part.graphicData == null)
                Log.Warning("[LGModularWeapons] " + part.defName + " is installed but has no "
                          + "<graphicData>, so no overlay can be drawn for it.");
            else
                Log.Warning("[LGModularWeapons] " + part.defName + " has <graphicData> but its "
                          + "Graphic failed to load - check <texPath> and <graphicClass>.");
        }

        // ---- required-part validation ----
        // A slot marked <required>true</required> with nothing in it means the weapon is
        // missing something it needs to function; the customization window refuses to apply.
        public bool TryGetMissingRequiredSlots(
            Dictionary<WeaponPartSlotDef, WeaponPartDef> config, out List<WeaponPartSlotDef> missing)
        {
            return TryGetMissingRequiredSlots(config, ActiveSlots, out missing);
        }

        public bool TryGetMissingRequiredSlots(
            Dictionary<WeaponPartSlotDef, WeaponPartDef> config,
            List<WeaponPartSlotDef> slotSet, out List<WeaponPartSlotDef> missing)
        {
            missing = null;
            foreach (var slot in slotSet)
            {
                if (!slot.required) continue;
                WeaponPartDef part = null;
                if (config != null) config.TryGetValue(slot, out part);
                if (part == null) part = slot.defaultPart;
                if (part == null)
                {
                    if (missing == null) missing = new List<WeaponPartSlotDef>();
                    missing.Add(slot);
                }
            }
            return missing != null;
        }

        public bool IsFunctional
        {
            get
            {
                foreach (var slot in ActiveSlots)
                    if (slot.required && PartInSlot(slot) == null) return false;
                return true;
            }
        }

        // Vanilla parity: CompUniqueWeapon implements this so trait offsets/factors show up
        // inside the stat's explanation tooltip. Without it, factors on stats like
        // RangedWeapon_RangeMultiplier are applied but invisible in the info card.
        public override void GetStatsExplanation(StatDef stat, StringBuilder sb, string whitespace = "")
        {
            StringBuilder inner = new StringBuilder();
            foreach (var slot in ActiveSlots)
            {
                var part = PartInSlot(slot);
                if (part == null) continue;

                float off = part.statOffsets.GetStatOffsetFromList(stat);
                if (!Mathf.Approximately(off, 0f))
                    inner.AppendLine(whitespace + "    " + part.LabelCap + ": " +
                        stat.Worker.ValueToString(off, false, ToStringNumberSense.Offset));

                float fac = part.statFactors.GetStatFactorFromList(stat);
                if (!Mathf.Approximately(fac, 1f))
                    inner.AppendLine(whitespace + "    " + part.LabelCap + ": " +
                        stat.Worker.ValueToString(fac, false, ToStringNumberSense.Factor));
            }
            if (inner.Length != 0)
            {
                sb.AppendLine(whitespace + "LGMW_StatsReport_Parts".Translate() + ":");
                sb.Append(inner.ToString());
            }
        }

        // Adds a dedicated "Installed parts" row to the info card listing every fitted part
        // and its modifiers - this is where factors become directly visible.
        public override IEnumerable<StatDrawEntry> SpecialDisplayStats()
        {
            var fitted = new List<WeaponPartDef>();
            foreach (var slot in ActiveSlots)
            {
                var part = PartInSlot(slot);
                if (part != null) fitted.Add(part);
            }
            if (fitted.Count == 0) yield break;

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < fitted.Count; i++)
            {
                var part = fitted[i];
                sb.AppendLine(part.LabelCap.ToString().Colorize(ColorLibrary.Yellow));
                if (!part.description.NullOrEmpty()) sb.AppendLine(part.description);

                if (!part.statOffsets.NullOrEmpty())
                    foreach (var m in part.statOffsets)
                        sb.AppendLine(" - " + m.stat.LabelCap + " " +
                            m.stat.Worker.ValueToString(m.value, false, ToStringNumberSense.Offset));

                if (!part.statFactors.NullOrEmpty())
                    foreach (var m in part.statFactors)
                        sb.AppendLine(" - " + m.stat.LabelCap + " " +
                            m.stat.Worker.ValueToString(m.value, false, ToStringNumberSense.Factor));

                // Burst modifiers bypass the stat system, so list them explicitly or they
                // would be invisible in the info card.
                if (part.burstShotCountOffset != 0)
                    sb.AppendLine(" - " + "LGMW_BurstCount".Translate() + " "
                        + part.burstShotCountOffset.ToStringWithSign());
                if (!Mathf.Approximately(part.burstShotCountMultiplier, 1f))
                    sb.AppendLine(" - " + "LGMW_BurstCount".Translate() + " x"
                        + part.burstShotCountMultiplier.ToString("0.##"));
                if (!Mathf.Approximately(part.burstShotSpeedMultiplier, 1f))
                    sb.AppendLine(" - " + "LGMW_BurstSpeed".Translate() + " x"
                        + part.burstShotSpeedMultiplier.ToString("0.##"));

                if (i < fitted.Count - 1) sb.AppendLine();
            }

            yield return new StatDrawEntry(
                parent.def.IsMeleeWeapon ? StatCategoryDefOf.Weapon_Melee : StatCategoryDefOf.Weapon_Ranged,
                "LGMW_InstalledParts".Translate(),
                fitted.Count.ToString(),
                sb.ToString(),
                1103);
        }

        public override string CompInspectStringExtra()
        {
            int fitted = 0;
            foreach (var slot in ActiveSlots)
                if (PartInSlot(slot) != null) fitted++;
            string str = "LGMW_PartsFitted".Translate(fitted, ActiveSlots.Count);
            if (!IsFunctional)
                str += "\n" + "LGMW_MissingRequired".Translate().Colorize(ColorLibrary.RedReadable);
            return str;
        }
    }
}