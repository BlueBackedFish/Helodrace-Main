using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace Helodrace.ModernWar
{
    public enum ModularWeaponPartStorageMode
    {
        // The part is represented by a count while it is in a linked parts box. A real
        // Thing is created only while the part is installed in an assembly.
        Virtual,

        // The part is valuable as an individual assembly (for example an upper receiver)
        // and is therefore kept as a real Thing when it is outside a weapon.
        IndependentThing,

        // Runtime selectors such as the hidden ammunition node are not inventory parts.
        Internal
    }

    public enum ModularWeaponPartCategory
    {
        Optional,
        Functional,
        Required
    }

    public enum ModularWeaponMissingFunction
    {
        SemiAutomaticOnly,
        SingleRoundCapacity
    }

    public sealed class ModularFlashlightTargetOverride
    {
        public List<PawnKindDef> pawnKinds;
        public HediffDef hediff;
        public float intensity = 1f;
        public float severityPerSecond;
        public bool requireExisting;

        public bool Matches(Pawn pawn)
        {
            return !pawnKinds.NullOrEmpty()
                && pawn?.kindDef != null
                && pawnKinds.Contains(pawn.kindDef);
        }
    }

    /// <summary>
    /// Legion-style weapon light data. The emitter offset is authored from the part's
    /// graphic centre, so it follows nested pivots, rail movement and vertical mirroring.
    /// Texture paths are optional; the renderer supplies procedural soft gradients when
    /// no texture is assigned.
    /// </summary>
    public sealed class ModularWeaponFlashlightProperties
    {
        public Vector3 emitterOffset = Vector3.zero;
        public Color color = new Color(1f, 0.95f, 0.75f, 0.16f);
        public string coneTexPath;
        public float nearWidth = 0.15f;
        public float farWidth = 1.1f;
        public float lengthOvershoot;
        public string circleTexPath;
        public float circleRadius = 0.55f;
        public bool drawCircle = true;
        public float closeRange = 3.5f;
        public float closeRangeMinScale = 0.2f;
        public float maxRange = 24f;
        public HediffDef hediff;
        public float intensity = 1f;
        public List<ModularFlashlightTargetOverride> targetOverrides;
        public bool affectsMechanoids;
        public bool affectsAnimals = true;
    }

    /// <summary>
    /// A laser emitter authored from the owning part's graphic centre. Like the weapon
    /// light, this offset follows the complete nested attachment transform, rail movement
    /// and top/bottom mirroring.
    /// </summary>
    public sealed class ModularWeaponLaserProperties
    {
        public Vector3 emitterOffset = Vector3.zero;
        public Color color = new Color(1f, 0.12f, 0.12f, 0.8f);
        public float beamWidth = 0.018f;
        public float dotSize = 0.12f;
        public bool drawDot = true;
        public float maxLength = 80f;
    }

    public enum ModularSightKind
    {
        IronFront,
        IronRear,
        Holographic,
        Reflex,
        Scope,
        Magnifier
    }

    /// <summary>
    /// Defines the optical axis of a sight. The axis is authored from the texture centre;
    /// aimRadius is the usable vertical window around it. Parts at a greater local X are
    /// in front of the sight and can cover this window through verticalOccupancy.
    /// </summary>
    public sealed class ModularWeaponSightProperties
    {
        public ModularSightKind kind;
        public Vector3 axisOffset = Vector3.zero;
        public float aimRadius = 0.015f;
        public float selectionPriority = 10f;
    }

    /// <summary>
    /// An ejection port authored from the owning receiver graphic centre. The complete
    /// attachment transform is applied at shot time, so receivers remain responsible for
    /// the port position while the selected ammunition supplies the casing mote.
    /// </summary>
    public sealed class ModularWeaponCasingPortProperties
    {
        public Vector3 offset = Vector3.zero;
        public float ejectionAngleOffset = 90f;
        public float speedMin = 1.5f;
        public float speedMax = 2.2f;
        public float scale = 0.75f;
        public float ejectionCycleFraction = 0.28f;
        public float feedStartCycleFraction = 0.55f;
        public float feedEndCycleFraction = 0.92f;
        public Vector3 feedStartOffset = new Vector3(-0.075f, 0f, -0.045f);
        public Vector3 feedEndOffset = new Vector3(-0.025f, 0f, -0.004f);
        public float feedingRoundDrawSize = 0.12f;
    }

    public enum ModularWeaponAnimatedPartKind
    {
        None,
        Bolt,
        Trigger
    }

    public enum ModularRailSurface
    {
        Unspecified,
        Top,
        Bottom,
        Side
    }

    public sealed class ModularAttachmentTransform
    {
        public Vector3 position = Vector3.zero;
        public float angle;
        public Vector2 scale = Vector2.one;
        public float layer;

        public ModularAttachmentTransform Clone()
        {
            return new ModularAttachmentTransform
            {
                position = position,
                angle = angle,
                scale = scale,
                layer = layer
            };
        }
    }

    public sealed class ModularAttachmentSocket
    {
        public string id;
        public string label;
        public List<string> tags = new List<string>();
        public ModularAttachmentTransform transform = new ModularAttachmentTransform();
        public bool required;
        public bool isRail;
        public float railLength;
        public int maxAttachments;
        public ModularRailSurface railSurface;

        public string Label => label.NullOrEmpty() ? id : label;
    }

    public sealed class ModularAttachmentMount
    {
        public string id = "mount";
        public string label;
        public List<string> socketTags = new List<string>();
        public ModularAttachmentTransform transform = new ModularAttachmentTransform();
        public float railOccupancy;
        public ModularRailSurface railSurface;
        public Vector3 oppositeSurfaceOffset = Vector3.zero;

        public string Label => label.NullOrEmpty() ? id : label;

        public bool Accepts(ModularAttachmentSocket socket)
        {
            if (socket == null) return false;
            bool tagMatch = socketTags.NullOrEmpty() || socket.tags.NullOrEmpty();

            for (int i = 0; !tagMatch && i < socketTags.Count; i++)
            {
                if (socket.tags.Contains(socketTags[i])) tagMatch = true;
            }

            if (!tagMatch) return false;
            if (!socket.isRail)
                return true;

            bool picatinny = socketTags?.Contains("picatinny") == true
                || socket.tags?.Contains("picatinny") == true;
            if (railSurface == ModularRailSurface.Unspecified
                || socket.railSurface == ModularRailSurface.Unspecified)
                return !picatinny;

            if (railSurface == ModularRailSurface.Side
                || socket.railSurface == ModularRailSurface.Side)
                return railSurface == ModularRailSurface.Side
                    && socket.railSurface == ModularRailSurface.Side;

            return IsVerticalSurface(railSurface)
                && IsVerticalSurface(socket.railSurface);
        }

        public bool UsesOppositeSurface(ModularAttachmentSocket socket)
        {
            return socket != null
                && IsVerticalSurface(railSurface)
                && IsVerticalSurface(socket.railSurface)
                && railSurface != socket.railSurface;
        }

        public ModularAttachmentTransform EffectiveTransform(
            ModularAttachmentSocket socket)
        {
            ModularAttachmentTransform result = transform?.Clone()
                ?? new ModularAttachmentTransform();
            if (UsesOppositeSurface(socket)) result.scale.y *= -1f;
            return result;
        }

        private static bool IsVerticalSurface(ModularRailSurface value)
        {
            return value == ModularRailSurface.Top
                || value == ModularRailSurface.Bottom;
        }
    }

    public sealed class ModularDefaultAttachment
    {
        public string socketId;
        public string mountId;
        public ThingDef part;
        public float railOffset;
    }

    public sealed class CompProperties_ModularWeaponNode : CompProperties
    {
        public bool isAssemblyRoot;
        public bool allowPlayerConfiguration = true;
        public ModularWeaponPartStorageMode storageMode =
            ModularWeaponPartStorageMode.Virtual;
        public ModularWeaponPartCategory partCategory =
            ModularWeaponPartCategory.Optional;
        public List<ModularWeaponMissingFunction> missingFunctions =
            new List<ModularWeaponMissingFunction>();
        public float realisticRoundsPerMinute;
        public float baseFireDelayFactor = 1.2f;
        public float fireDelayMultiplier = 1f;
        public float sightGroupHeightTolerance = 0.015f;

        // Internal authoring values. They are aggregated into a single
        // ModularWeaponConvertedStats profile and are never exposed as StatDefs.
        public ModularWeaponInternalStats internalStats;

        // Legion-style performance modifiers. Ordinary parts stack across the tree. On a
        // node with sight data they are gated by the one active sight group and scaled by
        // that group's unobstructed aiming-window efficiency.
        public List<StatModifier> statOffsets;
        public List<StatModifier> statFactors;
        public int burstShotCountOffset;
        public float burstShotCountMultiplier = 1f;
        public float burstShotSpeedMultiplier = 1f;

        // Non-StatDef combat overrides. Highest overridePriority wins; equal priorities use
        // deterministic render-tree order.
        public ThingDef projectileOverride;
        public ThingDef casingMoteDef;
        public string feedingRoundTexPath;
        public SoundDef soundCastOverride;
        public SoundDef soundCastTailOverride;
        public int overridePriority;

        // Muzzle presentation is resolved from the complete tree. Distance offsets add,
        // scale factors multiply, and any suppressMuzzleFlash flag disables the effect.
        public EffecterDef muzzleFlashEffecter;
        public float muzzleFlashDistance = 1.6f;
        public float muzzleFlashScale = 1f;
        public EffecterDef muzzleFlashEffecterOverride;
        public float muzzleFlashDistanceOffset;
        public float muzzleFlashScaleFactor = 1f;
        public bool suppressMuzzleFlash;
        public ModularMuzzleEffectKind muzzleEffectKind = ModularMuzzleEffectKind.Auto;

        public Vector3 graphicOffset = Vector3.zero;
        public float graphicAngle;
        public Vector2 graphicScale = Vector2.one;
        public float graphicLayer;
        public int outlinePriority;
        public ModularWeaponAnimatedPartKind animatedPart;
        public Vector3 animationTravel = Vector3.zero;
        public Vector3 animationPivot = Vector3.zero;
        public float animationAngle;
        // Internal selectors such as loaded ammunition remain real Things and are visible
        // on the ground, but should not be composited over the assembled weapon sprite.
        public bool hideWhenAttached;
        // Magazine capacity and CE ammunition identity are deliberately kept out of
        // ModularWeaponConvertedStats. Capacity belongs to the installed magazine,
        // while ammo-set/current-ammo selection belongs to the chamber and cartridge.
        // Strings avoid a hard assembly dependency on Combat Extended.
        public int magazineCapacity;
        public string ceAmmoSetDefName;
        public string ceAmmoDefName;
        public string ammunitionType;
        // Machine-readable chamber/ammunition identity. Diameter data lets mismatches be
        // classified without hard-coding individual cartridge names.
        public string chamberCaliber;
        public float boreDiameterMm;
        public string ammunitionCaliber;
        public float projectileDiameterMm;
        // A visual-occlusion interval only. It never participates in attachment collision.
        public float verticalOccupancy;
        public float verticalOccupancyOffset;
        public ModularWeaponSightProperties sight;
        public ModularWeaponFlashlightProperties flashlight;
        public ModularWeaponLaserProperties laser;
        public ModularWeaponCasingPortProperties casingPort;
        public List<ModularAttachmentMount> mounts = new List<ModularAttachmentMount>();
        public List<ModularAttachmentSocket> sockets = new List<ModularAttachmentSocket>();
        public List<ModularDefaultAttachment> defaultAttachments =
            new List<ModularDefaultAttachment>();

        public CompProperties_ModularWeaponNode()
        {
            compClass = typeof(CompModularWeaponNode);
        }

        public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
        {
            foreach (string error in base.ConfigErrors(parentDef)) yield return error;

            if (partCategory == ModularWeaponPartCategory.Functional
                && missingFunctions.NullOrEmpty())
                yield return parentDef.defName
                    + " is a functional modular part without a missingFunctions effect.";
            if (partCategory != ModularWeaponPartCategory.Functional
                && !missingFunctions.NullOrEmpty())
                yield return parentDef.defName
                    + " has missingFunctions but is not categorized as Functional.";

            if (isAssemblyRoot && realisticRoundsPerMinute <= 0f)
                yield return parentDef.defName
                    + " is a modular weapon root without realisticRoundsPerMinute.";
            if (isAssemblyRoot && baseFireDelayFactor < 1f)
                yield return parentDef.defName
                    + " has baseFireDelayFactor below its mechanical RPM floor.";
            if (fireDelayMultiplier <= 0f)
                yield return parentDef.defName + " has a non-positive fireDelayMultiplier.";
            if (isAssemblyRoot && sightGroupHeightTolerance <= 0f)
                yield return parentDef.defName
                    + " has a non-positive sightGroupHeightTolerance.";
            if (isAssemblyRoot && storageMode == ModularWeaponPartStorageMode.IndependentThing)
                yield return parentDef.defName
                    + " is an assembly root and does not need IndependentThing storage mode.";
            if (internalStats != null)
            {
                if (internalStats.massKg == 0f)
                    yield return parentDef.defName
                        + " has zero internal mass; use a positive value or -1 for automatic mass.";
                if (internalStats.momentOfInertia < 0f)
                    yield return parentDef.defName
                        + " has a negative internal moment of inertia.";
                if (internalStats.identificationDistanceMeters < 0f)
                    yield return parentDef.defName
                        + " has a negative internal identification distance.";
            }
            if (verticalOccupancy < 0f)
                yield return parentDef.defName + " has a negative verticalOccupancy.";
            if (magazineCapacity < 0)
                yield return parentDef.defName + " has a negative magazineCapacity.";
            if (!ceAmmoDefName.NullOrEmpty() && ammunitionType.NullOrEmpty())
                yield return parentDef.defName
                    + " defines ceAmmoDefName without an ammunitionType.";
            if (!ceAmmoSetDefName.NullOrEmpty() && chamberCaliber.NullOrEmpty())
                yield return parentDef.defName
                    + " defines ceAmmoSetDefName without a chamberCaliber.";
            if (!chamberCaliber.NullOrEmpty() && boreDiameterMm <= 0f)
                yield return parentDef.defName
                    + " defines a chamber caliber without a positive bore diameter.";
            if (!ammunitionCaliber.NullOrEmpty() && projectileDiameterMm <= 0f)
                yield return parentDef.defName
                    + " defines an ammunition caliber without a positive projectile diameter.";
            if (sight != null)
            {
                if (sight.aimRadius <= 0f)
                    yield return parentDef.defName
                        + " has a sight with a non-positive aimRadius.";
                if (sight.selectionPriority < 0f)
                    yield return parentDef.defName
                        + " has a sight with a negative selectionPriority.";
            }
            if (burstShotCountMultiplier <= 0f)
                yield return parentDef.defName
                    + " has a non-positive burstShotCountMultiplier.";
            if (burstShotSpeedMultiplier <= 0f)
                yield return parentDef.defName
                    + " has a non-positive burstShotSpeedMultiplier.";
            if (muzzleFlashScaleFactor < 0f)
                yield return parentDef.defName
                    + " has a negative muzzleFlashScaleFactor.";
            if (casingMoteDef != null
                && (casingMoteDef.thingClass == null
                    || !typeof(MoteThrown).IsAssignableFrom(casingMoteDef.thingClass)))
                yield return parentDef.defName
                    + " has a casingMoteDef that is not a MoteThrown.";
            if (casingPort != null)
            {
                if (casingPort.speedMin <= 0f
                    || casingPort.speedMax < casingPort.speedMin)
                    yield return parentDef.defName
                        + " has an invalid casing ejection speed range.";
                if (casingPort.scale <= 0f)
                    yield return parentDef.defName
                        + " has a non-positive casing scale.";
                if (casingPort.ejectionCycleFraction < 0f
                    || casingPort.ejectionCycleFraction > 1f)
                    yield return parentDef.defName
                        + " has a casing ejection fraction outside [0, 1].";
                if (casingPort.feedStartCycleFraction < 0f
                    || casingPort.feedEndCycleFraction
                        <= casingPort.feedStartCycleFraction
                    || casingPort.feedEndCycleFraction > 1f)
                    yield return parentDef.defName
                        + " has an invalid feeding phase range.";
                if (casingPort.feedingRoundDrawSize <= 0f)
                    yield return parentDef.defName
                        + " has a non-positive feeding-round draw size.";
            }
            if (flashlight != null)
            {
                if (flashlight.nearWidth <= 0f || flashlight.farWidth <= 0f)
                    yield return parentDef.defName
                        + " has a flashlight with a non-positive beam width.";
                if (flashlight.maxRange <= 0f)
                    yield return parentDef.defName
                        + " has a flashlight with a non-positive maxRange.";
                if (flashlight.closeRangeMinScale <= 0f
                    || flashlight.closeRangeMinScale > 1f)
                    yield return parentDef.defName
                        + " has a flashlight closeRangeMinScale outside (0, 1].";
            }
            if (laser != null)
            {
                if (laser.beamWidth <= 0f)
                    yield return parentDef.defName
                        + " has a laser with a non-positive beamWidth.";
                if (laser.dotSize <= 0f)
                    yield return parentDef.defName
                        + " has a laser with a non-positive dotSize.";
                if (laser.maxLength <= 0f)
                    yield return parentDef.defName
                        + " has a laser with a non-positive maxLength.";
            }

            HashSet<string> ids = new HashSet<string>();
            for (int i = 0; i < sockets.Count; i++)
            {
                ModularAttachmentSocket socket = sockets[i];
                if (socket == null || socket.id.NullOrEmpty())
                {
                    yield return parentDef.defName + " has a modular socket without an id.";
                    continue;
                }

                if (!ids.Add(socket.id))
                    yield return parentDef.defName + " has duplicate modular socket " + socket.id + ".";

                if (socket.isRail)
                {
                    if (socket.railLength <= 0f)
                        yield return parentDef.defName + " rail socket " + socket.id
                            + " must have a positive railLength.";
                    if (socket.transform != null
                        && !Mathf.Approximately(socket.transform.angle, 0f))
                        yield return parentDef.defName + " rail socket " + socket.id
                            + " must be horizontal (transform angle 0).";
                    if (socket.maxAttachments < 0)
                        yield return parentDef.defName + " rail socket " + socket.id
                            + " has a negative maxAttachments.";
                    if (socket.tags != null
                        && socket.tags.Contains("picatinny")
                        && socket.railSurface == ModularRailSurface.Unspecified)
                        yield return parentDef.defName + " Picatinny rail " + socket.id
                            + " must specify railSurface (Top, Bottom or Side).";
                }
            }

            ids.Clear();
            for (int i = 0; i < mounts.Count; i++)
            {
                ModularAttachmentMount mount = mounts[i];
                if (mount == null || mount.id.NullOrEmpty())
                {
                    yield return parentDef.defName + " has a modular mount without an id.";
                    continue;
                }

                if (!ids.Add(mount.id))
                    yield return parentDef.defName + " has duplicate modular mount " + mount.id + ".";

                if (mount.railOccupancy < 0f)
                    yield return parentDef.defName + " mount " + mount.id
                        + " has a negative railOccupancy.";
                if (mount.socketTags != null
                    && mount.socketTags.Contains("picatinny")
                    && mount.railOccupancy <= 0f)
                    yield return parentDef.defName + " Picatinny mount " + mount.id
                        + " must have a positive railOccupancy.";
                if (mount.socketTags != null
                    && mount.socketTags.Contains("picatinny")
                    && mount.railSurface == ModularRailSurface.Unspecified)
                    yield return parentDef.defName + " Picatinny mount " + mount.id
                        + " must specify railSurface (Top, Bottom or Side).";
            }

            for (int i = 0; i < defaultAttachments.Count; i++)
            {
                ModularDefaultAttachment attachment = defaultAttachments[i];
                if (attachment == null || attachment.part == null)
                {
                    yield return parentDef.defName + " has an invalid default modular attachment.";
                    continue;
                }

                if (SocketNamed(attachment.socketId) == null)
                    yield return parentDef.defName + " default attachment uses missing socket "
                        + attachment.socketId + ".";
            }
        }

        public ModularAttachmentSocket SocketNamed(string id)
        {
            if (id.NullOrEmpty()) return null;
            for (int i = 0; i < sockets.Count; i++)
            {
                if (sockets[i]?.id == id) return sockets[i];
            }

            return null;
        }

        public ModularAttachmentMount MountNamed(string id)
        {
            if (!id.NullOrEmpty())
            {
                for (int i = 0; i < mounts.Count; i++)
                {
                    if (mounts[i]?.id == id) return mounts[i];
                }
            }

            return mounts.Count > 0 ? mounts[0] : null;
        }
    }

    public struct ModularTransform2D
    {
        public Vector2 position;
        public float angle;
        public Vector2 scale;
        public float layer;

        public static ModularTransform2D Identity => new ModularTransform2D
        {
            position = Vector2.zero,
            angle = 0f,
            scale = Vector2.one,
            layer = 0f
        };

        public Vector2 TransformPoint(Vector3 local)
        {
            Vector2 scaled = new Vector2(local.x * scale.x, local.z * scale.y);
            return position + Rotate(scaled, angle);
        }

        public ModularTransform2D Attach(
            ModularAttachmentTransform socket,
            ModularAttachmentTransform mount,
            float childAngleOffset = 0f)
        {
            socket = socket ?? new ModularAttachmentTransform();
            mount = mount ?? new ModularAttachmentTransform();

            Vector2 socketPosition = TransformPoint(socket.position);
            float socketAngle = angle + socket.angle;
            Vector2 socketScale = Vector2.Scale(scale, SafeScale(socket.scale));
            Vector2 mountScale = SafeScale(mount.scale);
            Vector2 childScale = new Vector2(
                socketScale.x / mountScale.x,
                socketScale.y / mountScale.y);
            // The authored part angle belongs to the complete child node, not just its
            // texture. Folding it into the attachment transform keeps the mount pinned
            // to the parent socket while the graphic centre and all descendant sockets
            // orbit around that mount.
            float childAngle = socketAngle - mount.angle + childAngleOffset;
            Vector2 mountedOffset = new Vector2(
                mount.position.x * childScale.x,
                mount.position.z * childScale.y);

            return new ModularTransform2D
            {
                position = socketPosition - Rotate(mountedOffset, childAngle),
                angle = childAngle,
                scale = childScale,
                layer = layer + socket.layer - mount.layer
            };
        }

        private static Vector2 SafeScale(Vector2 value)
        {
            return new Vector2(
                Mathf.Abs(value.x) < 0.0001f ? 1f : value.x,
                Mathf.Abs(value.y) < 0.0001f ? 1f : value.y);
        }

        public static Vector2 Rotate(Vector2 value, float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            float sin = Mathf.Sin(radians);
            float cos = Mathf.Cos(radians);
            return new Vector2(
                value.x * cos - value.y * sin,
                value.x * sin + value.y * cos);
        }
    }

    public sealed class ModularRenderNode
    {
        public string path;
        public int depth;
        public Thing thing;
        public CompModularWeaponNode comp;
        public ModularTransform2D transform;
        public string parentSocketId;
        public string mountId;
        public CompModularWeaponNode parentComp;
        public int parentChildIndex = -1;
        public float railOffset;
        public bool attachedToRail;
        public bool mountedOnOppositeSurface;
        public Vector2 railStart;
        public Vector2 railEnd;
        public Vector2 occupiedRailStart;
        public Vector2 occupiedRailEnd;

        public CompProperties_ModularWeaponNode Props => comp.Props;
        public Vector2 GraphicCenter => transform.TransformPoint(Props.graphicOffset);
        public float GraphicAngle => transform.angle;
        public Vector2 GraphicScale => Vector2.Scale(transform.scale, Props.graphicScale);
        public bool GraphicVerticallyFlipped => GraphicScale.y < 0f;
        public float GraphicLayer => transform.layer + Props.graphicLayer;
    }
}
