using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace LGModularWeapons
{
    // One Def per part. The combinatorial explosion is avoided because a weapon
    // instance only stores *which* parts are fitted (in CompWeaponModular), never
    // a pre-baked Def for every combination.
    public class WeaponPartDef : Def
    {
        public WeaponPartSlotDef slot;

        // --- Performance ---
        // Applied through the vanilla StatWorker pipeline via CompWeaponModular.
        // Any StatDef works, e.g.:
        //   AccuracyTouch / AccuracyShort / AccuracyMedium / AccuracyLong
        //   RangedWeapon_Cooldown, RangedWeapon_RangeMultiplier, RangedWeapon_WarmupMultiplier
        //   Mass, MarketValue ...
        public List<StatModifier> statOffsets;
        public List<StatModifier> statFactors;

        // --- Burst behaviour (magazine / gas block parts) ---
        // Burst count and burst cadence are NOT StatDefs - vanilla handles them with plain
        // multiplier fields on WeaponTraitDef, so parts do the same.
        // A 20-round magazine trading capacity for control might use
        //   <burstShotCountOffset>-1</burstShotCountOffset>
        // while a drum mag uses a multiplier. Offset is applied before the multiplier.
        public int burstShotCountOffset = 0;
        public float burstShotCountMultiplier = 1f;

        // >1 = faster cadence between shots in a burst (fewer ticks), <1 = slower.
        public float burstShotSpeedMultiplier = 1f;

        // --- Equippable ability this part unlocks (underbarrel launcher, etc.) ---
        // The weapon def keeps its CompProperties_EquippableAbility(Reloadable) permanently,
        // so the comp list stays identical between save and load. This field decides whether
        // that ability is actually available: without the part it is gated off completely -
        // no gizmo, no charges, no reload job.
        public AbilityDef grantsAbility;

        // --- Melee tools this part adds (bayonet, spiked stock...) ---
        // CompEquippable.Tools normally returns def.tools only, so these are appended at
        // runtime and the weapon's verbs are rebuilt when the part is fitted or removed.
        public List<Tool> tools;

        // --- Muzzle flash (barrel length / muzzle devices) ---
        // A longer barrel pushes the flash forward; a suppressor swaps it for a smaller
        // effect or removes it entirely.
        public EffecterDef muzzleFlashEffecterOverride;
        public float muzzleFlashDistanceOffset = 0f;   // added to the weapon's base distance
        public float muzzleFlashScaleFactor = 1f;      // multiplied into the base scale
        public bool suppressMuzzleFlash = false;       // no flash at all while installed

        // --- Sound (muzzle / suppressor parts) ---
        public SoundDef soundCastOverride;
        public SoundDef soundCastTailOverride;

        // --- Projectile swap (barrel / caliber parts). Optional. ---
        public ThingDef projectileOverride;

        // Tie-breaker when more than one fitted part overrides the projectile (or the fire
        // sound). Highest wins; ties fall back to slot order, which depends on how the weapon
        // happens to list its slots - so set this whenever two parts could collide.
        // Convention: barrels/calibre 0, muzzle devices 10, ammo or magazine parts 20.
        public int overridePriority = 0;

        // --- Combat Extended magazine capacity (magazine-slot parts only). Optional. ---
        // 0 = this part does not touch CE's magazine capacity - true for every part except
        // an actual magazine. Set it on magazine parts to the number of rounds that specific
        // magazine holds, e.g. a 20-round magazine -> 20, a 30-round magazine -> 30, a drum
        // -> whatever its fluff says.
        //
        // Consumed by CompWeaponModular.ApplyCEAmmoConfig, which clones and swaps CE's own
        // CompAmmoUser.props to apply it (there is no simpler settable "override" field for
        // this in CE - see the long comment on ApplyCEAmmoConfig for why), entirely through
        // reflection so this project keeps no compile-time reference to CombatExtended.dll.
        // Completely inert when Combat Extended is not loaded, or when the weapon has no
        // CompAmmoUser. Fitting or removing a part that changes this force-ejects whatever is
        // currently chambered (CE's own CompAmmoUser.TryUnload) so a round that no longer
        // fits the new capacity never rides along silently.
        public int ceMagSizeOverride = 0;

        // --- Combat Extended caliber conversion (magazine-slot parts only). Optional. ---
        // defName of the CombatExtended.AmmoSetDef this part converts the weapon to, e.g.
        // "AmmoSet_9x19mmPara" for a 9mm conversion magazine/drum. Empty/null = this part
        // does not touch the weapon's caliber - true for every part except a genuine
        // caliber-conversion magazine.
        //
        // Looked up by name in CE's own AmmoSetDef database purely through reflection (see
        // CompWeaponModular.ApplyCEAmmoConfig) - this project never references
        // CombatExtended.dll, and this field is completely inert when CE is not loaded.
        // Fitting or removing a part that changes this force-ejects whatever is currently
        // chambered (it belongs to the wrong caliber now) and resets the weapon's loaded/
        // selected ammo to the new AmmoSetDef's first entry.
        //
        // A part that sets this is EXEMPT from the "projectileOverride parts are refused
        // under CE" rule in CanFitPart - this is the one case where the caliber change is
        // real and CE-recognized rather than a vanilla-only, silently-inert swap.
        public string ceAmmoSetDefName;

        // --- Install cost, paid once when the part is fitted ---
        public List<ThingDefCountClass> costList;

        // --- Compatibility: weapon must carry at least one of these weaponTags.
        //     Empty/null = fits every modular weapon. ---
        public List<string> compatibleWeaponTags;

        // --- Slots this part opens up while installed (RIS handguard -> rail slots).
        //     Removing the part cascade-removes anything fitted into the slots it granted. ---
        public List<WeaponPartSlotDef> addsSlots;

        // --- Placement this part imposes on OTHER parts while installed.
        //     A long barrel pushes the muzzle device forward; a riser raises the optic.
        //     Match by <slot> or by <part>, and set <additive>true</additive> to nudge
        //     relative to the existing placement instead of replacing it. ---
        public List<WeaponPartDrawOverride> childOffsets;

        // --- Free-form tags, used for group-level conflicts/prerequisites so you don't
        //     have to list every part individually (e.g. "optic", "suppressing"). ---
        public List<string> partTags;

        // --- Mutual exclusion. Checked in BOTH directions, so listing the conflict on
        //     one of the two parts is enough. ---
        public List<WeaponPartDef> conflictsWith;
        public List<string> conflictTags;

        // --- Combination conflicts: blocked only when ALL parts in a group are installed
        //     together. A short barrel alone is fine, a large gas block alone is fine, but
        //     both at once leaves no room for a long handguard. ---
        public List<PartCombination> conflictingCombinations;

        // --- Prerequisites, evaluated against everything else currently installed ---
        public List<WeaponPartDef> requiresParts;   // ALL of these must be installed
        public List<WeaponPartDef> requiresAnyOf;   // at least ONE of these
        public List<string> requiresTags;           // ALL of these tags must be present

        public bool HasTag(string tag)
        {
            return partTags != null && partTags.Contains(tag);
        }

        // --- Research gate ---
        public ResearchProjectDef requiredResearch;

        // --- Appearance overlay drawn over the weapon ---
        public GraphicData graphicData;
        public Vector3 drawOffset = Vector3.zero;

        // --- Laser sight ---
        // Present = this part emits a beam while its wielder is aiming.
        //
        // laserOffset is measured FROM THIS PART'S OWN DRAWN POSITION, not from the weapon
        // centre - the diode is attached to the sight. So when a handguard repositions the
        // rail through childOffsets, the beam follows with no extra bookkeeping, and values
        // here stay small (a few hundredths). Scales with the weapon body like every other
        // offset.
        public LaserSightProps laser;
        public Vector3 laserOffset = Vector3.zero;

        // --- Weapon light ---
        // Same relative-origin rule as the laser: measured from THIS PART's drawn position,
        // so a handguard that repositions the rail carries the cone with it.
        public FlashlightProps flashlight;
        public Vector3 flashlightOffset = Vector3.zero;

        // Draw order for this part's overlay. POSITIVE = above the weapon body,
        // NEGATIVE = underneath it (bipods, underbarrel launchers), 0 = same plane
        // (avoid: it z-fights with the gun sprite). Overridable per weapon / parent part.
        public float drawLayer = 1f;

        public Graphic Graphic => graphicData?.Graphic;

        public bool ResearchDone => requiredResearch == null || requiredResearch.IsFinished;

        public bool AppliesTo(ThingDef weapon)
        {
            if (compatibleWeaponTags.NullOrEmpty()) return true;
            if (weapon?.weaponTags == null) return false;
            for (int i = 0; i < compatibleWeaponTags.Count; i++)
                if (weapon.weaponTags.Contains(compatibleWeaponTags[i])) return true;
            return false;
        }
    }
}