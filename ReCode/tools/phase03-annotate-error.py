#!/usr/bin/env python3
"""Expose a bounded, chunked failure tail when Actions logs are inaccessible."""
import pathlib
import sys

path, title = sys.argv[1:]
text = pathlib.Path(path).read_text(encoding='utf-8', errors='replace')[-12000:]
chunks = [text[i:i + 2500] for i in range(0, len(text), 2500)] or ['No diagnostic output.']
for index, chunk in enumerate(chunks, 1):
    escaped = chunk.replace('%', '%25').replace('\r', '%0D').replace('\n', '%0A')
    print(f'::error title={title} {index}/{len(chunks)}::{escaped}')
