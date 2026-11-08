#!/usr/bin/env python3
"""Publish generated source as CI annotations when runner logs are unreachable.

Only files generated under our migrations folder are packaged. No environment,
SQL data, configuration, binaries, or credentials are included.
"""
import base64
import gzip
import io
import pathlib
import tarfile

folder = pathlib.Path('src/IO.Infrastructure/Migrations')
files = sorted(folder.glob('*.cs'))
if len(files) != 3 or not any(x.name.endswith('ModelSnapshot.cs') for x in files):
    raise SystemExit('Unexpected migration scaffold file list.')
buffer = io.BytesIO()
with tarfile.open(fileobj=buffer, mode='w') as archive:
    for file in files:
        archive.add(file, arcname=file.name, recursive=False)
encoded = base64.b64encode(gzip.compress(buffer.getvalue(), mtime=0)).decode('ascii')
chunks = [encoded[i:i + 7000] for i in range(0, len(encoded), 7000)]
if len(chunks) > 8:
    raise SystemExit(f'Scaffold exceeds annotation limit: {len(chunks)} chunks')
print(f'::notice title=EF_SCAFFOLD_COUNT::{len(chunks)}')
for index, chunk in enumerate(chunks):
    print(f'::notice title=EF_SCAFFOLD_{index:03}::{chunk}')
