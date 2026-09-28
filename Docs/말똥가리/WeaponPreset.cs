using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace LGModularWeapons
{
    // A named part loadout. Stored per weapon ThingDef, because a preset built for a rifle
    // means nothing on a pistol - the slots and compatible parts differ entirely.
    // A named part loadout.
    //
    // STORED AS defName STRINGS, NOT Def REFERENCES. Mod settings are read inside the Mod
    // constructor, which RimWorld runs in LoadedModManager.CreateModClasses() - BEFORE the
    // DefDatabase is populated. Scribe_Defs at that point resolves everything to null, which
    // produced a wall of "Could not load reference to ..." errors and then "Null key while
    // loading dictionary". Strings load safely at any time and are resolved on demand once
    // defs exist.
    public class WeaponPreset : IExposable
    {
        public string name;
        public string weaponDefName;

        // slot defName -> part defName
        public Dictionary<string, string> configNames = new Dictionary<string, string>();

        private List<string> scribeSlots;
        private List<string> scribeParts;

        public ThingDef WeaponDef =>
            weaponDefName.NullOrEmpty() ? null : DefDatabase<ThingDef>.GetNamedSilentFail(weaponDefName);

        public bool MatchesWeapon(ThingDef def) =>
            def != null && def.defName == weaponDefName;

        // Resolves to live defs. Anything missing (mod removed, def renamed) is skipped, so
        // a preset degrades gracefully instead of producing null entries.
        public Dictionary<WeaponPartSlotDef, WeaponPartDef> BuildConfig()
        {
            var result = new Dictionary<WeaponPartSlotDef, WeaponPartDef>();
            foreach (var kv in configNames)
            {
                WeaponPartSlotDef slot = DefDatabase<WeaponPartSlotDef>.GetNamedSilentFail(kv.Key);
                WeaponPartDef part = DefDatabase<WeaponPartDef>.GetNamedSilentFail(kv.Value);
                if (slot != null && part != null) result[slot] = part;
            }
            return result;
        }

        public void SetConfig(Dictionary<WeaponPartSlotDef, WeaponPartDef> config)
        {
            configNames = new Dictionary<string, string>();
            foreach (var kv in config)
                if (kv.Key != null && kv.Value != null)
                    configNames[kv.Key.defName] = kv.Value.defName;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name");
            Scribe_Values.Look(ref weaponDefName, "weaponDefName");
            Scribe_Collections.Look(ref configNames, "configNames",
                LookMode.Value, LookMode.Value, ref scribeSlots, ref scribeParts);

            if (Scribe.mode == LoadSaveMode.PostLoadInit && configNames == null)
                configNames = new Dictionary<string, string>();
        }

        public string Summary()
        {
            return "LGMW_PresetParts".Translate(configNames.Count);
        }
    }

    // Presets are stored in MOD SETTINGS, not on the Game.
    //
    // A GameComponent would tie a loadout to the save it was designed in, which is the
    // opposite of what a preset is for - you build "CQB rifle" once and want it in every
    // colony. ModSettings live in the config folder, so they survive new saves, and RimWorld
    // handles the file for us.
    public class ModularWeaponsSettings : ModSettings
    {
        private List<WeaponPreset> presets = new List<WeaponPreset>();

        public List<WeaponPreset> PresetsFor(ThingDef weaponDef)
        {
            var result = new List<WeaponPreset>();
            for (int i = 0; i < presets.Count; i++)
                if (presets[i].MatchesWeapon(weaponDef)) result.Add(presets[i]);
            return result;
        }

        public void Save(string name, ThingDef weaponDef,
            Dictionary<WeaponPartSlotDef, WeaponPartDef> config)
        {
            // Same name for the same weapon overwrites, which is what "save again" means to
            // a player who is iterating on one loadout.
            WeaponPreset existing = null;
            for (int i = 0; i < presets.Count; i++)
            {
                if (presets[i].MatchesWeapon(weaponDef) && presets[i].name == name)
                {
                    existing = presets[i];
                    break;
                }
            }

            WeaponPreset preset = existing ?? new WeaponPreset();
            preset.name = name;
            preset.weaponDefName = weaponDef.defName;
            preset.SetConfig(config);

            if (existing == null) presets.Add(preset);

            Write();   // to disk immediately - a crash should not lose the loadout
        }

        public void Delete(WeaponPreset preset)
        {
            presets.Remove(preset);
            Write();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref presets, "presets", LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (presets == null)
                {
                    presets = new List<WeaponPreset>();
                    return;
                }

                // Only structural nulls need dropping now; missing defs are handled at
                // resolve time by BuildConfig, which cannot run this early anyway.
                for (int i = presets.Count - 1; i >= 0; i--)
                    if (presets[i] == null || presets[i].weaponDefName.NullOrEmpty())
                        presets.RemoveAt(i);
            }
        }
    }

    public class LGModularWeaponsMod : Mod
    {
        public static ModularWeaponsSettings Settings;

        public LGModularWeaponsMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<ModularWeaponsSettings>();
        }

        public override string SettingsCategory() => "LG Modular Weapons";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Listing_Standard list = new Listing_Standard();
            list.Begin(inRect);
            list.Label("LGMW_SettingsPresetsInfo".Translate());
            list.End();
        }
    }

    // Kept as the access point so the windows do not care where presets live.
    public static class ModularPresetManager
    {
        public static ModularWeaponsSettings Instance => LGModularWeaponsMod.Settings;
    }

    // Minimal text entry window for naming a preset.
    public class Dialog_NamePreset : Window
    {
        private string text;
        private readonly System.Action<string> onAccept;
        private bool focused;

        public Dialog_NamePreset(string initial, System.Action<string> onAccept)
        {
            text = initial ?? "";
            this.onAccept = onAccept;

            forcePause = true;
            doCloseX = true;
            absorbInputAroundWindow = true;
            closeOnClickedOutside = true;
        }

        public override Vector2 InitialSize => new Vector2(420f, 160f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 26f), "LGMW_PresetName".Translate());

            GUI.SetNextControlName("LGMW_PresetNameField");
            text = Widgets.TextField(new Rect(0f, 30f, inRect.width, 30f), text);

            if (!focused)
            {
                UI.FocusControl("LGMW_PresetNameField", this);
                focused = true;
            }

            bool valid = !text.NullOrEmpty();
            GUI.enabled = valid;
            if (Widgets.ButtonText(new Rect(inRect.width - 140f, inRect.height - 36f, 140f, 32f),
                    "LGMW_PresetSave".Translate())
                || (valid && Event.current.type == EventType.KeyDown
                    && Event.current.keyCode == KeyCode.Return))
            {
                onAccept?.Invoke(text.Trim());
                Close();
            }
            GUI.enabled = true;
        }
    }
}