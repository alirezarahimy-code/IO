#!/usr/bin/env python3
"""Expose only named successful SQL/HTTPS checks in Actions annotations."""
import pathlib
import sys

lines = [line.strip() for line in pathlib.Path(sys.argv[1]).read_text(encoding='utf-8').splitlines()
         if line.startswith('PASS ')]
if len(lines) != 365 or lines[-1] != 'PASS disposable SQL Server integration checks':
    raise SystemExit('Unexpected integration check output; refusing to report success.')
chunks = []
current = ''
for line in lines:
    piece = (' | ' if current else '') + line
    if len(current) + len(piece) > 2800:
        chunks.append(current)
        current = line
    else:
        current += piece
if current:
    chunks.append(current)
for index, chunk in enumerate(chunks, 1):
    message = chunk.replace('%', '%25').replace('\r', '%0D').replace('\n', '%0A')
    print(f'::notice title=SQL and HTTPS checks {index}/{len(chunks)}::{message}')
