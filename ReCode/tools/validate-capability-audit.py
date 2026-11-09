#!/usr/bin/env python3
"""Check audit inventory integrity without interpreting it as authorization."""
import csv
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
print('PASS capability inventory: unique proposed requirements and complete initial device action crosswalk')
