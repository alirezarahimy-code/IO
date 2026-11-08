#!/usr/bin/env python3
"""Expose only named successful SQL checks in an Actions annotation."""
import pathlib
import sys

lines = [line.strip() for line in pathlib.Path(sys.argv[1]).read_text(encoding='utf-8').splitlines()
         if line.startswith('PASS ')]
if len(lines) != 49 or lines[-1] != 'PASS disposable SQL Server integration checks':
    raise SystemExit('Unexpected SQL check output; refusing to report success.')
message = ' | '.join(lines).replace('%', '%25').replace('\r', '%0D').replace('\n', '%0A')
print('::notice title=Phase 3 SQL checks::' + message)
