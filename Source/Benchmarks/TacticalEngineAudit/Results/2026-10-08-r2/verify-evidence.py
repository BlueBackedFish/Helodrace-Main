"""Verify raw hashes and replay four comparisons without launching RimWorld."""
import argparse
import gzip
import hashlib
import json
from pathlib import Path
import subprocess

p = argparse.ArgumentParser()
p.add_argument('--output', required=True, help='Fresh directory below CreatorTemp')
a = p.parse_args()
here = Path(__file__).resolve().parent
out = Path(a.output).resolve()
allowed = Path('C:/Users/Public/Documents/ESTsoft/CreatorTemp').resolve()
assert out.is_relative_to(allowed) and out != allowed and not out.exists()
out.mkdir(parents=True)
inventory = json.loads((here / 'raw-manifest.json').read_text(encoding='utf-8'))
for record in inventory:
    relative = Path(record['path'])
    assert not relative.is_absolute() and '..' not in relative.parts
    data = gzip.decompress((here / relative).read_bytes())
    assert len(data) == record['originalBytes']
    assert hashlib.sha256(data).hexdigest() == record['sha256']
    target = (out / relative).with_suffix('')
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(data)
repo = here.parents[4]
cli = repo / 'Source/Tools/AgentProfiler/bin/Debug/net10.0/AgentProfiler.dll'
for size in (50, 200):
    root = out / 'raw/verified' / f'hd-r2-matrix{size}-v9-20261008'
    for workload in ('open-approach', 'sapper-wall'):
        result = subprocess.run(['C:/Program Files/dotnet/dotnet.exe', str(cli), 'engine-compare',
            str(root / workload / 'vanilla'), str(root / workload / 'new')], check=True, capture_output=True)
        comparison = json.loads(result.stdout)
        assert not comparison['unmatchedBaseline'] and not comparison['unmatchedCandidate']
        assert len(comparison['groups']) == 1
        group = comparison['groups'][0]
        assert group['newAiFixedWindowCpuGatePassed'] and group['baselineRuns'] == group['candidateRuns'] == 3
        original = json.loads((here / f'low-{size}-{workload}-comparison.json').read_text(encoding='utf-8-sig'))['groups'][0]
        for metric in ('baselineTickCpuMs', 'candidateTickCpuMs', 'tickCpuRatio',
                       'baselineWindowCpuMs', 'candidateWindowCpuMs', 'windowCpuRatio'):
            assert group[metric] == original[metric], metric
        (out / f'comparison-{size}-{workload}.json').write_bytes(result.stdout)
        print(f'PASS {size}/{workload}: tick={group["tickCpuRatio"]:.6f}, window={group["windowCpuRatio"]:.6f}')
print(f'PASS: {len(inventory)} raw file hashes, four reproducible comparisons. Extracted: {out}')
