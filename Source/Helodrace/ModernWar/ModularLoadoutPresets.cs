using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;

namespace Helodrace.ModernWar
{
    public static class ModularPresetXmlExporter
    {
        public static string ExportWeapon(CompModularWeaponNode root)
        {
            if (root?.parent?.def == null) return string.Empty;
            StringBuilder xml = NewDocument();
            Line(xml, 1, "<Helodrace.ModernWar.ModularWeaponPresetDef>");
            Line(xml, 2, "<defName>" + ExportedDefName(
                "HD_WeaponPreset_Exported_", root.parent.def) + "</defName>");
            Line(xml, 2, "<label>" + Escape("exported " + root.parent.def.label
                + " preset") + "</label>");
            Line(xml, 2, "<weaponDef>" + Escape(root.parent.def.defName)
                + "</weaponDef>");
            if (root.ChildCount > 0)
            {
                Line(xml, 2, "<parts>");
                for (int i = 0; i < root.ChildCount; i++)
                    AppendWeaponPart(xml, root, i, 3);
                Line(xml, 2, "</parts>");
            }
            Line(xml, 1, "</Helodrace.ModernWar.ModularWeaponPresetDef>");
            FinishDocument(xml);
            return xml.ToString();
        }

        public static string ExportArmor(CompModularArmor armor)
        {
            if (armor?.parent?.def == null) return string.Empty;
            StringBuilder xml = NewDocument();
            Line(xml, 1, "<Helodrace.ModernWar.ModularArmorPresetDef>");
            Line(xml, 2, "<defName>" + ExportedDefName(
                "HD_ArmorPreset_Exported_", armor.parent.def) + "</defName>");
            Line(xml, 2, "<label>" + Escape("exported " + armor.parent.def.label
                + " preset") + "</label>");
            Line(xml, 2, "<armorDef>" + Escape(armor.parent.def.defName)
                + "</armorDef>");

            List<InstalledModularArmorPart> fixedParts = armor.InstalledParts
                .Where(record => record?.part != null && !record.IsPalsMounted)
                .OrderBy(record => record.slot?.uiOrder ?? int.MaxValue)
                .ThenBy(record => record.slot?.defName)
                .ToList();
            if (fixedParts.Count > 0)
            {
                Line(xml, 2, "<fixedParts>");
                for (int i = 0; i < fixedParts.Count; i++)
                {
                    InstalledModularArmorPart record = fixedParts[i];
                    Line(xml, 3, "<li>");
                    Line(xml, 4, "<slot>" + Escape(record.slot?.defName)
                        + "</slot>");
                    Line(xml, 4, "<part>" + Escape(record.part.defName)
                        + "</part>");
                    if (record.position != null)
                        Line(xml, 4, "<position>" + Escape(record.position.defName)
                            + "</position>");
                    if (record.plateOrderSwapped)
                        Line(xml, 4, "<plateOrderSwapped>true</plateOrderSwapped>");
                    Line(xml, 3, "</li>");
                }
                Line(xml, 2, "</fixedParts>");
            }

            List<InstalledModularArmorPart> palsParts = armor.InstalledParts
                .Where(record => record?.part != null && record.IsPalsMounted)
                .OrderBy(record => record.palsPanel?.defName)
                .ThenBy(record => record.palsY)
                .ThenBy(record => record.palsX)
                .ThenBy(record => record.part.defName)
                .ToList();
            if (palsParts.Count > 0)
            {
                Line(xml, 2, "<palsParts>");
                for (int i = 0; i < palsParts.Count; i++)
                {
                    InstalledModularArmorPart record = palsParts[i];
                    Line(xml, 3, "<li>");
                    Line(xml, 4, "<part>" + Escape(record.part.defName)
                        + "</part>");
                    Line(xml, 4, "<panel>" + Escape(record.palsPanel?.defName)
                        + "</panel>");
                    Line(xml, 4, "<x>" + record.palsX + "</x>");
                    Line(xml, 4, "<y>" + record.palsY + "</y>");
                    Line(xml, 3, "</li>");
                }
                Line(xml, 2, "</palsParts>");
            }

            Line(xml, 1, "</Helodrace.ModernWar.ModularArmorPresetDef>");
            FinishDocument(xml);
            return xml.ToString();
        }

