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
    conflicts = {}
    for name, comp in parts.items():
        excluded = [entry.text for entry in comp.findall('incompatibleParts/li')]
        assert len(excluded) == len(set(excluded)), (name, 'duplicate incompatible part')
        assert all(part in parts and part != name for part in excluded), (name, excluded)
        conflicts[name] = set(excluded)
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
        installed = [weapon] + [p.text for p in preset.findall('.//part')]
        for i, first in enumerate(installed):
            for second in installed[i + 1:]:
                assert second not in conflicts[first] and first not in conflicts[second], (name, first, second, 'conflicting preset')
    wood = 'HD_ModularPart_Stock_M14Wood'
    socom = 'HD_ModularPart_Stock_Socom16_Part'
    ebr = {'HD_ModularPart_Handguard_MK14EBR', 'HD_ModularPart_PistolGrip_MK14EBR', 'HD_ModularPart_Stock_MK14EBR'}
    assert ebr <= conflicts[wood] and ebr <= conflicts[socom]
    assert 'HD_ModularPart_Barrel_M1A16_Part' in conflicts[wood]
    short_barrel = 'HD_ModularPart_Barrel_M1A16_Part'
    chassis = 'HD_ModularPart_Handguard_MK14EBR'
    assert short_barrel not in conflicts[socom] and socom not in conflicts[short_barrel], 'SOCOM16 stock must accept its 16-inch barrel'
    assert short_barrel in conflicts[chassis] or chassis in conflicts[short_barrel], 'EBR chassis must reject the 16-inch barrel'
    for barrel in ('HD_ModularPart_Barrel_M1418_Part', 'HD_ModularPart_Barrel_M1422_Part'):
        assert barrel not in conflicts[chassis] and chassis not in conflicts[barrel], (chassis, barrel, 'longer barrels remain compatible')
    print('PASS: %d authored conflicts; every shipped preset has a compatible full assembly.' % sum(map(len, conflicts.values())))
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



def validate_ammunition_labels():
    """Keep English/Korean item names and installed-ammo readouts consistent."""
    expected = {
        'HD_Ammunition_556M193_Ammo': 'FMJ',
        'HD_Ammunition_556M855_Ammo': 'FMJ',
        'HD_Ammunition_556M995_Ammo': 'AP',
        'HD_Ammunition_556MK262_Ammo': 'MATCH',
        'HD_Ammunition_556MK318_Ammo': 'OTM',
        'HD_Ammunition_556M855A1_Ammo': 'EPR',
        'HD_Ammunition_300BLKTACTX': 'EXP',
        'HD_Ammunition_919M882_Ammo': 'FMJ',
        'HD_Ammunition_76251M80A1_Ammo': 'EPR',
        'HD_Ammunition_76251M198_Ammo': 'DUPLEX',
        'HD_Ammunition_76251M948_Ammo': 'SLAP',
        'HD_Ammunition_76251M993_Ammo': 'AP',
        'HD_Ammunition_76251M1158_Ammo': 'ADVAP',
        'HD_Ammunition_76251MK316_Ammo': 'MATCH',
        'HD_Ammunition_76251MK319_Ammo': 'OTM',
        'HD_ModularPart_Ammunition_762M80_Part': 'FMJ',
        'HD_ModularPart_Ammunition_45ACPM1911_Part': 'FMJ',
        'HD_ModularPart_Ammunition_919M882_P320_Part': 'FMJ',
        'HD_ModularPart_Ammunition_M249_M855A1_Part': 'EPR',
    }
    translated = {}
    for path in (ROOT / 'Languages' / 'Korean (한국어)' / 'DefInjected' / 'ThingDef').glob('*.xml'):
        for entry in ET.parse(path).getroot():
            if entry.tag.endswith(('.label', '.comps.0.ammunitionType')):
                translated[entry.tag] = entry.text or ''

    found = set()
    for path in (ROOT / 'Defs').rglob('*.xml'):
        for node in ET.parse(path).getroot().findall('ThingDef'):
            comp = node.find("comps/li[@Class='%s']" % COMP)
            if comp is None or comp.find('ammunitionType') is None:
                continue
            name = node.findtext('defName')
            assert name in expected, (name, 'new ammunition needs a classification label')
            suffix = ' (%s)' % expected[name]
            assert node.findtext('label', '').endswith(suffix), (name, 'English label')
            assert comp.findtext('ammunitionType', '').endswith(suffix), (name, 'English ammo readout')
            assert translated.get(name + '.label', '').endswith(suffix), (name, 'Korean label')
            assert translated.get(name + '.comps.0.ammunitionType', '').endswith(suffix), (name, 'Korean ammo readout')
            found.add(name)
    assert found == set(expected), ('missing ammunition labels', set(expected) - found)
    print('PASS: %d ammunition selections have matching class suffixes in English/Korean names and readouts.' % len(found))

if __name__ == '__main__':
    validate()
    validate_ammunition_textures()
    validate_ammunition_labels()
