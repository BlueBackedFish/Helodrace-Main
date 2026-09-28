# Modular weapon UI icon routing

Command_VerbTarget.DrawIcon uses Widgets.ThingIcon(ownerThing) instead of
Command.icon whenever ownerThing is present. ColonistBar uses the same
Thing-instance overload for weapons under portraits.

The Widgets.ThingIcon(Rect, Thing, float, Rot4?, bool, float, bool) prefix now
draws the existing cached assembly icon for modular assembly roots. Non-modular
items and pending bakes retain vanilla rendering. Scale, alpha and grayscale
are preserved, and GUI color is restored. ThingDef.uiIcon is never changed.

The existing Command.icon path remains for commands without ownerThing.
Both displays share the existing time-sliced bake budget and per-assembly cache.

Verification: compiled against installed RimWorld assemblies. Reviewed vanilla
Command_VerbTarget and ColonistBar call sites. Runtime visual verification still
required after restart: drafted portrait weapon, attack command, two different
builds of one weapon Def, part changes, and disabled command appearance.