        private static void AppendWeaponPart(
            StringBuilder xml,
            CompModularWeaponNode parent,
            int childIndex,
            int indent)
        {
            Thing childThing = parent.ChildAt(childIndex);
            CompModularWeaponNode child =
                childThing?.TryGetComp<CompModularWeaponNode>();
            if (childThing?.def == null || child == null) return;

            Line(xml, indent, "<li>");
            Line(xml, indent + 1, "<part>" + Escape(childThing.def.defName)
                + "</part>");
            Line(xml, indent + 1, "<socketId>" + Escape(
                parent.SocketIdAt(childIndex)) + "</socketId>");
            string mountId = parent.MountIdAt(childIndex);
            if (!mountId.NullOrEmpty())
                Line(xml, indent + 1, "<mountId>" + Escape(mountId)
                    + "</mountId>");
            float railOffset = parent.RailOffsetAt(childIndex);
            if (Math.Abs(railOffset) > 0.000001f)
                Line(xml, indent + 1, "<railOffset>" + railOffset.ToString(
                    "R", CultureInfo.InvariantCulture) + "</railOffset>");
            if (child.ChildCount > 0)
            {
                Line(xml, indent + 1, "<children>");
                for (int i = 0; i < child.ChildCount; i++)
                    AppendWeaponPart(xml, child, i, indent + 2);
                Line(xml, indent + 1, "</children>");
            }
            Line(xml, indent, "</li>");
        }

        private static StringBuilder NewDocument()
        {
            StringBuilder xml = new StringBuilder(4096);
            xml.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
            xml.AppendLine("<Defs>");
            return xml;
        }

        private static void FinishDocument(StringBuilder xml)
        {
            xml.AppendLine("</Defs>");
        }

        private static string ExportedDefName(string prefix, ThingDef rootDef)
        {
            return prefix + (rootDef?.defName ?? "Unknown") + "_"
                + DateTime.UtcNow.ToString(
                    "yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture);
        }

        private static string Escape(string value)
        {
            return SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
        }

        private static void Line(StringBuilder xml, int indent, string value)
        {
            xml.Append(' ', indent * 2).AppendLine(value);
        }
    }

    public sealed class ModularArmorPresetPart
    {
        public ModularArmorSlotDef slot;
        public ModularArmorPartDef part;
        public ModularArmorPositionDef position;
        public bool plateOrderSwapped;
    }

    public sealed class ModularArmorPresetPalsPart
    {
        public ModularArmorPartDef part;
        public ModularArmorPalsPanelDef panel;
        public int x;
        public int y;
    }

    /// <summary>
    /// A stable, XML-authored armor configuration. Physical plates and removable armor
    /// modules are materialized as Things only when the preset is applied to an apparel.
    /// </summary>
    public sealed class ModularArmorPresetDef : Def
    {
        public ThingDef armorDef;
        public bool useDefaultConfiguration;
        public List<ModularArmorPresetPart> fixedParts = new List<ModularArmorPresetPart>();
        public List<ModularArmorPresetPalsPart> palsParts =
            new List<ModularArmorPresetPalsPart>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;

            CompProperties_ModularArmor armor =
                armorDef?.GetCompProperties<CompProperties_ModularArmor>();
            if (armor == null)
            {
                yield return defName + " has no armorDef with CompModularArmor.";
                yield break;
            }

            if (useDefaultConfiguration
                && (!fixedParts.NullOrEmpty() || !palsParts.NullOrEmpty()))
                yield return defName
                    + " uses the default configuration but also declares preset parts.";

            if (useDefaultConfiguration) yield break;

            HashSet<ModularArmorSlotDef> occupiedSlots =
                new HashSet<ModularArmorSlotDef>();
            HashSet<string> conflictTags = new HashSet<string>();
            for (int i = 0; i < fixedParts.Count; i++)
            {
                ModularArmorPresetPart entry = fixedParts[i];
                if (entry?.slot == null || entry.part == null)
                {
                    yield return defName + " has an incomplete fixed armor entry.";
                    continue;
                }

                if (!occupiedSlots.Add(entry.slot))
                    yield return defName + " assigns armor slot " + entry.slot.defName
                        + " more than once.";
                if (armor.slots.NullOrEmpty() || !armor.slots.Contains(entry.slot))
                    yield return defName + " uses unsupported armor slot "
                        + entry.slot.defName + ".";
                if (entry.part.installMode != ModularArmorInstallMode.Fixed
                    || entry.part.slot != entry.slot)
                    yield return defName + " assigns " + entry.part.defName
                        + " to the wrong fixed slot.";
                ModularArmorPositionDef position = entry.position
                    ?? entry.part.DefaultPosition;
                if (!entry.part.AllowsPosition(position))
                    yield return defName + " uses an invalid position for "
                        + entry.part.defName + ".";
                foreach (string error in CompatibilityErrors(
                    armor, entry.part, conflictTags))
                    yield return error;
            }

