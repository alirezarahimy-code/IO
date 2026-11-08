#!/usr/bin/env python3
# Temporary transfer of the official scaffold from a trusted Actions runner.
import base64
import gzip
import io
from pathlib import Path
import tarfile

files = sorted(Path('src/IO.Infrastructure/IdentityMigrations').glob('*.cs'))
if len(files) != 3:
    raise SystemExit('Expected 3 scaffold files')
buffer = io.BytesIO()
with tarfile.open(fileobj=buffer, mode='w') as tar:
    for file in files:
        tar.add(file, arcname=file.name)
encoded = base64.b64encode(gzip.compress(buffer.getvalue())).decode('ascii')
chunks = [encoded[n:n+3000] for n in range(0, len(encoded), 3000)]
print(f'::notice title=IDENTITY_SCAFFOLD_COUNT::{len(chunks)}')
for i, chunk in enumerate(chunks):
    print(f'::notice title=IDENTITY_SCAFFOLD_{i:03}::{chunk}')
