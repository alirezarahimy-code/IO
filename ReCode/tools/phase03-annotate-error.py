#!/usr/bin/env python3
"""Expose bounded build/test failures in Actions annotations when logs cannot be fetched."""
import pathlib
import sys

path, title = sys.argv[1:]
text = pathlib.Path(path).read_text(encoding='utf-8', errors='replace')[-10000:]
text = text.replace('%', '%25').replace('\r', '%0D').replace('\n', '%0A')
print(f'::error title={title}::{text}')