            List<ModularArmorPresetPalsPart> acceptedPals =
                new List<ModularArmorPresetPalsPart>();
            for (int i = 0; i < palsParts.Count; i++)
            {
                ModularArmorPresetPalsPart entry = palsParts[i];
                if (entry?.part == null || entry.panel == null)
                {
                    yield return defName + " has an incomplete PALS armor entry.";
                    continue;
                }

                int width = entry.part.PalsWidthFor(entry.panel);
                if (entry.part.installMode != ModularArmorInstallMode.Positionable
                    || entry.part.allowedPalsPanels.NullOrEmpty()
                    || !entry.part.allowedPalsPanels.Contains(entry.panel))
                    yield return defName + " cannot mount " + entry.part.defName
                        + " on " + entry.panel.defName + ".";
                if (armor.palsPanels.NullOrEmpty()
                    || !armor.palsPanels.Contains(entry.panel))
                    yield return defName + " uses unsupported PALS panel "
                        + entry.panel.defName + ".";
                if (entry.x < 0 || entry.y < 0
                    || entry.x + width > entry.panel.columns
                    || entry.y + entry.part.palsHeight > entry.panel.rows)
                    yield return defName + " places " + entry.part.defName
                        + " outside the PALS grid.";
                foreach (ModularArmorPresetPalsPart other in acceptedPals)
                {
                    if (other.panel == entry.panel && RectanglesOverlap(
                        entry.x, entry.y, width, entry.part.palsHeight,
                        other.x, other.y, other.part.PalsWidthFor(other.panel),
                        other.part.palsHeight))
                        yield return defName + " has overlapping PALS parts on "
                            + entry.panel.defName + ".";
                }
                acceptedPals.Add(entry);
                foreach (string error in CompatibilityErrors(
                    armor, entry.part, conflictTags))
                    yield return error;
            }

            if (!armor.slots.NullOrEmpty())
            {
                for (int i = 0; i < armor.slots.Count; i++)
                    if (armor.slots[i]?.required == true
                        && !occupiedSlots.Contains(armor.slots[i]))
                        yield return defName + " does not fill required armor slot "
                            + armor.slots[i].defName + ".";
            }
        }

        private IEnumerable<string> CompatibilityErrors(
            CompProperties_ModularArmor armor,
            ModularArmorPartDef part,
            HashSet<string> activeConflictTags)
        {
            if (!part.compatibleArmorTags.NullOrEmpty()
                && (armor.armorTags.NullOrEmpty()
                    || !part.compatibleArmorTags.Any(armor.armorTags.Contains)))
                yield return defName + " uses incompatible armor part "
                    + part.defName + ".";

            if (!part.conflictTags.NullOrEmpty())
            {
                if (part.conflictTags.Any(activeConflictTags.Contains))
                    yield return defName + " contains conflicting armor part "
                        + part.defName + ".";
                activeConflictTags.UnionWith(part.conflictTags);
            }
        }

        private static bool RectanglesOverlap(
            int x1, int y1, int width1, int height1,
            int x2, int y2, int width2, int height2)
        {
            return x1 < x2 + width2 && x1 + width1 > x2
                && y1 < y2 + height2 && y1 + height1 > y2;
        }
    }

    public sealed class ModularWeaponPresetPart
    {
        public ThingDef part;
        public string socketId;
        public string mountId;
        public float railOffset;
        public List<ModularWeaponPresetPart> children =
            new List<ModularWeaponPresetPart>();
    }

    /// <summary>
    /// An exact recursive modular weapon tree. useDefaultConfiguration is useful for
    /// PawnKinds which only need to lock in the authored default assembly.
    /// </summary>
    public sealed class ModularWeaponPresetDef : Def
    {
        public ThingDef weaponDef;
        public bool useDefaultConfiguration;
        public List<ModularWeaponPresetPart> parts = new List<ModularWeaponPresetPart>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            CompProperties_ModularWeaponNode root =
                weaponDef?.GetCompProperties<CompProperties_ModularWeaponNode>();
            if (root?.isAssemblyRoot != true)
            {
                yield return defName
                    + " has no weaponDef with a modular weapon root comp.";
                yield break;
            }

            if (useDefaultConfiguration && !parts.NullOrEmpty())
                yield return defName
                    + " uses the default configuration but also declares preset parts.";
            if (useDefaultConfiguration) yield break;

