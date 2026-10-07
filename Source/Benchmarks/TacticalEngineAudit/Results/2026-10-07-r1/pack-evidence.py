import argparse, gzip, hashlib, json, statistics
from pathlib import Path

p = argparse.ArgumentParser()
p.add_argument('--output', required=True)
p.add_argument('--verified', action='append', default=[])
p.add_argument('--excluded', action='append', default=[])
a = p.parse_args()
out = Path(a.output)
out.mkdir(parents=True, exist_ok=True)
records, inventory = [], []
def load(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))
for classification, sources in [('verified', a.verified), ('excluded', a.excluded)]:
    for source in sources:
        root = Path(source)
        for path in sorted(root.rglob('*')):
            if not path.is_file() or not (path.suffix in ('.json', '.log') or path.name == 'pawn-manifest.txt'):
                continue
            if any(part in ('Saves', 'Worlds') for part in path.relative_to(root).parts):
                continue
            data = path.read_bytes()
            relative = Path('raw') / classification / root.name / path.relative_to(root)
            dest = out / str(relative).replace('\\', '/')
            dest = dest.with_name(dest.name + '.gz')
            dest.parent.mkdir(parents=True, exist_ok=True)
            dest.write_bytes(gzip.compress(data, mtime=0))
            inventory.append(dict(path=dest.relative_to(out).as_posix(), originalBytes=len(data), sha256=hashlib.sha256(data).hexdigest()))
        for path in sorted(root.rglob('audit.json')):
            audit = load(path)
            row = dict(classification=classification, source=root.name + '/' + path.parent.relative_to(root).as_posix(), audit=audit)
            captures = list((path.parent / 'profiles').glob('capture-*.json'))
            if captures:
                assert len(captures) == 1, (path, captures)
                c = load(captures[0])
                ticks = c['endTick'] - c['startTick']
                ref = statistics.median(c['reference']['sampleMs']) if c.get('reference') else None
                methods = []
                for m in c['methods']:
                    methods.append(dict(method=m['method'], calls=m['calls'], inclusiveMs=m['inclusiveMs'],
                        selfMs=m['trackedSelfMs'], averageMs=m['inclusiveMs']/m['calls'] if m['calls'] else None,
                        maxMs=m['maxMs'], p95Ms=m['p95Ms'], p99Ms=m['p99Ms'], callsPerTick=m['calls']/ticks,
                        msPerTick=m['inclusiveMs']/ticks, corePercentPerTick=m['inclusiveMs']/ticks/ref*100 if ref else None,
                        cpuMeasured=m['cpuMeasured'], threadCpuMs=m['threadCpuMs']))
                row.update(build=c['assemblySha256'], ticks=ticks, frames=c['endFrame']-c['startFrame'],
                    complete=c['complete'], dropped=c['dropped'], coreBatchMs=ref,
                    mainWindowCpuMs=c.get('mainThreadWindowCpuMs'), processWindowCpuMs=c.get('processWindowCpuMs'), methods=methods)
                if classification == 'verified':
                    assert audit['complete'] and audit['isolationVerified'] and audit['error'] is None
                    assert c['complete'] and c['dropped'] == 0 and not any(m['exceptions'] for m in c['methods'])
                    assert ticks >= audit['sampleTicks'] and ticks <= audit['sampleTicks'] + 10
                    tick = next(m for m in c['methods'] if m['method'].startswith('Verse.TickManager.DoSingleTick('))
                    assert tick['calls'] == ticks and tick['threadCpuMs'] > 0
                    legacy_hits = [m for m in c['methods'] if m['calls'] and m['method'].startswith((
                        'Helodrace.MapComponent_Raid', 'Helodrace.GameComponent_Raid', 'Helodrace.MapComponent_TacticalMapAnalysis'))]
                    row['legacyMethodsWithCalls'] = len(legacy_hits)
                    if audit['engine'] != 'legacy':
                        assert not legacy_hits and not audit['legacyComponents'] and not audit['installedLegacyHooks']
                    else:
                        assert len(audit['legacyComponents']) == 11 and audit['installedLegacyHooks'] and legacy_hits
            elif classification == 'verified':
                assert audit['complete'] and audit['isolationVerified'] and audit['error'] is None
                assert not (path.parent / 'profiles').exists(), 'No-profile control installed profiler'
                assert audit['uninstrumentedMainCpuMs'] > 0 and audit['uninstrumentedProcessCpuMs'] > 0
            records.append(row)
(out / 'measurements.json').write_text(json.dumps(dict(warning='Method times are elapsed; CPU measured at whole tick/window. Inclusive scopes overlap. New R1 is vanilla fallback.', runs=records), ensure_ascii=False, indent=2), encoding='utf-8')
(out / 'raw-manifest.json').write_text(json.dumps(inventory, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(dict(runs=len(records), files=len(inventory), bytes=sum(f.stat().st_size for f in out.rglob('*.gz')))))
