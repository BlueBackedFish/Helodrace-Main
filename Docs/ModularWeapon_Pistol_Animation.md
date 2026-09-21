# Modular pistol animation

M1911A1 development pistol: `HD_Gun_ModularM1911_Test_Weapon`.
Spawn using `Helodrace > Spawn modular M1911 development pistol`.

The existing production M1911 is a separate weapon. The development pistol uses
the supplied 512px canvas textures without changing their pixels.

Reusable `animatedPart` values:

- `Slide`: bolt-cycle travel, including reload travel and externally notified last-round catch.
- `Hammer`: static attachment pose is decocked; `animationAngle` defines the cocked position.
  Readiness follows grip safety (equipped, drafted and standing). Ignition drops
  the hammer and the first 42% of the cycle returns it to cocked. Unheld weapons
  are immediately decocked, including ground printing and inventory icons.
- `TiltingBarrel`: follows slide travel; starts unlocking at 18% travel and reaches
  full tilt at 65%. Travel and rotation return to zero as the slide closes.
- `GripSafety`: moves toward pressed while the equipped pawn is drafted and able
  to stand, and toward released otherwise, over six simulation ticks.

All use existing `animationTravel`, `animationPivot`, and `animationAngle` fields.
The M1911 uses a 55-degree animation around its original hammer pivot, with zero
static angle. Both assembly dialogs use the same animated geometry as world drawing.
Placement values were updated from export `20260917_054213`, excluding the hammer
part and receiver hammer socket. Its original mount and pivot are retained.
Root `assemblyScale` scales the entire assembly, including sockets and animation
pivots (M1911: 0.5). Root `animationSpeed` accelerates the firing cycle without
changing fire rate (M1911: 1.75). Both default to 1 for existing weapons and are
preserved by XML export. Receiver and parts share one recoil transform.
The attachment editor supports type selection, undo and XML export for these values.
The M1911 slide is one part. Its primary texture draws at layer 3 and its
`additionalGraphics` entry draws the lower texture at layer -2. Additional
graphics share the part's transform, animation, scale, selection and visibility.
Each entry specifies `graphicLayer` and `graphicData` (texPath, Graphic_Single,
drawSize). Its matching `_Outline` is loaded automatically. Gameplay, sockets,
storage and performance still see one part. The barrel is an independent sibling.
The old UP Def name remains for save compatibility; obsolete attached DOWN items
are removed when an old M1911 assembly is loaded into its render snapshot.

The magazine supplies seven-round capacity to modular consumers. Empty-magazine
catch and reload notifications still require an ammunition system to call the
existing notification API; vanilla does not gain an ammunition simulation here.
No new CE patch or dedicated .45 casing sprite is included.

Verification: build and XML texture/default-attachment validation passed.
In-game validation still required: fire facing east/west, draft/undraft, drop and
reequip, and empty reload with an ammunition integration that issues notifications.