            foreach (string error in ValidateChildren(root, parts, weaponDef.defName, 0))
                yield return defName + ": " + error;
        }

        private static IEnumerable<string> ValidateChildren(
            CompProperties_ModularWeaponNode parent,
            List<ModularWeaponPresetPart> entries,
            string path,
            int depth)
        {
            if (depth > 32)
            {
                yield return path + " exceeds the maximum modular tree depth.";
                yield break;
            }

            entries = entries ?? new List<ModularWeaponPresetPart>();
            Dictionary<string, int> socketCounts = new Dictionary<string, int>();
            for (int i = 0; i < entries.Count; i++)
            {
                ModularWeaponPresetPart entry = entries[i];
                if (entry?.part == null || entry.socketId.NullOrEmpty())
                {
                    yield return path + " has an incomplete attachment entry.";
                    continue;
                }

                ModularAttachmentSocket socket = parent.SocketNamed(entry.socketId);
                CompProperties_ModularWeaponNode child =
                    entry.part.GetCompProperties<CompProperties_ModularWeaponNode>();
                ModularAttachmentMount mount = child?.MountNamed(entry.mountId);
                if (socket == null)
                    yield return path + " has no socket " + entry.socketId + ".";
                if (child == null)
                    yield return entry.part.defName + " is not a modular weapon part.";
                else if (mount == null || socket != null && !mount.Accepts(socket))
                    yield return entry.part.defName + " has no compatible mount for "
                        + entry.socketId + ".";

                socketCounts.TryGetValue(entry.socketId, out int count);
                count++;
                socketCounts[entry.socketId] = count;
                if (socket != null)
                {
                    int limit = socket.isRail ? socket.maxAttachments : 1;
                    if (limit > 0 && count > limit)
                        yield return path + " exceeds the attachment limit of "
                            + entry.socketId + ".";
                }

                if (child != null)
                    foreach (string error in ValidateChildren(
                        child, entry.children, path + "/" + entry.part.defName,
                        depth + 1))
                        yield return error;
            }

            if (!parent.sockets.NullOrEmpty())
            {
                for (int i = 0; i < parent.sockets.Count; i++)
                {
                    ModularAttachmentSocket socket = parent.sockets[i];
                    if (socket?.required == true
                        && (!socketCounts.TryGetValue(socket.id, out int count)
                            || count == 0))
                        yield return path + " does not fill required socket "
                            + socket.id + ".";
                }
            }
        }
    }

    /// <summary>
    /// Keeps default-only loadout presets usable before the full recursive weapon preset
    /// applier is present. A newer CompModularWeaponNode instance method takes precedence
    /// over this extension automatically when that implementation is available.
    /// </summary>
    public static class ModularWeaponPresetCompatibility
    {
        public static bool TryApplyPreset(
            this CompModularWeaponNode comp,
            ModularWeaponPresetDef preset,
            out string rejection)
        {
            rejection = null;
            if (comp?.parent?.def == null
                || preset?.weaponDef != comp.parent.def
                || comp.Props?.isAssemblyRoot != true)
            {
                rejection = "The weapon preset does not target this assembly root.";
                return false;
            }

            if (preset.useDefaultConfiguration)
            {
                return true;
            }

            rejection = "This build does not support applying an exact recursive weapon preset.";
            return false;
        }
    }

    public sealed class ModularPawnKindLoadout
    {
        public float weight = 1f;
        public ModularWeaponPresetDef weaponPreset;
        public bool replaceWeaponIfMissing = true;
        public List<ModularArmorPresetDef> armorPresets =
            new List<ModularArmorPresetDef>();
        public bool addMissingArmor;
    }

    /// <summary>
    /// Add this extension to a PawnKindDef. Exactly one weighted loadout is selected
    /// after vanilla gear generation, then its weapon and armor presets are applied.
    /// </summary>
    public sealed class PawnKindModularLoadoutExtension : DefModExtension
    {
        public List<ModularPawnKindLoadout> loadouts =
            new List<ModularPawnKindLoadout>();

        public override IEnumerable<string> ConfigErrors()
        {
            foreach (string error in base.ConfigErrors()) yield return error;
            if (loadouts.NullOrEmpty())
            {
                yield return "PawnKind modular loadout extension has no loadouts.";
                yield break;
            }

            float totalWeight = 0f;
            for (int i = 0; i < loadouts.Count; i++)
            {
                ModularPawnKindLoadout loadout = loadouts[i];
                if (loadout == null)
                {
                    yield return "PawnKind modular loadout extension has a null entry.";
                    continue;
                }
                if (loadout.weight <= 0f)
                    yield return "PawnKind modular loadout entry " + i
                        + " has a non-positive weight.";
                else
                    totalWeight += loadout.weight;
                if (loadout.weaponPreset == null
                    && loadout.armorPresets.NullOrEmpty())
                    yield return "PawnKind modular loadout entry " + i
                        + " does not reference any presets.";
                if (!loadout.armorPresets.NullOrEmpty()
                    && loadout.armorPresets.Any(preset => preset == null))
                    yield return "PawnKind modular loadout entry " + i
                        + " has a null armor preset.";
            }
            if (totalWeight <= 0f)
                yield return "PawnKind modular loadout extension has no selectable entry.";
        }
    }

    [HarmonyPatch(
        typeof(PawnGenerator),
        "GenerateGearFor",
        new[] { typeof(Pawn), typeof(PawnGenerationRequest) })]
    public static class Patch_PawnGenerator_ModularLoadout
    {
        [HarmonyPostfix]
        public static void Postfix(Pawn pawn)
        {
            PawnKindModularLoadoutExtension extension = pawn?.kindDef
                ?.GetModExtension<PawnKindModularLoadoutExtension>();
            ModularPawnKindLoadout loadout = SelectLoadout(extension?.loadouts);
            if (loadout == null) return;

            ApplyWeapon(pawn, loadout);
            ApplyArmor(pawn, loadout);
        }

        private static ModularPawnKindLoadout SelectLoadout(
            List<ModularPawnKindLoadout> loadouts)
        {
            if (loadouts.NullOrEmpty()) return null;
            float total = 0f;
            for (int i = 0; i < loadouts.Count; i++)
                if (loadouts[i] != null && loadouts[i].weight > 0f)
                    total += loadouts[i].weight;
            if (total <= 0f) return null;

            float value = Rand.Value * total;
            for (int i = 0; i < loadouts.Count; i++)
            {
                ModularPawnKindLoadout loadout = loadouts[i];
                if (loadout == null || loadout.weight <= 0f) continue;
                value -= loadout.weight;
                if (value <= 0f) return loadout;
            }
            return loadouts.LastOrDefault(entry => entry != null && entry.weight > 0f);
        }

        private static void ApplyWeapon(Pawn pawn, ModularPawnKindLoadout loadout)
        {
            ModularWeaponPresetDef preset = loadout.weaponPreset;
            if (preset?.weaponDef == null || pawn.equipment == null) return;

            ThingWithComps weapon = pawn.equipment.Primary;
            if (weapon?.def != preset.weaponDef)
            {
                if (!loadout.replaceWeaponIfMissing) return;
                pawn.equipment.DestroyAllEquipment(DestroyMode.Vanish);
                weapon = ThingMaker.MakeThing(
                    preset.weaponDef,
                    GenStuff.DefaultStuffFor(preset.weaponDef)) as ThingWithComps;
                if (weapon == null) return;
                pawn.equipment.AddEquipment(weapon);
            }

            CompModularWeaponNode comp = weapon.TryGetComp<CompModularWeaponNode>();
            if (comp == null || comp.TryApplyPreset(preset, out string rejection)) return;
            Log.Warning("[Helodrace] Could not apply weapon preset " + preset.defName
                + " to " + pawn.kindDef.defName + ": " + rejection);
        }

        private static void ApplyArmor(Pawn pawn, ModularPawnKindLoadout loadout)
        {
            if (loadout.armorPresets.NullOrEmpty() || pawn.apparel == null) return;
            for (int i = 0; i < loadout.armorPresets.Count; i++)
            {
                ModularArmorPresetDef preset = loadout.armorPresets[i];
                if (preset?.armorDef == null) continue;
                Apparel apparel = pawn.apparel.WornApparel
                    .FirstOrDefault(item => item?.def == preset.armorDef);
                if (apparel == null && loadout.addMissingArmor)
                {
                    apparel = ThingMaker.MakeThing(
                        preset.armorDef,
                        GenStuff.DefaultStuffFor(preset.armorDef)) as Apparel;
                    if (apparel != null) pawn.apparel.Wear(apparel, true, false);
                }

                CompModularArmor comp = apparel?.TryGetComp<CompModularArmor>();
                if (comp == null || comp.TryApplyPreset(preset, out string rejection))
                    continue;
                Log.Warning("[Helodrace] Could not apply armor preset " + preset.defName
                    + " to " + pawn.kindDef.defName + ": " + rejection);
            }
        }
    }
}
