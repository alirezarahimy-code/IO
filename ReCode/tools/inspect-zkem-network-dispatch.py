#!/usr/bin/env python3
"""Emulate hash-pinned SDK x86 setters up to a trapped transport (no COM/device).

No DLL loader, DllMain, OS API, socket, firmware or commpro code is executed.
Only reviewed setter instructions run in Unicorn; CRT and transport are stubs.
The synthetic ACK tests SDK return handling, NOT device support/persistence.
Dependencies: pefile==2024.8.26 unicorn==2.1.4
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct

import pefile
from unicorn import Uc, UC_ARCH_X86, UC_MODE_32, UC_HOOK_CODE
from unicorn.x86_const import UC_X86_REG_EAX, UC_X86_REG_EIP, UC_X86_REG_ESP

ROOT = Path(__file__).resolve().parents[2]
FIXTURES = [
    dict(path='SDK 6.3.1.45/zkemsdk.dll',
         sha256='890596687008389d88eabb15ae0d02b9b24cc19b6caf861f84dfd13c515cec91',
         setter=0x1002f4a0, info=0x10023bc0,
         sprintf=0x10044c80, strlen=0x10047520, memset=0x100462b0,
         checks=[0x10045c70, 0x10045ca0, 0x100449d0],
         operate=0x1009a1b8, transport=0x1009a1d0,
         timeout=0x88),
    dict(path='NewPack Dlls 10.06.1398/zkemsdk.dll',
         sha256='52879682f7c7e5cb78bcae5ad9c9f8b4fe1c51f4df3688b10836d62a80f29384',
         setter=0x1001d440, info=None,
         sprintf=0x100288e7, strlen=None, memset=0x100293d0,
         checks=[0x1002876e], operate=0x1004a198, transport=0x1004a1a0,
         timeout=0x7c),
]


def run(fixture, name, value, *, ack=2000, null=False, info=None):
    raw = (ROOT / fixture['path']).read_bytes()
    if hashlib.sha256(raw).hexdigest() != fixture['sha256']:
        raise ValueError('Unreviewed binary: ' + fixture['path'])
    pe = pefile.PE(data=raw)
    exports = {e.name: pe.OPTIONAL_HEADER.ImageBase + e.address
               for e in pe.DIRECTORY_ENTRY_EXPORT.symbols}
    assert exports[b'Z_SetSysOption'] == fixture['setter']
    if info is not None:
        assert exports[b'Z_SetDeviceInfo'] == fixture['info']
    u = Uc(UC_ARCH_X86, UC_MODE_32)
    base = pe.OPTIONAL_HEADER.ImageBase
    image = pe.get_memory_mapped_image()
    u.mem_map(base, (len(image) + 4095) & ~4095)
    u.mem_write(base, image)
    # All imports are redirected into a trap-only page. Unknown calls fail closed.
    traps = 0x20000000
    u.mem_map(traps, 0x10000)
    hooks = {}
    for descriptor in pe.DIRECTORY_ENTRY_IMPORT:
        for imported in descriptor.imports:
            addr = traps + len(hooks) * 16
            u.mem_write(imported.address, struct.pack('<I', addr))
            hooks[addr] = ('forbidden', descriptor.dll, imported.name, imported.ordinal)
    operate = struct.unpack('<I', u.mem_read(fixture['operate'], 4))[0]
    transport = struct.unpack('<I', u.mem_read(fixture['transport'], 4))[0]
    hooks[operate] = ('operate',)
    hooks[transport] = ('transport',)
    for key in ['sprintf', 'strlen', 'memset']:
        if fixture[key] is not None:
            hooks[fixture[key]] = (key,)
    for address in fixture['checks']:
        hooks[address] = ('check',)
    memory = 0x30000000
    u.mem_map(memory, 0x20000)
    stack, context, option, val, stop = (memory + x for x in
                                       [0x18000, 0x100, 0x1000, 0x2000, 0x1f000])
    u.mem_write(context, struct.pack('<I', 0x12345678))
    u.mem_write(context + fixture['timeout'], struct.pack('<I', 1234))
    u.mem_write(option, name.encode('ascii') + b'\0')
    u.mem_write(val, value.encode('ascii') + b'\0')
    args = [0 if null else context, info if info is not None else option,
            int(value) if info is not None else val]
    u.mem_write(stack, struct.pack('<4I', stop, *args))
    u.reg_write(UC_X86_REG_ESP, stack)
    sends, operations = [], []

    def dword(address):
        return struct.unpack('<I', u.mem_read(address, 4))[0]

    def string(address):
        data = bytearray()
        for offset in range(1024):
            b = u.mem_read(address + offset, 1)
            if b == b'\0':
                return bytes(data)
            data.extend(b)
        raise ValueError('Unbounded string in reviewed probe')

    def hook(emu, address, size, user):
        if address == stop:
            emu.emu_stop()
            return
        if address not in hooks:
            if traps <= address < traps + 0x10000:
                raise AssertionError('Unreviewed import entry')
            return
        kind = hooks[address][0]
        esp = emu.reg_read(UC_X86_REG_ESP)
        a = lambda i: dword(esp + 4 + i * 4)
        result = 0
        if kind == 'forbidden':
            raise AssertionError('Unreviewed import: ' + repr(hooks[address]))
        if kind == 'sprintf':
            fmt = string(a(1))
            if fmt == b'%s':
                data = string(a(2))
            elif fmt == b'%s=%s':
                data = string(a(2)) + b'=' + string(a(3))
            elif fmt == b'%s=%d':
                data = string(a(2)) + b'=' + str(a(3)).encode('ascii')
            else:
                raise AssertionError('Unreviewed CRT format: ' + repr(fmt))
            emu.mem_write(a(0), data + b'\0')
            result = len(data)
        elif kind == 'strlen':
            result = len(string(a(0)))
        elif kind == 'memset':
            assert a(2) <= 1024
            emu.mem_write(a(0), bytes([a(1) & 255]) * a(2))
            result = a(0)
        elif kind == 'operate':
            operations.append([a(0), a(1)])
        elif kind == 'transport':
            assert a(0) == 0x12345678 and a(3) == 0 and a(5) == 1234
            assert a(4) <= 1024
            sends.append(dict(command=struct.unpack('<H', emu.mem_read(a(1), 2))[0],
                              payloadHex=bytes(emu.mem_read(a(2), a(4))).hex()))
            emu.mem_write(a(1), struct.pack('<H', ack))
        # Cdecl CRT/import stubs preserve stack; reviewed caller cleans arguments.
        # Check helpers preserve EAX (the return value in debug epilogues).
        if kind != 'check':
            emu.reg_write(UC_X86_REG_EAX, result)
        emu.reg_write(UC_X86_REG_ESP, esp + 4)
        emu.reg_write(UC_X86_REG_EIP, dword(esp))

    u.hook_add(UC_HOOK_CODE, hook)
    u.emu_start(fixture['info'] if info is not None else fixture['setter'],
                stop, timeout=2_000_000, count=100_000)
    assert u.reg_read(UC_X86_REG_EIP) == stop, 'Probe did not finish within its bounds'
    result = u.reg_read(UC_X86_REG_EAX)
    expected = 0xffffffff if null else (4 if not name else (1 if ack == 2000 else 0xfffffffe))
    assert result == expected, (fixture['path'], name, result, expected)
    if null or not name:
        assert sends == []
    else:
        payload = f'{name}={value}'.encode('ascii') + b'\0'
        assert sends == [dict(command=12, payloadHex=payload.hex())], sends
    assert operations == ([] if null else [[0x12345678, 0]])
    return dict(method='Z_SetDeviceInfo' if info is not None else 'Z_SetSysOption',
                name=name, value=value, syntheticAck=ack, nullContext=null,
                infoCode=info, returnUnsigned=result, sends=sends)


def main():
    report = dict(kind='offline-x86-emulation-with-trapped-transport',
                  realComExecuted=False, firmwareVerified=False,
                  caveat='Generic option forwarding does not prove DHCP support on firmware',
                  fixtures=[])
    for fixture in FIXTURES:
        checks = []
        for name, value in [('TCPPort', '4371'), ('DHCP', '0'), ('DHCP', '1')]:
            for ack in [2000, 2001, 0]:
                checks.append(run(fixture, name, value, ack=ack))
        for port in ['1', '65535']:
            checks.append(run(fixture, 'TCPPort', port))
        # Negative control: arbitrary option forwarding is NOT firmware evidence.
        checks.append(run(fixture, 'IO_UNSUPPORTED_PROBE', '1'))
        checks.append(run(fixture, '', '1'))
        checks.append(run(fixture, 'TCPPort', '4371', null=True))
        if fixture['info'] is not None:
            for ack in [2000, 2001]:
                checks.append(run(fixture, 'TCPPort', '4371', info=70, ack=ack))
            # Public reverse-engineered table says 69; this binary disagrees.
            checks.append(run(fixture, 'ControlMinute', '4371', info=69))
        report['fixtures'].append(dict(path=fixture['path'], sha256=fixture['sha256'],
                                       probes=checks))
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--verify', action='store_true', help='Compare to committed evidence')
    args = parser.parse_args()
    if args.verify:
        evidence = ROOT / 'ReCode/docs/part-03/s11-network-native-dispatch-evidence.json'
        assert report == json.loads(evidence.read_text()), 'Committed evidence differs from emulation'
        count = sum(len(f['probes']) for f in report['fixtures'])
        print(f'PASS: {count} hash-pinned native setter probes; trapped transport; no COM/firmware acceptance')
    else:
        print(json.dumps(report, indent=2))


if __name__ == '__main__':
    main()
