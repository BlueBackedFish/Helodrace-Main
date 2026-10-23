"""Check the delivered files/indices/archive against the requested asset states."""
from pathlib import Path
import hashlib,json,zipfile
from PIL import Image

HERE=Path(__file__).resolve().parent
OUT=HERE/'output'
cfg=json.loads((HERE/'settings.json').read_text())
expected=set(json.loads((HERE.parent/'BUSKIII/output/manifest.json').read_text())['counts'])
tile_ids={r['name'] for r in json.loads((HERE.parent/'BUSKIII/output/manifest.json').read_text())['objects']}
results={}
for variant,source in cfg['sources'].items():
    assert hashlib.sha256((HERE/source['path']).resolve().read_bytes()).hexdigest()==source['sha256']
    quality=json.loads((OUT/'1024'/variant/'package_validation.json').read_text())
    assert quality['status']=='passed',(variant,quality['status'])
    rendered=json.loads((OUT/'1024'/variant/'render_manifest.json').read_text())
    for name,metadata in rendered['files'].items():
        if name.startswith('ERA/'):
            assert not (set(metadata['holdout_objects'])&tile_ids),'A neighboring module was baked into a tile mask'
        if name.startswith(('Hull/','Turret/','Parts/')):
            assert not (set(metadata['visible_objects'])&tile_ids),'ERA baked into base sprite'
    assert rendered['BUSK_body_includes_ERA'] is False
    assert rendered['tow_deployed_paint'][0]['repainted_faces']>0
    results[variant]={'source_unchanged':True,'sizes':{},'stable_composition_max_error_255':max(
        t['stable_max_rgb_error_255'] for d in quality['directions'].values() for t in d['composition'].values())}
    for size in cfg['sizes']:
        root=OUT/str(size)/variant
        index=json.loads((root/'index.json').read_text())
        assert not index['outline'] and not index['shadows']
        assert set(index['directions'])=={'east','west','north','south'}
        files=list(root.rglob('*.png'))
        assert len(files)==quality['PNG_count_per_size']
        for file in files:
            with Image.open(file) as im:
                assert im.mode=='RGBA' and im.size==(size,size),file
                assert im.getpixel((0,0))[3]==0,file
                bounds=im.getchannel('A').getbbox()
                if bounds:assert min(bounds[0],bounds[1],size-bounds[2],size-bounds[3])>=2,file
        for direction,info in index['directions'].items():
            assert len(info['hull_states'])==4 and len(info['turret_states'])==2 and len(info['previews'])==8
            assert set(info['era'])==(tile_ids if variant=='BUSK' else set())
            assert len(info['parts'])==9
            assert info['turret_pivot']==[size/2]*2
            assert set(info['hull_control_pivots'])=={'roof_hatch','rear_ramp'}
            assert set(info['turret_control_pivots'])=={'tow','gun'}
            for file in [*info['parts'].values(),*info['previews'].values()]:assert (root/file).is_file(),file
            for group,expected_parent,expected_count in [('hull_states','hull',109),('turret_states','turret',15)]:
                for state,spec in info[group].items():
                    assert (root/spec['file']).is_file()
                    assert (root/spec['assembled']).is_file()
                    assert len(spec['era_files'])==(expected_count if variant=='BUSK' else 0)
                    actual={entry['states'][state] for entry in info['era'].values() if entry['parent']==expected_parent}
                    assert set(spec['era_files'])==actual
                    assert len(actual)==len(spec['era_files'])
                    for file in actual:assert (root/file).is_file(),file
            for entry in info['era'].values():
                assert (root/entry['default_file']).is_file()
                assert entry['default_file'].endswith('_'+direction+'.png')
                assert entry['family'] in expected
        east=Image.open(root/index['directions']['east']['hull_states']['roof_closed_ramp_closed']['file']).convert('RGBA')
        west=Image.open(root/index['directions']['west']['hull_states']['roof_closed_ramp_closed']['file']).convert('RGBA')
        assert east.transpose(Image.Transpose.FLIP_LEFT_RIGHT).tobytes()!=west.tobytes(),'WEST is only a mirrored EAST'
        north=Image.open(root/index['directions']['north']['hull_states']['roof_closed_ramp_closed']['file']).convert('RGBA')
        south=Image.open(root/index['directions']['south']['hull_states']['roof_closed_ramp_closed']['file']).convert('RGBA')
        assert north.transpose(Image.Transpose.ROTATE_180).tobytes()!=south.tobytes(),'SOUTH is only a rotated NORTH'
        results[variant]['sizes'][str(size)]={'PNGs':len(files),'ERA_modules_per_direction':len(index['directions']['east']['era']),
                                            'default_ERA_views':sum(len(info['era']) for info in index['directions'].values()),
                                            'states_per_direction':8,'outline':False,'rgba_transparent':True}
with zipfile.ZipFile(OUT/'Bradley_TexturePackage.zip') as archive:
    assert archive.testzip() is None
    assert not any('/'+folder+'/' in name for name in archive.namelist() for folder in ['Assembled','ReferenceInstalled','Preview'])
    for size in [256,512]:
        for variant in cfg['sources']:
            root=OUT/str(size)/variant
            for folder in ['Hull','Turret','Parts','ERA']:
                for file in (root/folder).rglob('*.png'):
                    assert archive.read(file.relative_to(OUT).as_posix())==file.read_bytes(),file
            runtime=json.loads(archive.read(f'{size}/{variant}/index.json'))
            assert runtime['runtime_assets_only']
            assert set(runtime['directions'])==set(cfg['directions'])
            for info in runtime['directions'].values():
                assert 'previews' not in info
                for group in ['hull_states','turret_states']:
                    for state in info[group].values():
                        assert 'assembled' not in state and 'installed_reference' not in state
                        for file in [state['file'],*state['era_files']]:
                            assert f'{size}/{variant}/{file}' in archive.namelist()
    assert archive.read('README.md')==(HERE/'README.md').read_bytes()
    assert 'BUSK_REFERENCES.md' in archive.namelist()
    archive_entries=len(archive.namelist())
result={'status':'passed','variants':results,'archive_entries':archive_entries,
        'archive_contains_only_unarmored_bases_and_separate_modules':True,
        'camera_directions_independent':True,'module_neighbors_not_baked_as_holdouts':True,
        'small_personnel_door':'closed, included in rear ramp',
        'outline':False,'shadows':False,'game_validation_performed':False,
        'AA_note':'Independent source-over layers have subpixel boundary differences; full-state renders are also supplied'}
(OUT/'validation.json').write_text(json.dumps(result,indent=2)+'\n')
print('TEXTURE PACKAGE VALIDATION PASSED',json.dumps(results))
