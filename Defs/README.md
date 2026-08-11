# Def layout

- Keep top-level folders aligned with gameplay modules or eras.
- Put building `ThingDef`s in `Buildings` and other item `ThingDef`s in `Items`.
- Within each era, group tangible items by the same role names: `Apparel`,
  `Equipment`, `Ammunition`, `Materials`, `Grenades`, and `Weapons`.
- Group buildings by their role (`Industry`, `Defense`, or `Utility`) when an era
  has more than one kind. A system-specific file may be kept when its Defs are
  tightly coupled.
- Assign an era from the item's service generation and its research gate. Internal
  projectiles and effects stay with the system that owns them.
- Keep Helod race definitions under `Helod`, grouped by their role.
- Use descriptive PascalCase file names. Do not add `Def` or `Defs` when the name is already clear.
- Keep a module suffix such as `_GreatWar` on generic file names that may otherwise collide.
- For one-to-one `DefInjected` translation files, keep the source and translation file names identical. `TranslationEditor.py` uses the file name to locate the source Def; split translation files may use a descriptive suffix.
