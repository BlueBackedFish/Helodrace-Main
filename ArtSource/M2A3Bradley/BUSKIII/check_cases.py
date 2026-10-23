"""SAT check of exterior case bodies; mounting standoffs intentionally touch armor."""
from itertools import combinations
import json
from pathlib import Path
import numpy as np


def overlaps(records, tolerance=.002):
    failures = []
    for a, b in combinations(records, 2):
        ac, bc = np.array(a['case_center']), np.array(b['case_center'])
        ar, br = np.array(a['rotation']), np.array(b['rotation'])
        ah, bh = np.array(a['size'])/2, np.array(b['size'])/2
        delta = bc-ac
        axes = [*ar.T, *br.T, *[np.cross(x,y) for x in ar.T for y in br.T]]
        penetration = float('inf')
        for axis in axes:
            norm = np.linalg.norm(axis)
            if norm < 1e-7:
                continue
            axis = axis/norm
            depth = sum(abs(ar.T@axis)*ah)+sum(abs(br.T@axis)*bh)-abs(delta@axis)
            penetration = min(penetration, depth)
            if depth <= tolerance:
                break
        else:
            failures.append([a['name'], b['name'], float(penetration)])
    return failures


if __name__ == '__main__':
    manifest = json.loads((Path(__file__).parent/'output/manifest.json').read_text())
    failures = overlaps(manifest['objects'])
    print(json.dumps(failures, indent=2))
    print('case collisions:', len(failures))
    raise SystemExit(bool(failures))
