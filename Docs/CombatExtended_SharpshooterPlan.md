# Combat Extended Sharpshooter Compatibility Plan

This note records the intended sharpshooter-mode behavior of Helodrace firearms
before translating them into Combat Extended weapon patches.

## Current System

Helodrace firearms use `Helodrace.CompProperties_SharpshooterWeapon`.
When a pawn has `HD_Gene_Sharpshooter`, the weapon can toggle to an alternate
verb. The comp currently assumes:

- `altVerbIndex` points at the alternate firing verb, usually index `1`.
- The normal verb remains the first verb.
- The alternate verb has `hasStandardCommand=false`.
- `altAccuracyMultiplier` and `altCooldownMultiplier` are vanilla-side helpers.

For CE compatibility, the important part to preserve is the two-verb structure:

- verb `0`: normal CE firing mode
- verb `1`: Helodrace sharpshooter CE firing mode

The CE patch should not replace each weapon with only one verb, and should not
insert extra verbs before the sharpshooter verb unless `altVerbIndex` is also
patched.

## CE Translation Rules

Use CE XML values to express the mode difference whenever possible.

- Accuracy/precision: prefer CE spread, sway, recoil, aim/warmup, and range
  values on the alternate verb rather than relying on `altAccuracyMultiplier`.
- Rate of fire: prefer CE burst count, burst interval, warmup, cooldown, and
  reload/magazine behavior rather than relying on `altCooldownMultiplier`.
- Ammunition: both normal and sharpshooter verbs should use the same ammo set
  unless the weapon's concept explicitly needs a special load.
- Shotguns: do not model pellets as vanilla `burstShotCount`; use CE shotgun
  projectile/pellet behavior so one shell is consumed per shot.
- Heavy weapons: movement penalties from `moveSpeedOffset` can remain in the
  Helodrace comp, but CE bulk/sway/recoil should also carry the mounted-fire
  feel.

## Weapon Intent Map

| Weapon | Current Alternate Verb | Design Intent | CE Patch Direction |
| --- | --- | --- | --- |
| Ironfang SAA | 6 shots, very short range, fast warmup, high cooldown multiplier | Fanning. Empty the cylinder fast at close range. | Alt verb should be close-range, high-spread, high-recoil fanning. Keep six-shot burst feel, but make CE ammo consumption respect revolver capacity. CE-specific comp may be needed if CE burst consumes too many rounds or ignores cylinder state. |
| Ironfang M1860 Army | 6 shots, very short range, fast warmup, high cooldown multiplier | Fanning for a cap-and-ball revolver. Slower/rougher than Ironfang SAA. | Same as SAA, but slower burst interval, worse reload/handling, more smoke/old-gun feel if CE supports it. |
| W&M Model 3 | 6 shots, very short range, nearly instant warmup, high cooldown multiplier | Fast break-open revolver fanning. | Close-range fanning with slightly cleaner handling than Ironfang M1860 Army. Keep strong short-range identity. |
| Henry Rifle | Short range, faster warmup, lower cooldown multiplier | Lever-action snap fire / fast repeat shot. | Alt verb should be shorter range with higher spread/recoil recovery cost but faster aimed shot cadence. Do not make it a six-shot burst; it is controlled rapid fire. |
| Frontier Arms M1887 | Same pellet count, much shorter range, faster warmup, lower accuracy multiplier | Shotgun rush / short lever-action blast. | Use CE shotgun shell behavior. Alt verb should be faster and closer-ranged with wider spread, not nine separate ammo-consuming shots. |
| Frontier Arms M1895 | Short range, faster warmup | Powerful lever-action snap shot. | Alt verb should trade range/precision for faster warmup and quicker follow-up. Higher recoil than Henry. |
| Graylock M1861 | Longer range, much slower warmup, accuracy multiplier | Deliberate muzzle-loader marksman shot. | Alt verb should be long-range aimed fire with increased warmup/aim time and better precision. Reload remains slow. |
| Graylock M1873 | Longer range, slower warmup, accuracy multiplier | Deliberate trapdoor rifle marksman shot. | Alt verb should be long-range precision fire with slower aim and better shot stability. |
| M1911 | 2-shot burst, short range, fast warmup | Double tap. | Alt verb should be close-range two-shot burst with increased recoil/spread and fast warmup. Keep magazine consumption at two rounds. |
| Graylock M1892 | Shorter range, faster warmup | Bolt-action snap shot / quick aimed shot. | Alt verb should trade long-range performance for faster target acquisition. Keep single-shot bolt cadence. |
| M1 Hartwell | 2-shot burst, short range, faster warmup | Semi-auto double tap. | Alt verb should be two-shot controlled pair with increased recoil and reduced effective range. |
| M1919A6 LMG | Longer range, 12-shot burst, high accuracy multiplier, severe move penalty | Braced sustained fire / pseudo-mounted sharpshooter mode. | Alt verb should require commitment: longer or equal warmup, long burst, improved sustained-fire control, strong recoil/bulk penalties, and the existing move penalty. A CE-specific comp may be useful if we need to force immobility, crouch/braced behavior, or disallow firing while moving. |
| Graylock M1903 | Shorter range, faster warmup | Fast bolt-action snap shot, not true sniping in current XML. | Decide whether to preserve current snap-shot behavior or redesign as a sniper mode. If preserving existing behavior, CE alt verb should be faster but less precise/shorter-ranged. |
| Graylock M1903 Whitmore | 2-shot burst, short range, fast warmup | Whitmore device double tap / rapid fire. | Alt verb should be short-range two-shot burst using Whitmore ammo, with lower per-shot precision than normal. |

## Cases That May Need CE-Specific Code

XML-only CE patching should be enough for most weapons if CE accepts both normal
and alternate verbs on one weapon. Add a CE-specific comp only if testing shows
one of these failures:

- CE ammo consumption treats fanning or shotgun pellet simulation incorrectly.
- CE replaces or ignores `CompEquippable.PrimaryVerb` in a way that bypasses the
  Helodrace alternate-verb swap.
- CE fire-mode gizmos conflict with the Helodrace sharpshooter toggle.
- The M1919A6 needs stronger bracing rules than XML can express.

If code is needed, prefer a narrow optional integration class that activates only
when `ceteam.combatextended` is loaded. The current Helodrace comp should remain
usable without CE.

## Patch Order

1. Add a CE-only patch guarded by `PatchOperationFindMod`.
2. Add CE ammo users and ammo sets to each firearm.
3. Replace each weapon's verbs with exactly two CE-compatible verbs.
4. Keep `altVerbIndex=1` for all sharpshooter weapons.
5. Add CE projectile or ammo mappings for Helodrace bullet defs only if needed.
6. Test manual fire, drafted auto-fire, ammo consumption, reload, and the
   sharpshooter toggle on each weapon family.
