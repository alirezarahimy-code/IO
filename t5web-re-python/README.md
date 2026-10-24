# t5web-re-python — T5Web 3.5.7 reverse-engineering toolkit (Python)

Python toolkit for static reverse engineering of the **T5Web 3.5.7** binaries
(reference copies in `T5Web [3.5.7]/App` in this repository). Built and
verified 2026-10-23. **No reference assembly is ever executed** — all work is
static: PE/metadata parsing (dnfile) + REA `inspect-managed-members` evidence
(`rea` CLI, static managed analyzer).

## What it does

- `tools/cil.py` — a CIL (MS-IL) decoder for the T5 family:
  - PE/CLI reader via `dnfile` (sections, method bodies, tokens, user strings);
  - the **verified opcode table** for the T5 3.5.7 emitter (see below);
  - per-method **verification** against REA evidence: opcode-name histogram +
    CIL anchors + instruction count must all match;
  - a deterministic **learner** that recovers the table for any new T5-family
    assembly given its REA evidence (used here, and reusable e.g. for TWF 4.28
    which links the same `T5V3.*` assemblies).

## Verification results (2026-10-23)

| Binary | Methods | Result (no learning, table baked in) |
|---|---|---|
| T5V3.Model.dll | 1141 | **pass=1141 fail=0** (REA-verified) |
| T5V3Web.dll | 3186 (32 without bodies) | **pass=3154 fail=0** unverifiable=32 (REA-verified) |
| T5V3.Attendance.dll | 397 | 397/397 walk-clean |
| T5V3.DB.dll | 1731 | 1731/1731 walk-clean |
| T5V3.LeaveCalcs.dll | 31 | 31/31 walk-clean |
| T5V3.Misc.dll | 211 | 211/211 walk-clean |
| T5V3.Report.dll | 1486 | 1486/1486 walk-clean |
| T5V3.ReportGenerator.dll | 699 | 699/699 walk-clean |

**8850 methods across all 8 T5 DLLs disassemble without error.**

Evidence files (REA output, deterministic):
- `evidence/t5v3-model-members.json` (2.3 MB)
- `evidence/t5v3web-members.json` (16.2 MB)

Regenerate at any time with (REA 6.7.0, static only):

```
rea inspect-managed-members "<path to dll>" --format json
```

## The T5 emitter is NOT ECMA-standard

Key deviations found and verified (details in `docs/cil-decoder-status.md`):

- Constant block shifted: `0x15=ldc.i4.m1`, `0x16..0x1E=ldc.i4.0..8`,
  `0x1F=ldc.i4.s`, `0x20=ldc.i4`, `0x23=ldc.r8` (no `.9`/`.100`/`i8`/`r4`
  observed in this corpus).
- `.s` branches (verified): `0x2B br.s, 0x2C brfalse.s, 0x2D brtrue.s,
  0x2E beq.s, 0x30 bgt.s, 0x31 ble.s, 0x32 blt.s, 0x33 bne.un.s,
  0x35 bgt.un.s, 0x37 blt.un.s`.
- Long branches (verified): `0x38 br, 0x39 brfalse, 0x3A brtrue, 0x3B beq,
  0x3F blt`.
- `0x25=dup`, `0x26=pop`, `0x45=switch`.
- `callvirt` = `0x6F` **and** `0x89` (both verified; the emitter duplicates
  opcodes — cf. `and`=0x5F only, `nop`=0x00/0x01).
- `0x5F=and, 0x60=or, 0x61=xor, 0x62=shl, 0x63=shr, 0x65=neg,
  0x50=ldind.ref, 0x52=stind.i1, 0x54=stind.i4` (memory block re-placed).
- `0x71=ldobj, 0x75=isinst, 0x81=stobj, 0x8C=box, 0x8E=ldlen, 0xD0=ldtoken,
  0xDD=leave, 0xDE=leave.s, 0xA5=unbox.any, 0xD1=conv.u2, 0xD2=conv.u1,
  0x6A=conv.i8, 0x6E=conv.u8`.
- **`stelem` (0xA4), `stobj` (0x81) and `ldobj` (0x71) carry 4-byte type
  tokens** (unlike the plain ECMA `stelem`); typed `ldelem.*` /
  `stelem.i1`/`stelem.i2` (0x9C/0x9D) are 1-byte.
- **`throw` (0x7A) is 1 byte with NO token** (exception object comes from the
  preceding `newobj`) — the single most impactful deviation.
- `switch` operand = `u32 count + count*i32 offsets` (no default entry);
  switches with 116 cases exist.
- Long-branch target = `next_ip + s32`; `.s` target = `next_ip + s8`
  (anchor-verified, multiple converging branches in one method).
- Method bodies use a vendor header convention: first byte low-bits `0b10`
  = 1-byte header with size in bits[7:2]; low-bits `0b11` = 12-byte header
  with `code_size` u16 at +4. (Verified against REA `file_offset`/`il_size`
  for all 4295 bodied methods.)
- `0x00` and `0x01` are both nop-like (0x01 presumed duplicate nop; unverified
  — appears only in DLLs without REA evidence).

The full verified set is baked into `tools/cil.py` as `PINS` (105 single-byte
opcodes + 8 `0xFE` prefix pairs), plus `FE_KIND`/`SHAPE` operand-kind rules.
`tables/t5v3-full.json` is the learner's final table dump (byte → name, kind,
locked set).

## Usage

```bash
python3 -m venv .venv && .venv/bin/pip install -r requirements.txt

# verify one DLL against its REA evidence
.venv/bin/python tools/cil.py "<dll>" evidence/t5v3-model-members.json

# extend the table to a new T5-family assembly (needs its evidence file)
rea inspect-managed-members "<new dll>" --format json > evidence/new.json
.venv/bin/python tools/cil.py "<new dll>" evidence/new.json --learn --out tables/new.json

# disassemble / inspect
.venv/bin/python tools/cil.py "<dll>"
```

## Layout

```
tools/cil.py                 decoder + verifier + learner (single file)
evidence/*.json              REA inspect-managed-members output (2 DLLs)
tables/t5v3-full.json        final learned table (dump)
docs/cil-decoder-status.md   findings + full status
scripts/rebuild_all.py       one-shot: verify Model, learn Web, dump table
requirements.txt             dnfile
```

## Notes & limits

- `walk-clean` (no evidence) ≠ verified: it only proves the walk terminates
  and every byte has a table entry. REA-verified = Model + Web only.
- The 32 unverifiable Web methods have no IL body in the binary
  (abstract/interface/forwarded — inherent, not a decoder gap).
- Per-method name injectivity: the emitter duplicates some opcodes
  (`and` = 0x5F/0x6E, `br.s` = 0x2B/0x40, nop = 0x00/0x01, callvirt ×14 bytes);
  the table stores one name per byte — verification is per-method, so the
  duplicates never matter.
- Static analysis only: no decompiled-source claims, no execution of reference
  binaries, no license bypass (project constraint, doc 30).
