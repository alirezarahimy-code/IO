#!/usr/bin/env python3
"""Check audit inventory integrity without interpreting it as authorization."""
import csv
from collections import Counter
from pathlib import Path

root = Path(__file__).resolve().parents[1] / 'docs/pages-first/audit'
def read(name):
    with (root / name).open(encoding='utf-8', newline='') as f:
        return list(csv.DictReader(f))

requirements = read('capability-requirements.csv')
ids = [r['requirement_id'] for r in requirements]
assert ids and len(ids) == len(set(ids))
assert all(r['implementation'] == 'planned-not-grantable' for r in requirements)
legacy = read('legacy-actions.csv')
assert sum(r['source'] == 'T5Web-original-metadata' for r in legacy) == 343
assert all(r['review'] == 'unreviewed' for r in legacy)
target = {(r['controller'], r['method']) for r in legacy
          if r['source'] == 'T5Web-original-metadata' and r['controller'] in ('Device', 'DeviceOperations', 'iclock')}
review = read('t5-device-actions-review.csv')
reviewed = [(r['controller'], r['action']) for r in review]
assert len(reviewed) == len(set(reviewed)) and set(reviewed) == target
assert all(r['review_status'] == 'initial-review-contract-unverified' for r in review)
assert all(set(r['requirements'].split(';')) <= set(ids) for r in review)
stage1 = read('stage1-twf-actions.csv')
expected = Counter((r['source'], r['path'], r['line'], r['method']) for r in legacy
                   if r['source'] in ('TWF-original-decompiled', 'Aria'))
actual = Counter((r['source'], r['path'], r['line'], r['method']) for r in stage1)
assert actual == expected
assert all(r['area_fa'] and r['method_review'] == 'inventory-only' and
           r['implementation_status'] == 'not-assessed-per-method' for r in stage1)
views = read('stage1-view-comparison.csv')
assert views and len({r['relative_view'] for r in views}) == len(views)
assert all(r['behavior_review'] == 'inventory-only' for r in views)
assert all(r['path_comparison'] == ('both' if r['twf_original_path'] and r['aria_path'] else
           'twf-only' if r['twf_original_path'] else 'aria-only') for r in views)
print('PASS capability inventory: proposed requirements, device crosswalk, stage-one declaration and view coverage')
