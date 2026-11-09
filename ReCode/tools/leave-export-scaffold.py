#!/usr/bin/env python3
"""Export EF-generated migration from the disposable Actions runner via annotations."""
import base64, gzip, io, tarfile
from pathlib import Path
root = Path('src/IO.Infrastructure/Migrations')
files = sorted(root.glob('*_LeaveLedger*.cs')) + [root / 'IoDbContextModelSnapshot.cs']
if len(files) != 3 or not all(f.is_file() for f in files):
    raise SystemExit('Expected migration, designer and snapshot')
buf = io.BytesIO()
with tarfile.open(fileobj=buf, mode='w') as archive:
    for file in files:
        archive.add(file, arcname=file.name)
encoded = base64.b64encode(gzip.compress(buf.getvalue())).decode('ascii')
chunks = [encoded[i:i+3000] for i in range(0, len(encoded), 3000)]
print(f'::notice title=LEAVE_SCAFFOLD_COUNT::{len(chunks)}')
for index, chunk in enumerate(chunks):
    print(f'::notice title=LEAVE_SCAFFOLD_{index:03}::{chunk}')
