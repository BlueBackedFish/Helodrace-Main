"""Validate bare modular parts and the complete, root-only spawn presets."""
from pathlib import Path
import re
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[3]
COMP = 'Helodrace.ModernWar.CompProperties_ModularWeaponNode'


def load():
    parts, presets = {}, {}
    for path in (ROOT / 'Defs').rglob('*.xml'):
        document = ET.parse(path).getroot()
        assert not document.findall('.//defaultAttachments'), str(path)
        for node in document:
            name = node.findtext('defName')
            if node.tag == 'ThingDef' and name:
                comp = node.find("comps/li[@Class='%s']" % COMP)
                if comp is not None:
                    parts[name] = comp
            elif node.tag == 'Helodrace.ModernWar.ModularWeaponPresetDef':
                assert name not in presets, name
                assert node.find('useDefaultConfiguration') is None, name
                presets[name] = node
    return parts, presets


def canonical(entries):
    return [dict(part=e.findtext('part'), socket=e.findtext('socketId'),
                 mount=e.findtext('mountId'), offset=float(e.findtext('railOffset', '0')),
                 children=canonical(e.find('children')))
            for e in entries] if entries is not None else []


def validate():
    parts, presets = load()
    roots = {name: comp for name, comp in parts.items() if comp.findtext('isAssemblyRoot') == 'true'}
    categories = {name: comp.findtext('partCategory', 'Optional') for name, comp in parts.items()}
    patch = ET.parse(ROOT / 'Patches/ModularWeapon_PartCategories.xml')
    for op in patch.getroot():
        category = op.findtext('value/partCategory')
        if category:
            for name in re.findall(r'defName="([^"]+)"', op.findtext('xpath', '')):
                categories[name] = category

    expectations = 0
    for name, comp in parts.items():
        if name in roots:
            spawn = comp.findtext('spawnPreset')
            assert spawn in presets, (name, spawn)
            assert presets[spawn].findtext('weaponDef') == name, (name, spawn)
        else:
            assert comp.find('spawnPreset') is None, name
        for socket in comp.findall('sockets/li'):
            expected = socket.findtext('expectedPart')
            if expected:
                assert expected in parts, (name, expected)
                assert categories[expected] in ('Required', 'Functional'), (name, expected)
                expectations += 1

    attachments = 0
    def check_tree(parent, entries, depth=0):
        nonlocal attachments
        assert depth < 32, parent
        comp = parts[parent]
        sockets = {s.findtext('id'): s for s in comp.findall('sockets/li')}
        counts = {}
        for entry in entries if entries is not None else []:
            part, socket_id = entry.findtext('part'), entry.findtext('socketId')
            assert part in parts and socket_id in sockets, (parent, part, socket_id)
            socket = sockets[socket_id]
            mounts = parts[part].findall('mounts/li')
            mount_id = entry.findtext('mountId')
            mount = next((m for m in mounts if m.findtext('id') == mount_id), None) if mount_id else (mounts[0] if mounts else None)
            assert mount is not None, (part, mount_id)
            tags = {t.text for t in socket.findall('tags/li')}
            accepts = {t.text for t in mount.findall('socketTags/li')}
            assert not tags or not accepts or tags & accepts, (parent, socket_id, part, mount_id)
            counts[socket_id] = counts.get(socket_id, 0) + 1
            limit = int(socket.findtext('maxAttachments', '0')) if socket.findtext('isRail') == 'true' else 1
            assert limit <= 0 or counts[socket_id] <= limit, (parent, socket_id)
            attachments += 1
            check_tree(part, entry.find('children'), depth + 1)
        for socket_id, socket in sockets.items():
            assert socket.findtext('required') != 'true' or counts.get(socket_id, 0), (parent, socket_id, 'required socket empty')

    for name, preset in presets.items():
        weapon = preset.findtext('weaponDef')
        assert weapon in roots, name
        assert len(preset.find('parts')) > 0, name
        check_tree(weapon, preset.find('parts'))
    print('PASS: %d bare parts, %d complete weapon roots, %d exact presets, %d attachments, %d missing-part expectations.'
          % (len(parts) - len(roots), len(roots), len(presets), attachments, expectations))
    return parts, presets, roots


def validate_ammunition_textures():
    """Check item icons, feed animations and casing motes against shipped assets."""
    prefix = 'Weapons/Modular/Ammunition/'
    available = {p.relative_to(ROOT / 'Textures').with_suffix('').as_posix()
                 for p in (ROOT / 'Textures' / prefix).rglob('*.png')}
    missing, references, used = [], 0, set()
    for folder in ('Defs', 'Patches'):
        for path in (ROOT / folder).rglob('*.xml'):
            for node in ET.parse(path).getroot().iter():
                texture = (node.text or '').strip()
                if node.tag not in ('texPath', 'feedingRoundTexPath', 'uiIconPath') or not texture.startswith(prefix):
                    continue
                references += 1
                used.add(texture)
                if texture not in available:
                    missing.append('%s: %s' % (path.relative_to(ROOT), texture))
    assert references > 0, 'No modular ammunition textures were checked.'
    assert not missing, 'Missing ammunition textures:\n' + '\n'.join(missing)
    print('PASS: %d ammunition texture references resolve to %d shipped icons, feeding rounds and casings.'
          % (references, len(used)))


if __name__ == '__main__':
    validate()
    validate_ammunition_textures()
