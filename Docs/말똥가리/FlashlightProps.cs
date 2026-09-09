using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace LGModularWeapons
{
    // Weapon light carried by a part. Like the laser it only does anything while the wielder
    // is aiming, but instead of a thin beam it throws a cone down the aim line, marks the
    // target with a ring, and dazzles whatever it is pointed at.
    // A stronger (or weaker) reaction for specific creatures.
    public class FlashlightTargetOverride
    {
        public List<PawnKindDef> pawnKinds;

        // Replaces the light's normal hediff for these targets. Leave null to keep it and
        // only change the intensity.
        public HediffDef hediff;

        public float intensity = 1f;

        // ADDITIVE mode, for hediffs that manage their own severity - vanilla's
        // LightExposure being the obvious one. It ticks itself off the glow grid:
        //
        //     Severity += (PsychGlowAt(pos) != PsychGlow.Dark ? 0.4f : -0.25f);   // per 60 ticks
        //
        // A weapon light is a sprite, not real illumination, so the glow grid never sees it.
        // Feeding severity in at a comparable rate reproduces the behaviour exactly, without
        // touching the DLC's own logic. Vanilla's lit rate is 0.4/second.
        public float severityPerSecond = 0f;

        // Never create the hediff, only add to one the creature already carries. Entities are
        // born with LightExposure; ordinary pawns should not suddenly gain it.
        public bool requireExisting = false;

        public bool Matches(Pawn pawn)
        {
            return !pawnKinds.NullOrEmpty() && pawn?.kindDef != null
                && pawnKinds.Contains(pawn.kindDef);
        }
    }

    public class FlashlightProps
    {
        public Color color = new Color(1f, 0.95f, 0.75f, 0.16f);

        // ---- Shaft ----
        // The beam body is a TRAPEZOID with independently adjustable near and far widths, so
        // it can be splayed like a fan or kept as a straight column. The texture is stretched
        // across that shape, so keep it simple and uniform along its length.
        //
        // Texture layout (Unity UVs: V=0 is the BOTTOM of the image):
        //   * Draw it VERTICALLY: bottom = emitter, top = target.
        //   * Horizontal axis = beam width, shape centred on x=0.5.
        //   * Avoid detail that would smear when stretched; a soft vertical gradient that
        //     fades toward the top blends into the pool nicely.
        //   * Draw it white - <color> tints it.
        public string coneTexPath;

        public float nearWidth = 0.15f;
        public float farWidth = 1.1f;

        // Extra length past the target's centre, in cells.
        //
        // The shaft already spans emitter -> target centre exactly. If it still looks like it
        // stops short, the usual cause is the TEXTURE fading out before its top edge - the
        // mesh reaches, the visible light does not. Rather than repainting, push the geometry
        // a little further so the bright part of the image lands on the target. Negative
        // values pull it back.
        public float lengthOvershoot = 0f;

        // ---- Head ----
        // Separate round texture for the pool of light. Fixed size at every range, drawn on
        // top of the shaft's far end.
        public string circleTexPath;
        public float circleRadius = 0.55f;
        public bool drawCircle = true;

        // ---- Close range ----
        // Under this distance the far width and the pool taper with distance instead of
        // staying at full size. Without it a target a cell away gets the full-width flare
        // crammed into almost no length, and the trapezoid reads as a squashed blob rather
        // than a beam. Set to 0 to disable.
        public float closeRange = 3.5f;

        // Floor for that taper, so the light never collapses to nothing at point blank.
        public float closeRangeMinScale = 0.2f;

        // Beyond this the light simply is not drawn and the dazzle does not apply - a torch
        // has a working distance like any weapon does.
        public float maxRange = 24f;

        // --- Debuff ---
        // Applied to whatever the wielder is aiming at. Leave null for a purely visual light.
        public HediffDef hediff;

        // How much this light counts for while it is on a target. Severity ends up being the
        // SUM of every light currently pointed at that pawn, so 1.0 means "one light = one
        // stage" and the HediffDef's stages read as 1, 2, 3 lights. Applied every tick while
        // aiming - there is no ramp-up, the penalty is immediate and vanishes just as fast.
        public float intensity = 1f;

        // Per-target overrides: some things react to light far more strongly than a human
        // squinting. List the pawn kinds and what the light does to them instead.
        //
        // This is how the Anomaly light-sensitivity behaviour is reproduced without depending
        // on any of that DLC's internals: point an entry at the entity's PawnKindDef and give
        // it a harsher hediff and a higher intensity. Use MayRequire on the def so the whole
        // file still loads for players without the DLC.
        public List<FlashlightTargetOverride> targetOverrides;

        // Only pawns are worth dazzling; mechs and animals can be excluded per def.
        public bool affectsMechanoids = false;
        public bool affectsAnimals = true;
    }
}