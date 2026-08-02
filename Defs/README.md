# Def layout

- Keep top-level folders aligned with gameplay modules or eras.
- Put building `ThingDef`s in `Buildings` and other item `ThingDef`s in `Items`.
- Keep Helod race definitions under `Helod`, grouped by their role.
- Use descriptive PascalCase file names. Do not add `Def` or `Defs` when the name is already clear.
- Keep a module suffix such as `_GreatWar` on generic file names that may otherwise collide.
- For one-to-one `DefInjected` translation files, keep the source and translation file names identical. `TranslationEditor.py` uses the file name to locate the source Def; split translation files may use a descriptive suffix.
