# CIL decoder — status & methodology (T5 3.5.7 family)

Final state 2026-10-23. Companion to `README.md`.

## Verification methodology

A method **passes** iff, with the baked table:

1. the IL walk terminates exactly at `il_size` (no unknown byte, no operand
   overrun);
2. `instruction_count` matches the REA body record;
3. the opcode-name **histogram** equals the REA `opcode_counts` exactly
   (both directions);
4. every REA **CIL anchor** (`il_offset → opcode`) matches the name decoded
   at that offset.

Evidence = `rea inspect-managed-members <dll> --format json` (static managed
analysis; never loads/executes the target). The evidence carries per method:
`il_size`, `instruction_count`, `opcode_counts` (name → count), `anchors`
(offset, opcode, operand_kind, operand), `file_offset`, `header_format`.

Results: **T5V3.Model.dll 1141/1141**, **T5V3Web.dll 3154/3154** (32
unverifiable = no body in binary). Both reproduced **without learning**,
table baked in.

## How the table was recovered

1. Seed = ECMA placeholders + 58-byte Model-anchored core + 7 Web-anchored
   pins (dup/pop/bne.un.s/ldobj/isinst/box/ldc.i4.m1).
2. Deterministic hill-climbing learner (`CIL.learn`): for each failing method,
   try re-naming the implicated byte to a candidate name (anchor-observed
   names and histogram-deficit names), keep the first assignment that makes
   the method pass; freeze anchor-confirmed bytes; blacklist failed
   (byte, name) pairs. Converges Model in 0 rounds, Web in 2.
3. Bake: the union of byte → name over all instruction starts of all passing
   methods of both DLLs (105 bytes + 8 fe pairs) becomes `PINS` — locked.

## Deviations confirmed the hard way (history)

- **stobj/stelem/ldobj token leak**: missing operand kinds made 4-byte type
  tokens decode as 4 phantom instructions (the last Web failures; e.g.
  `stobj` token `37 00 00 1b` = TypeSpec read as blt.un.s/nop/nop/ldc.i4.5).
- **WordsToInt (0x60000A7)**: three ambiguous unary/binary bytes
  (`0x5F/0x6A/0x6E` ↔ and/conv.i8/conv.u8) — all 6 per-method permutations
  match the histogram; disambiguated by cross-method usage (0x5F is `and` in
  10 other methods; 0x6A/0x6E occur only here) → `0x5F=and, 0x6A=conv.i8,
  0x6E=conv.u8`.
- **Edit (0x60000A42)**: two phantom "brfalse" at 4-byte-wide positions were
  `stelem` + ArrayType tokens (`39 00 00 01` = ArrayType #57) — proved
  `stelem` is a token instruction in this emitter; fixing its kind resolved
  the whole method (988-instruction count matched exactly).
- **Branch target formula**: long I32 branches target `next_ip + s32`,
  `.s` forms `next_ip + s8` — confirmed by three long branches in one method
  converging on the same target.
- **switch**: `u32 count + count×i32` offsets, **no default entry**; a
  116-case switch (T5V3.ReportGenerator) exposed an over-strict `count>64`
  guard in an earlier revision.
- **Method body headers**: vendor convention — first byte low-bits `0b10`:
  1-byte header, size in bits[7:2]; low-bits `0b11`: 12-byte header,
  `code_size` u16 at +4, `local_var_sig` u32 at +8. Verified against
  REA `file_offset`+`il_size` for **all 4295** bodied methods.
- **throw**: 1 byte, no token (four independent methods).
- **0x01**: presumed duplicate nop (unverified; only in DLLs without
  evidence).

## Environment notes

- Run with the venv python: dnfile 0.18 (plain `python3` may lack it).
- Long methods (max 5612 instructions in T5V3Web) require raised recursion
  limits if the DFS-style walkers are used; `learn()` sets 30000.
- dnfile note: this PE/CLI metadata's `MethodDef` table lacks the `Class`
  column (custom toolchain), so declaring-type resolution relies on the REA
  evidence's `declaring_type` field; the decoder does not need it.
- REA CLI: `npm install rea-agents`, then `node_modules/.bin/rea
  inspect-managed-members <dll> --format json`.
