#!/usr/bin/env python3
"""Reproducible discovery inventory, not an authorization or behavioral analyzer."""
import csv
import io
import pathlib
import re
import subprocess

ROOT = pathlib.Path(__file__).resolve().parents[2]
COMMIT = '2c01d1074ebf18adc3c43063dd14dbe639246e04'
OUT = ROOT / 'ReCode/docs/pages-first/audit/legacy-actions.csv'

def git(*args):
    return subprocess.check_output(['git', *args], cwd=ROOT, text=True, encoding='utf-8')

def source(path):
    return git('show', COMMIT + ':' + path)

rows = []
t5path = 'src/T5Web/docs/legacy-actions.csv'
for r in csv.DictReader(io.StringIO(source(t5path))):
    rows.append({'source': 'T5Web-original-metadata', 'path': 'T5Web [3.5.7]/App/bin/T5V3Web.dll',
                 'controller': r['controller'], 'method': r['action'], 'line': '', 'evidence': 'metadata; historical rewrite decision not adopted',
                 'review': 'unreviewed', 'capability': '', 'permission': ''})

# Syntax discovery, not a semantic C# parser: includes public helpers, overloads
# and potential non-actions. No body, parameters or literals are exported.
decl = re.compile(r'^\s*public\s+(?:async\s+)?(?:override\s+)?(?:virtual\s+)?(?:[\w.<>?,\[\]]+\s+)+(?P<name>\w+)\s*\(')
for label, prefix in [
    ('TWF-original-decompiled', 'src/TWF/OriginalDecompiled/MyTime3/MyTime3.Controllers'),
    ('Aria', 'src/TWF/TWF.Web/Controllers'),
    ('AriaAdmin', 'src/T5Web/T5Web.Web/Controllers'),
]:
    files = git('ls-tree', '-r', '--name-only', COMMIT, prefix).splitlines()
    for path in files:
        if not path.endswith('Controller.cs'):
            continue
        lines = source(path).splitlines()
        for line_no, text in enumerate(lines, 1):
            m = decl.match(text)
            if not m:
                continue
            controller = pathlib.PurePosixPath(path).stem.removesuffix('Controller')
            rows.append({'source': label, 'path': path, 'controller': controller, 'method': m.group('name'),
                         'line': str(line_no), 'evidence': 'public declaration; action/verb/body unverified',
                         'review': 'unreviewed', 'capability': '', 'permission': ''})

fields = ['source', 'path', 'controller', 'method', 'line', 'evidence', 'review', 'capability', 'permission']
OUT.parent.mkdir(parents=True, exist_ok=True)
with OUT.open('w', encoding='utf-8', newline='') as f:
    writer = csv.DictWriter(f, fieldnames=fields, lineterminator='\n')
    writer.writeheader()
    writer.writerows(rows)
print(f'{len(rows)} discovery rows written to {OUT.relative_to(ROOT)}')
