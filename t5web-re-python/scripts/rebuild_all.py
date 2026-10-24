#!/usr/bin/env python3
"""
scripts/rebuild_all.py — one-shot verification pipeline.

  1. verify T5V3.Model.dll against its REA evidence (table baked in);
  2. verify T5V3Web.dll (no learning — the table already covers it);
  3. walk-clean the remaining six T5 DLLs;
  4. (optional, --learn) re-run the learner on Web and re-dump the table.

Usage:  .venv/bin/python scripts/rebuild_all.py [--learn]
"""
import importlib.util
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
BIN = '/home/user/IO/T5Web [3.5.7]/App/bin'

spec = importlib.util.spec_from_file_location(
    'cil', os.path.join(ROOT, 'tools', 'cil.py'))
cil = importlib.util.module_from_spec(spec)
spec.loader.exec_module(cil)


def main():
    learn = '--learn' in sys.argv
    failures = 0
    for dll, ev, label in [
        ('T5V3.Model.dll', 't5v3-model-members.json', 'MODEL'),
        ('T5V3Web.dll', 't5v3web-members.json', 'WEB'),
    ]:
        c = cil.CIL(os.path.join(BIN, dll),
                    os.path.join(ROOT, 'evidence', ev))
        if learn:
            c.learn(max_rounds=40, verbose=True)
        r = c.verify()
        status = 'OK' if r['fail'] == 0 else 'FAIL'
        print(f'{status} {label}: pass={r["pass"]} fail={r["fail"]} '
              f'unverifiable={r["unverifiable"]}')
        for tok, why in r['failures'][:10]:
            print(f'    {hex(tok)}: {why}')
        failures += r['fail']
        if learn:
            with open(os.path.join(ROOT, 'tables', 't5v3-full.json'), 'w') as f:
                json.dump(c.dump_table(), f, indent=1)
            print('   table re-dumped -> tables/t5v3-full.json')

    print('--- walk-clean (no evidence) ---')
    total = 0
    for dll in ['T5V3.Model.dll', 'T5V3Web.dll', 'T5V3.Attendance.dll',
                'T5V3.DB.dll', 'T5V3.LeaveCalcs.dll', 'T5V3.Misc.dll',
                'T5V3.Report.dll', 'T5V3.ReportGenerator.dll']:
        c = cil.CIL(os.path.join(BIN, dll), None)
        ok = bad = 0
        for m in c.methods_list:
            if m['il'] is None:
                continue
            try:
                c.decode_il(m['il'])
                ok += 1
            except Exception:
                bad += 1
        total += ok + bad
        print(f'   {dll}: {ok}/{ok+bad}')
    print(f'TOTAL: {total} methods')
    return 1 if failures else 0


if __name__ == '__main__':
    sys.exit(main())
