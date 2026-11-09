#!/usr/bin/env python3
"""Temporary transfer of official EF scaffold from isolated Actions runner."""
import base64
import gzip
import io
from pathlib import Path
import tarfile

root = Path('src/IO.Infrastructure/Migrations')
files = sorted(root.glob('*_MappingChanges*.cs')) + [root / 'IoDbContextModelSnapshot.cs']
if len(files) != 3 or not all(f.is_file() for f in files):
    raise SystemExit('Expected migration, designer and snapshot')
buffer = io.BytesIO()
with tarfile.open(fileobj=buffer, mode='w') as tar:
    for file in files:
        tar.add(file, arcname=file.name)
encoded = base64.b64encode(gzip.compress(buffer.getvalue())).decode('ascii')
chunks = [encoded[n:n+3000] for n in range(0, len(encoded), 3000)]
print(f'::notice title=MAPPING_SCAFFOLD_COUNT::{len(chunks)}')
for i, chunk in enumerate(chunks):
    print(f'::notice title=MAPPING_SCAFFOLD_{i:03}::{chunk}')
