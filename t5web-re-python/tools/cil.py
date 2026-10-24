#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
tools/cil.py — CIL decoder + REA-evidence verifier for the T5 3.5.7 family.

The T5 vendor binaries use a NON-STANDARD CIL opcode layout (verified against
REA `inspect-managed-members` evidence for T5V3.Model.dll 1141/1141 and
T5V3Web.dll 3154/3154, 2026-10-23).  This module ships:

  * a dnfile-based IL reader (PE sections, tiny/fat bodies, tokens, #US);
  * the verified opcode table (PINS below; extendable by the learner);
  * per-method verification: REA opcode-name histograms + CIL anchors +
    instruction counts must all match;
  * a deterministic hill-climbing learner that recovers the table for any
    new assembly of the family given its REA evidence.

Usage:
  cil.py <dll>                          # disassemble (print first methods)
  cil.py <dll> <evidence.json>          # verify against REA evidence
  cil.py <dll> <evidence.json> --learn  # learn/extend the table
  cil.py <dll> <evidence.json> --out t  # dump the resulting table to JSON
  cil.py <dll> <evidence.json> --table t.json   # load a table, verify

Run with the venv python that has dnfile installed (requirements.txt).
"""
from __future__ import annotations

import json
import struct
import sys

# --------------------------------------------------------------------------
# 1. Seed tables
# --------------------------------------------------------------------------

# ECMA-335 II.1 single-byte opcodes.  PLACEHOLDER map: the T5 layout deviates
# in many places (e.g. 0x15..0x26 constant block, 0x2B.. long branches at
# 0x38+, 0x8C box, 0xA2 stelem.ref with token...) — PINS and the learner
# correct every byte that actually appears in the corpus.
def _std_table() -> dict:
    t = {}
    t[0x00] = 'nop'
    for i in range(4):
        t[0x02 + i] = f'ldarg.{i}'
        t[0x06 + i] = f'ldloc.{i}'
        t[0x0A + i] = f'stloc.{i}'
    t[0x0E] = 'ldarg.s'; t[0x0F] = 'starg.s'; t[0x10] = 'ldarga.s'
    t[0x11] = 'ldloc.s'; t[0x12] = 'ldloca.s'; t[0x13] = 'stloc.s'
    t[0x14] = 'ldnull'
    t[0x15] = 'ldc.i4.m1'
    for i in range(10):
        t[0x16 + i] = f'ldc.i4.{i}'
    t[0x20] = 'ldc.i4.100'; t[0x21] = 'ldc.i4.s'; t[0x22] = 'ldc.i4'
    t[0x23] = 'ldc.i8'; t[0x24] = 'ldc.r4'; t[0x25] = 'ldc.r8'
    t[0x26] = 'dup'; t[0x27] = 'pop'; t[0x28] = 'jmp'
    t[0x29] = 'call'; t[0x2A] = 'calli'; t[0x2B] = 'ret'
    t[0x2C] = 'br'; t[0x2D] = 'brfalse'; t[0x2E] = 'brtrue'; t[0x2F] = 'beq'
    t[0x30] = 'bge'; t[0x31] = 'bgt'; t[0x32] = 'ble'; t[0x33] = 'blt'
    t[0x34] = 'bne'; t[0x35] = 'bge.un'; t[0x36] = 'bgt.un'
    t[0x37] = 'ble.un'; t[0x38] = 'blt.un'; t[0x39] = 'bne.un'
    t[0x3A] = 'br.s'; t[0x3B] = 'brfalse.s'; t[0x3C] = 'brtrue.s'
    t[0x3D] = 'beq.s'; t[0x3E] = 'bge.s'; t[0x3F] = 'bgt.s'
    t[0x40] = 'ble.s'; t[0x41] = 'blt.s'; t[0x42] = 'bne.s'
    t[0x43] = 'bge.un.s'; t[0x44] = 'bgt.un.s'; t[0x45] = 'ble.un.s'
    t[0x46] = 'blt.un.s'; t[0x47] = 'bne.un.s'
    t[0x48] = 'add'; t[0x49] = 'sub'; t[0x4A] = 'mul'; t[0x4B] = 'div'
    t[0x4C] = 'div.un'; t[0x4D] = 'rem'; t[0x4E] = 'rem.un'
    t[0x4F] = 'and'; t[0x50] = 'or'; t[0x51] = 'xor'; t[0x52] = 'shl'
    t[0x53] = 'shr'; t[0x54] = 'shr.un'; t[0x55] = 'neg'; t[0x56] = 'not'
    conv = ['i1', 'i2', 'i4', 'i8', 'r4', 'r8', 'u1', 'u2', 'u4', 'u8']
    for i, c in enumerate(conv):
        t[0x57 + i] = f'conv.{c}'
        t[0x61 + i] = f'conv.ovf.{c}'
        t[0x6B + i] = f'conv.ovf.{c}.un'
        t[0x75 + i] = f'conv.{c}.un'
    t[0x6F] = 'callvirt'; t[0x72] = 'ldstr'; t[0x73] = 'newobj'
    t[0x74] = 'castclass'; t[0x75] = 'isinst'; t[0x76] = 'unbox'
    t[0x77] = 'unbox.any'; t[0x7A] = 'throw'
    t[0x7B] = 'ldfld'; t[0x7C] = 'ldflda'; t[0x7D] = 'stfld'
    t[0x7E] = 'ldsfld'; t[0x7F] = 'ldsflda'; t[0x80] = 'stsfld'
    t[0x81] = 'ldtoken'; t[0x82] = 'endfinally'; t[0x83] = 'leave'
    t[0x84] = 'leave.s'; t[0x85] = 'starg'
    for i in range(4):
        t[0x86 + i] = f'starg.{i}'
    t[0x8A] = 'box'; t[0x8D] = 'newarr'; t[0x8E] = 'ldlen'; t[0x8F] = 'ldelema'
    t[0x90] = 'ldelem.i1'; t[0x91] = 'ldelem.u1'; t[0x92] = 'ldelem.i2'
    t[0x93] = 'ldelem.u2'; t[0x94] = 'ldelem.i4'; t[0x95] = 'ldelem.u4'
    t[0x96] = 'ldelem.i8'; t[0x97] = 'ldelem.u8'; t[0x98] = 'ldelem.r4'
    t[0x99] = 'ldelem.r8'; t[0x9A] = 'ldelem.ref'; t[0x9B] = 'ldelem'
    t[0x9C] = 'stelem.i1'; t[0x9D] = 'stelem.u1'; t[0x9E] = 'stelem.i2'
    t[0x9F] = 'stelem.u2'; t[0xA0] = 'stelem.i4'; t[0xA1] = 'stelem.u4'
    t[0xA2] = 'stelem.i8'; t[0xA3] = 'stelem.u8'; t[0xA4] = 'stelem.r4'
    t[0xA5] = 'stelem.r8'; t[0xA6] = 'stelem.ref'; t[0xA7] = 'stelem'
    t[0xA8] = 'unbox'; t[0xA9] = 'unbox.any'; t[0xAA] = 'refanyval'
    t[0xAC] = 'initobj'; t[0xAE] = 'sizeof'; t[0xAF] = 'unbox.any'
    for i in range(4):
        t[0x02 + i] = f'ldarg.{i}'
    t[0x45] = 'switch'
    t[0x40] = 'br.s'  # T5: .s set starts earlier (see PINS)
    return t


def _std_fe() -> dict:
    """(0xFE, x) -> ECMA name placeholders."""
    names = {
        0x01: 'ceq', 0x02: 'cgt', 0x03: 'cgt.un', 0x04: 'clt',
        0x05: 'clt.un', 0x06: 'ldftn', 0x07: 'ldflda', 0x08: 'dup',
        0x09: 'pop', 0x0A: 'jmp', 0x0B: 'ldarg', 0x0C: 'ldarga',
        0x0D: 'starg', 0x0E: 'starg.s', 0x0F: 'ldloc', 0x10: 'ldloca',
        0x11: 'stloc', 0x12: 'stloc.s', 0x15: 'initobj', 0x16: 'constrained.',
        0x18: 'unbox', 0x19: 'unbox.any', 0x20: 'refanyval', 0x26: 'localloc',
        0x2A: 'rethrow', 0x2C: 'sizeof', 0x2F: 'ckfinite',
    }
    return {(0xFE, x): n for x, n in names.items()}


# Operand-kind classification by opcode name.  Kinds:
#   N no operand, I8 sbyte, I32 int32, I32L long, F32, F64,
#   T 32-bit type/member token, TS user-string token,
#   SW switch (u32 count + count*i32 offsets), 2T two tokens
SHAPE: dict = {
    'ldarg.0': 'N', 'ldarg.1': 'N', 'ldarg.2': 'N', 'ldarg.3': 'N',
    'ldloc.0': 'N', 'ldloc.1': 'N', 'ldloc.2': 'N', 'ldloc.3': 'N',
    'stloc.0': 'N', 'stloc.1': 'N', 'stloc.2': 'N', 'stloc.3': 'N',
    'starg.0': 'N', 'starg.1': 'N', 'starg.2': 'N', 'starg.3': 'N',
    'ldarg.s': 'I8', 'starg.s': 'I8', 'ldloc.s': 'I8', 'stloc.s': 'I8',
    'ldloca.s': 'I8', 'ldarga.s': 'I8',
    'ldarg': 'I8', 'ldloc': 'I8', 'stloc': 'I8', 'starg': 'I8',
    'ldarga': 'I8', 'ldloca': 'I8',
    'ldnull': 'N',
    'ldc.i4.m1': 'N', 'ldc.i4.0': 'N', 'ldc.i4.1': 'N', 'ldc.i4.2': 'N',
    'ldc.i4.3': 'N', 'ldc.i4.4': 'N', 'ldc.i4.5': 'N', 'ldc.i4.6': 'N',
    'ldc.i4.7': 'N', 'ldc.i4.8': 'N', 'ldc.i4.9': 'N', 'ldc.i4.100': 'N',
    'ldc.i4.s': 'I8', 'ldc.i4': 'I32', 'ldc.i8': 'I32L',
    'ldc.r4': 'F32', 'ldc.r8': 'F64',
    'dup': 'N', 'pop': 'N', 'jmp': 'N', 'ret': 'N', 'nop': 'N',
    'leave': 'I32', 'leave.s': 'I8', 'rethrow': 'N',
    'throw': 'N',               # T5 deviation: throw is 1 byte, no token
    'endfinally': 'N', 'endfilter': 'N',
    'br': 'I32', 'brfalse': 'I32', 'brtrue': 'I32', 'beq': 'I32',
    'bge': 'I32', 'bgt': 'I32', 'ble': 'I32', 'blt': 'I32',
    'bne': 'I32', 'bge.un': 'I32', 'bgt.un': 'I32', 'ble.un': 'I32',
    'blt.un': 'I32', 'bne.un': 'I32',
    'br.s': 'I8', 'brfalse.s': 'I8', 'brtrue.s': 'I8', 'beq.s': 'I8',
    'bge.s': 'I8', 'bgt.s': 'I8', 'ble.s': 'I8', 'blt.s': 'I8',
    'bne.s': 'I8', 'bge.un.s': 'I8', 'bgt.un.s': 'I8', 'ble.un.s': 'I8',
    'blt.un.s': 'I8', 'bne.un.s': 'I8',
    'add': 'N', 'sub': 'N', 'mul': 'N', 'div': 'N', 'div.un': 'N',
    'rem': 'N', 'rem.un': 'N', 'and': 'N', 'or': 'N', 'xor': 'N',
    'shl': 'N', 'shr': 'N', 'shr.un': 'N', 'neg': 'N', 'not': 'N',
    'ckfinite': 'N',
    'ldind.i1': 'N', 'ldind.u1': 'N', 'ldind.i2': 'N', 'ldind.u2': 'N',
    'ldind.i4': 'N', 'ldind.u4': 'N', 'ldind.i8': 'N', 'ldind.u8': 'N',
    'ldind.r4': 'N', 'ldind.r8': 'N', 'ldind.ref': 'N',
    'stind.i1': 'N', 'stind.u1': 'N', 'stind.i2': 'N', 'stind.u2': 'N',
    'stind.i4': 'N', 'stind.u4': 'N', 'stind.i8': 'N', 'stind.u8': 'N',
    'stind.r4': 'N', 'stind.r8': 'N', 'stind.ref': 'N',
    'ldfld': 'T', 'ldflda': 'T', 'stfld': 'T',
    'ldsfld': 'T', 'ldsflda': 'T', 'stsfld': 'T',
    'ldtoken': 'T', 'initobj': 'T', 'sizeof': 'T',
    'constrained.': 'T',
    'call': 'T', 'calli': '2T', 'callvirt': 'T', 'newobj': 'T',
    'newarr': 'T', 'ldftn': 'T',
    'box': 'T', 'unbox': 'T', 'unbox.any': 'T', 'castclass': 'T',
    'isinst': 'T', 'ldobj': 'T', 'stobj': 'T',
    'ldlen': 'N', 'ldelema': 'T',
    'ldelem': 'T',               # T5 emitter: polymorphic ldelem carries a token
    'ldelem.i1': 'N', 'ldelem.u1': 'N', 'ldelem.i2': 'N',
    'ldelem.u2': 'N', 'ldelem.i4': 'N', 'ldelem.u4': 'N',
    'ldelem.i8': 'N', 'ldelem.u8': 'N', 'ldelem.r4': 'N',
    'ldelem.r8': 'N', 'ldelem.ref': 'N',
    'stelem': 'T',               # T5 deviation: stelem carries a type token
    'stelem.i1': 'N', 'stelem.u1': 'N', 'stelem.i2': 'N',
    'stelem.u2': 'N', 'stelem.i4': 'N', 'stelem.u4': 'N',
    'stelem.i8': 'N', 'stelem.u8': 'N', 'stelem.r4': 'N',
    'stelem.r8': 'N', 'stelem.ref': 'N',
    'ldstr': 'TS', 'switch': 'SW', 'refanyval': 'T', 'localloc': 'N',
    'tailcall': 'N', 'cpblk': 'N', 'ldblk': 'N', 'initblk': 'N',
}


def kind_for(name: str) -> str:
    if name in SHAPE:
        return SHAPE[name]
    if name.startswith('conv.'):
        return 'N'
    if name.startswith(('ldarg.', 'ldloc.', 'stloc.', 'starg.', 'ldc.i4.')):
        return 'N'
    return 'N'


# --------------------------------------------------------------------------
# Verified pins: every byte that appears at an instruction-start position in
# T5V3.Model.dll (1141 methods) or T5V3Web.dll (3154 methods), with the name
# that makes ALL methods of both assemblies pass REA-evidence verification
# (anchored + full histogram + instruction count, 2026-10-23).
# The learner may override a pin ONLY when a directly aligned CIL anchor
# demands another name (safety valve; never triggered for these bytes).
# --------------------------------------------------------------------------
PINS: dict = {
    0x00: 'nop',
    0x02: 'ldarg.0',
    0x03: 'ldarg.1',
    0x04: 'ldarg.2',
    0x05: 'ldarg.3',
    0x06: 'ldloc.0',
    0x07: 'ldloc.1',
    0x08: 'ldloc.2',
    0x09: 'ldloc.3',
    0x0A: 'stloc.0',
    0x0B: 'stloc.1',
    0x0C: 'stloc.2',
    0x0D: 'stloc.3',
    0x0E: 'ldarg.s',
    0x0F: 'ldarga.s',
    0x10: 'starg.s',
    0x11: 'ldloc.s',
    0x12: 'ldloca.s',
    0x13: 'stloc.s',
    0x14: 'ldnull',
    0x15: 'ldc.i4.m1',
    0x16: 'ldc.i4.0',
    0x17: 'ldc.i4.1',
    0x18: 'ldc.i4.2',
    0x19: 'ldc.i4.3',
    0x1A: 'ldc.i4.4',
    0x1B: 'ldc.i4.5',
    0x1C: 'ldc.i4.6',
    0x1D: 'ldc.i4.7',
    0x1E: 'ldc.i4.8',
    0x1F: 'ldc.i4.s',
    0x20: 'ldc.i4',
    0x23: 'ldc.r8',
    0x25: 'dup',
    0x26: 'pop',
    0x28: 'call',
    0x2A: 'ret',
    0x2B: 'br.s',
    0x2C: 'brfalse.s',
    0x2D: 'brtrue.s',
    0x2E: 'beq.s',
    0x30: 'bgt.s',
    0x31: 'ble.s',
    0x32: 'blt.s',
    0x33: 'bne.un.s',
    0x35: 'bgt.un.s',
    0x37: 'blt.un.s',
    0x38: 'br',
    0x39: 'brfalse',
    0x3A: 'brtrue',
    0x3B: 'beq',
    0x3F: 'blt',
    0x45: 'switch',
    0x50: 'ldind.ref',
    0x52: 'stind.i1',
    0x54: 'stind.i4',
    0x58: 'add',
    0x59: 'sub',
    0x5A: 'mul',
    0x5B: 'div',
    0x5D: 'rem',
    0x5F: 'and',
    0x60: 'or',
    0x61: 'xor',
    0x62: 'shl',
    0x63: 'shr',
    0x65: 'neg',
    0x68: 'conv.i2',
    0x69: 'conv.i4',
    0x6A: 'conv.i8',
    0x6C: 'conv.r8',
    0x6D: 'conv.u4',
    0x6E: 'conv.u8',
    0x6F: 'callvirt',
    0x71: 'ldobj',
    0x72: 'ldstr',
    0x73: 'newobj',
    0x74: 'castclass',
    0x75: 'isinst',
    0x7A: 'throw',
    0x7B: 'ldfld',
    0x7C: 'ldflda',
    0x7D: 'stfld',
    0x7E: 'ldsfld',
    0x80: 'stsfld',
    0x81: 'stobj',
    0x8C: 'box',
    0x8D: 'newarr',
    0x8E: 'ldlen',
    0x8F: 'ldelema',
    0x91: 'ldelem.u1',
    0x94: 'ldelem.i4',
    0x96: 'ldelem.i8',
    0x9A: 'ldelem.ref',
    0x9C: 'stelem.i1',
    0x9D: 'stelem.i2',
    0xA2: 'stelem.ref',
    0xA4: 'stelem',
    0xA5: 'unbox.any',
    0xD0: 'ldtoken',
    0xD1: 'conv.u2',
    0xD2: 'conv.u1',
    0xDC: 'endfinally',
    0xDD: 'leave',
    0xDE: 'leave.s',
}

# fe-prefix pins (verified)
FE_PINS: dict = {
    (0xFE, 0x01): 'ceq',
    (0xFE, 0x02): 'cgt',
    (0xFE, 0x03): 'cgt.un',
    (0xFE, 0x04): 'clt',
    (0xFE, 0x05): 'clt.un',
    (0xFE, 0x06): 'ldftn',
    (0xFE, 0x15): 'initobj',
    (0xFE, 0x16): 'constrained.',
}

FE_KIND: dict = {
    'ceq': 'N', 'cgt': 'N', 'cgt.un': 'N', 'clt': 'N', 'clt.un': 'N',
    'ldftn': 'T', 'initobj': 'T', 'constrained.': 'T', 'unbox': 'T',
    'unbox.any': 'T', 'refanyval': 'T', 'sizeof': 'T', 'rethrow': 'N',
    'dup': 'N', 'pop': 'N', 'jmp': 'N', 'ldarg': 'I8', 'starg': 'I8',
    'ldarga': 'I8', 'ldloc': 'I8', 'stloc': 'I8', 'ldloca': 'I8',
    'localloc': 'N', 'ckfinite': 'N', 'ldflda': 'T', 'ldsflda': 'T',
    'starg.s': 'I8', 'stloc.s': 'I8',
}

# --------------------------------------------------------------------------
# 2. PE / metadata access
# --------------------------------------------------------------------------

class CIL:
    def __init__(self, dll_path: str, evidence_path: str | None = None,
                 op_table: dict | None = None):
        import dnfile  # local import so the module loads without the dep
        self.path = dll_path
        self.pe = dnfile.dnPE(dll_path)
        self._raw = open(dll_path, 'rb').read()

        # sections (COFF header: Nsec@+2, SizeOfOptionalHeader@+16)
        e_lfanew = struct.unpack_from('<I', self._raw, 0x3C)[0]
        coff = e_lfanew + 4
        nsec = struct.unpack_from('<H', self._raw, coff + 2)[0]
        soh = struct.unpack_from('<H', self._raw, coff + 16)[0]
        sec_off = coff + 20 + soh
        self.sections = []
        for i in range(nsec):
            s = sec_off + i * 40
            vsize, vaddr, rsize, rptr = struct.unpack_from('<IIII', self._raw, s + 8)
            self.sections.append((vaddr, rptr, max(rsize, vsize)))

        # opcode tables
        std = _std_table()
        self.op: dict = {b: (n, kind_for(n)) for b, n in std.items()}
        # 0x01: presumed duplicate nop (this format duplicates opcodes, cf.
        # and=0x5F/0x6E, br.s=0x2B/0x40).  UNVERIFIED: appears only in DLLs
        # without REA evidence (T5V3.Attendance/DB), always as a standalone
        # 1-byte instruction.  Unlocked — the learner may re-label it.
        self.op.setdefault(0x01, ('nop', 'N'))
        for b, n in PINS.items():
            self.op[b] = (n, kind_for(n))
        self.prefix: dict = {(0xFE, x): (n, FE_KIND.get(n, kind_for(n)))
                             for (fx, x), n in _std_fe().items()}
        for k, n in FE_PINS.items():
            self.prefix[k] = (n, FE_KIND.get(n, kind_for(n)))
        self.lock = {b for b in PINS}
        self.frozen: set = set()
        self.bad: set = set()
        if op_table:
            self.load_table(op_table)

        # methods
        self.methods_list = []
        md = self.pe.net.mdtables.MethodDef
        for i, row in enumerate(md):
            rva = row.Rva or 0
            tok = 0x06000000 + i + 1
            il = self._body_at(rva) if rva else None
            self.methods_list.append({
                'token': tok, 'name': row.Name, 'rva': rva, 'il': il,
            })
        self.by_token = {m['token']: m for m in self.methods_list}

        self.ev = None
        self.ev_by_token: dict = {}
        if evidence_path:
            self.load_evidence(evidence_path)

    # -- PE helpers ---------------------------------------------------------
    def rva_to_off(self, rva: int) -> int:
        for va, rp, sz in self.sections:
            if va <= rva < va + sz:
                return rp + (rva - va)
        return -1

    def _body_at(self, rva: int) -> bytes | None:
        off = self.rva_to_off(rva)
        if off < 0 or off >= len(self._raw):
            return None
        b = self._raw[off]
        # T5 vendor header convention (verified against REA evidence for all
        # 4295 methods across T5V3.Model.dll + T5V3Web.dll):
        #   low-bits 0b10 -> 1-byte header, code size in bits[7:2]
        #   low-bits 0b11 -> 12-byte header, code size = u16 at +4
        if b & 0x3 == 0x2:
            size = (b >> 2) & 0x3F
            return bytes(self._raw[off + 1:off + 1 + size])
        if b & 0x3 == 0x3:
            code_size = struct.unpack_from('<H', self._raw, off + 4)[0]
            return bytes(self._raw[off + 12:off + 12 + code_size])
        return None

    def load_evidence(self, path: str):
        with open(path) as f:
            d = json.load(f)
        nr = d.get('normalized_result', d)
        for m in nr.get('methods', []):
            tok = int(m['token'], 16)
            self.ev_by_token[tok] = m

    def load_table(self, t: dict):
        for k, v in (t.get('op') or {}).items():
            b = int(k, 16)
            if b == 0xFE:
                continue
            name = v[0]
            kind = v[1] if len(v) > 1 and v[1] else kind_for(name)
            self.op[b] = (name, kind)
        for k, v in (t.get('prefix') or {}).items():
            self.prefix[(int(k[2:4], 16), int(k[4:6], 16))] = \
                (v[0], kind_for(v[0]))
        for b in (t.get('locked') or []):
            self.lock.add(int(b, 16))

    def dump_table(self) -> dict:
        out = {'op': {}, 'prefix': {}, 'locked': []}
        for b in sorted(self.op):
            n, k = self.op[b]
            out['op'][f'0x{b:02x}'] = [n, k]
        for (fx, x), (n, k) in sorted(self.prefix.items()):
            out['prefix'][f'0x{fx:02x}{x:02x}'] = [n, k]
        out['locked'] = [f'0x{b:02x}' for b in sorted(self.lock)]
        return out

    # -- tokens / strings ----------------------------------------------------
    def resolve_token(self, tok: int) -> str:
        try:
            ty = (tok >> 24) & 0xFF
            idx = tok & 0x00FFFFFF
            md = self.pe.net.mdtables
            if ty == 0x01:
                return f'TypeRef:{md.TypeRef[idx].Name}'
            if ty == 0x02:
                r = md.TypeDef[idx]
                ns = r.Namespace or ''
                return f'TypeDef:{ns + "." if ns else ""}{r.Name}'
            if ty == 0x04:
                return f'Field:{md.Field[idx].Name}'
            if ty == 0x06:
                return f'MethodDef:{md.MethodDef[idx].Name}'
            if ty == 0x0A:
                return f'MemberRef:{md.MemberRef[idx].Name}'
            if ty == 0x1B:
                return f'GenericParam:{md.GenericParam[idx].Name}'
            return f'token:0x{tok:08x}'
        except Exception:
            return f'token:0x{tok:08x}'

    def string_at(self, idx: int) -> str:
        try:
            return self.pe.net.user_strings.get_string(idx)
        except Exception:
            return f'<us:{idx}>'

    # -- decode --------------------------------------------------------------
    def _entry_at(self, il: bytes, pc: int, for_walk: bool = False):
        n = len(il)
        op = il[pc]
        if op == 0xFE:
            if pc + 1 >= n:
                return None
            ent = self.prefix.get((0xFE, il[pc + 1]))
            if ent is None:
                if for_walk:
                    return None
                raise ValueError(f'unknown opcode 0xFE{il[pc+1]:02x} @ {pc}')
            return ent[0], ent[1], 2
        ent = self.op.get(op)
        if ent is None:
            if for_walk:
                return None
            raise ValueError(f'unknown opcode 0x{op:02x} @ {pc}')
        return ent[0], ent[1], 1

    def _read_op(self, il: bytes, pc: int, kind: str, nbytes: int = 1):
        n = len(il)
        npc = pc + nbytes
        if kind == 'N':
            return None, npc
        if kind == 'I8':
            if npc >= n:
                return None
            return struct.unpack_from('<b', il, npc)[0], npc + 1
        if kind in ('I32', 'I32L'):
            if npc + 4 > n:
                return None
            return struct.unpack_from('<i', il, npc)[0], npc + 4
        if kind == 'F32':
            if npc + 4 > n:
                return None
            return struct.unpack_from('<f', il, npc)[0], npc + 4
        if kind == 'F64':
            if npc + 8 > n:
                return None
            return struct.unpack_from('<d', il, npc)[0], npc + 8
        if kind in ('T', 'TS'):
            if npc + 4 > n:
                return None
            return struct.unpack_from('<I', il, npc)[0], npc + 4
        if kind == '2T':
            if npc + 8 > n:
                return None
            return struct.unpack_from('<II', il, npc), npc + 8
        if kind == 'SW':
            if npc + 4 > n:
                return None
            count = struct.unpack_from('<I', il, npc)[0]
            if npc + 4 + 4 * count > n:
                return None
            return [struct.unpack_from('<i', il, npc + 4 + 4 * j)[0]
                    for j in range(count)], npc + 4 + 4 * count
        return None, npc

    def walk(self, il: bytes):
        """Greedy walk. Returns (ok, [(pc, name, kind, nbytes, width)...])."""
        out = []
        pc = 0
        n = len(il)
        while pc < n:
            ent = self._entry_at(il, pc, for_walk=True)
            if ent is None:
                return False, out
            name, kind, nbytes = ent
            r = self._read_op(il, pc, kind, nbytes)
            if r is None:
                return False, out
            _, npc = r
            out.append((pc, name, kind, nbytes, npc - pc))
            pc = npc
        return True, out

    def decode_il(self, il: bytes):
        out = []
        pc = 0
        n = len(il)
        while pc < n:
            op = il[pc]
            entry = self._entry_at(il, pc)
            if entry is None:
                raise ValueError(f'unknown opcode 0x{op:02x} @ {pc}')
            name, kind, nbytes = entry
            detail = None
            operand, npc = self._read_op(il, pc, kind, nbytes)
            if npc is None or npc > n:
                raise ValueError(f'operand overruns: 0x{op:02x}({name}) @ {pc}')
            if kind == 'I8' and name.startswith(('br', 'beq', 'bge', 'bgt',
                                                 'ble', 'blt', 'bne', 'leave')):
                detail = f'-> {pc + 2 + operand}'
            elif kind == 'I32' and name.startswith(('br', 'beq', 'bge', 'bgt',
                                                    'ble', 'blt', 'bne', 'leave')):
                detail = f'-> {pc + 5 + operand}'
            elif kind == 'T':
                detail = self.resolve_token(operand)
            elif kind == 'TS':
                detail = 'str ' + repr(self.string_at(operand))
            elif kind == 'SW':
                detail = f'switch[{operand}]'
            out.append((pc, name, operand, detail))
            pc = npc
        return out

    # -- verification ----------------------------------------------------------
    def _ev_method(self, m):
        ev = self.ev_by_token.get(m['token'])
        if not ev:
            return None
        b = ev.get('body') or {}
        if b.get('status') != 'present' or not b.get('il_size'):
            return None
        return ev

    def check_method(self, m) -> list | None:
        """Problem list (empty = pass); None = unverifiable."""
        probs = []
        ev = self._ev_method(m)
        if ev is None:
            return None
        b = ev['body']
        il = m['il']
        if il is None:
            return ['no-body-in-dll']
        if len(il) != b.get('il_size'):
            probs.append(f'il-size {len(il)}!={b.get("il_size")}')
        ok, instr = self.walk(il)
        if not ok:
            last = instr[-1][0] if instr else 0
            probs.append(f'walk-stuck@{last}')
            return probs
        if b.get('instruction_count') is not None:
            if len(instr) != b['instruction_count']:
                probs.append(f'count {len(instr)}!={b["instruction_count"]}')
        got = {}
        for _, name, *_ in instr:
            got[name] = got.get(name, 0) + 1
        want = dict(b.get('opcode_counts') or {})
        for name, c in want.items():
            if got.get(name, 0) != c:
                probs.append(f'hist {name}:{got.get(name, 0)}!={c}')
        for name, c in got.items():
            if want.get(name, 0) != c:
                probs.append(f'hist+ {name}:{c}')
        my = {pc: name for pc, name, *_ in instr}
        for a in b.get('anchors', []):
            off = a.get('il_offset')
            if off in my and my[off] != a['opcode']:
                probs.append(f'anchor@{off} {my[off]}!={a["opcode"]}')
        return probs

    def verify(self) -> dict:
        res = {'pass': 0, 'fail': 0, 'unverifiable': 0, 'failures': []}
        if not self.ev_by_token:
            for m in self.methods_list:
                if m['il'] is None:
                    res['unverifiable'] += 1
                    continue
                try:
                    self.decode_il(m['il'])
                    res['pass'] += 1
                except Exception as e:
                    res['fail'] += 1
                    res['failures'].append((m['token'], str(e)[:80]))
            return res
        for m in self.methods_list:
            probs = self.check_method(m)
            if probs is None:
                res['unverifiable'] += 1
            elif not probs:
                res['pass'] += 1
            else:
                res['fail'] += 1
                res['failures'].append((m['token'], '; '.join(probs[:6])))
        return res

    # -- learning ----------------------------------------------------------------
    def set_name(self, byte: int, name: str, kind: str | None = None,
                 lock: bool = True):
        self.op[byte] = (name, kind or kind_for(name))
        if lock:
            self.lock.add(byte)

    def _all_names(self) -> set:
        names = {n for n, k in self.op.values()}
        names |= {n for n, k in self.prefix.values()}
        for ev in self.ev_by_token.values():
            b = ev.get('body') or {}
            names |= set((b.get('opcode_counts') or {}).keys())
        return names

    def learn(self, max_rounds: int = 80, verbose: bool = True):
        sys.setrecursionlimit(30000)
        self.bad = set()
        self.frozen = set()
        ev_methods = [m for m in self.methods_list if self._ev_method(m)]
        name_pool = self._all_names()
        prev_fail = None
        for rnd in range(max_rounds):
            fails = [m for m in ev_methods if self.check_method(m)]
            if not fails:
                if verbose:
                    print(f'[learn] CONVERGED round {rnd}: '
                          f'all {len(ev_methods)} pass')
                break
            if verbose:
                print(f'[learn] round {rnd}: pass={len(ev_methods)-len(fails)} '
                      f'fail={len(fails)} (locked={len(self.lock)}, '
                      f'frozen={len(self.frozen)}, bad={len(self.bad)})')
            if prev_fail is not None and len(fails) >= prev_fail:
                if verbose:
                    print('[learn] no progress — stopping')
                break
            prev_fail = len(fails)
            for m in sorted(fails, key=lambda x: len(x['il'] or b'')):
                if not self.check_method(m):
                    continue
                if self._fix_method(m, name_pool):
                    ok, instr = self.walk(m['il'])
                    if ok:
                        ev = self._ev_method(m)
                        anch = {a['il_offset']: a['opcode']
                                for a in ev['body'].get('anchors', [])}
                        for pc, name, *_ in instr:
                            if pc in anch and anch[pc] == name:
                                self.frozen.add(m['il'][pc])
        return self.verify()

    def _fix_method(self, m, name_pool) -> bool:
        il = m['il']
        ev = self._ev_method(m)
        b = ev['body']
        want = dict(b.get('opcode_counts') or {})
        anch = {a['il_offset']: a['opcode'] for a in b.get('anchors', [])}
        ok, instr = self.walk(il)

        def hist_of(instr_):
            h = {}
            for _, name, *_ in instr_:
                h[name] = h.get(name, 0) + 1
            return h

        def try_rename(byte, name):
            if name not in name_pool or (byte, name) in self.bad:
                return False
            cur = self.op.get(byte, ('', 'N'))
            if cur[0] == name:
                return False
            if byte in self.frozen:
                pos = [pc for pc, nm, *_ in instr if m['il'][pc] == byte]
                if not any(pc in anch and anch[pc] == name for pc in pos):
                    return False
            old = self.op.get(byte)
            self.op[byte] = (name, kind_for(name))
            probs = self.check_method(m)
            if not probs:
                return True
            if old:
                self.op[byte] = old
            self.bad.add((byte, name))
            return False

        if not ok:
            stuck = instr[-1][0] if instr else 0
            if stuck >= len(il):
                return False
            byte = il[stuck]
            h = hist_of(instr)
            short = {n for n, c in want.items() if h.get(n, 0) < c}
            anch_near = {a for off, a in anch.items() if off >= stuck}
            cands = sorted(short & anch_near) + sorted(short - anch_near)
            for name in cands:
                if try_rename(byte, name):
                    return True
            return False

        probs = self.check_method(m)
        if not probs:
            return True
        my = {pc: name for pc, name, *_ in instr}
        # 1) aligned anchor mismatches
        for off, aname in anch.items():
            if off in my and my[off] != aname:
                if try_rename(il[off], aname):
                    return True
        # 2) histogram over/under with same-kind swaps
        h = hist_of(instr)
        over = {n for n in h if h[n] > want.get(n, 0)}
        under = {n for n, c in want.items() if h.get(n, 0) < c}
        for pc, n in sorted(my.items()):
            if n in over:
                for cand in sorted(under):
                    if kind_for(cand) == self.op.get(il[pc], ('N', 'N'))[1]:
                        if try_rename(il[pc], cand):
                            return True
        # 3) count mismatch: width-changing renames near the tail
        if instr and len(instr) != b.get('instruction_count'):
            cands = sorted(under) if under else sorted(set(anch.values()))
            for i in range(len(instr) - 1, max(0, len(instr) - 16), -1):
                pc = instr[i][0]
                for cand in cands:
                    if kind_for(cand) == self.op.get(il[pc], ('N', 'N'))[1]:
                        continue
                    if try_rename(il[pc], cand):
                        return True
        return False


# --------------------------------------------------------------------------
# 3. CLI
# --------------------------------------------------------------------------

def main(argv):
    if len(argv) < 2:
        print(__doc__)
        return 1
    dll = argv[1]
    ev = None
    learn = False
    out = None
    table = None
    i = 2
    while i < len(argv):
        a = argv[i]
        if a == '--learn':
            learn = True
        elif a == '--out':
            out = argv[i + 1]
            i += 1
        elif a == '--table':
            table = argv[i + 1]
            i += 1
        elif not a.startswith('--'):
            ev = a
        i += 1
    c = CIL(dll, ev, op_table=None)
    if table:
        with open(table) as f:
            c.load_table(json.load(f))
    if ev:
        r = c.verify()
        print(f'VERIFY: pass={r["pass"]} fail={r["fail"]} '
              f'unverifiable={r["unverifiable"]}')
        for tok, why in r['failures'][:12]:
            print(f'  {hex(tok)}: {why}')
    if learn:
        r = c.learn(max_rounds=80, verbose=True)
        print(f'AFTER LEARN: pass={r["pass"]} fail={r["fail"]} '
              f'unverifiable={r["unverifiable"]}')
        for tok, why in r['failures'][:12]:
            print(f'  {hex(tok)}: {why}')
    if out:
        with open(out, 'w') as f:
            json.dump(c.dump_table(), f, indent=1)
        print(f'TABLE -> {out}')
    shown = 0
    for m in c.methods_list:
        if m['il'] is None:
            continue
        if shown >= 5:
            break
        try:
            instr = c.decode_il(m['il'])
        except Exception as e:
            print(f'{hex(m["token"])} {m["name"]}: {e}')
            continue
        print(f'-- {hex(m["token"])} {m["name"]} '
              f'({len(instr)} instr, {len(m["il"])} B)')
        for pc, name, op, det in instr[:14]:
            print(f'   {pc:5d}  {name:<12} '
                  f'{(str(op)[:24] if op is not None else ""):<24} {det or ""}')
        shown += 1
    return 0


if __name__ == '__main__':
    sys.exit(main(sys.argv))
