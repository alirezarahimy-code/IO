#!/usr/bin/env python3
"""Read-only evidence extractor for the bundled PE/MSFT typelib. Never loads/executes COM.
Only the two daylight methods are reported; this is not a general typelib decompiler.
PE resource, MSFT segment/function layouts are decoded as data using stdlib only.
"""
import hashlib
import json
from pathlib import Path
import struct
import sys

ROOT = Path(__file__).resolve().parents[2]
DLL = ROOT / 'SDK 6.3.1.45/zkemkeeper.dll'
EXPECTED_DLL = '33b5d535cffdd14dd123d22d8a069004add468c71a7c8411a906f641b7ef2399'
EXPECTED_TLB = 'f7b8fba2f3d5352b9482a0f6394b916f998aac469ec08c0bd175f7b5c2fbd395'


def extract(path):
    pe = path.read_bytes()
    def word(p): return struct.unpack_from('<H', pe, p)[0]
    def dword(p): return struct.unpack_from('<I', pe, p)[0]
    assert pe[:2] == b'MZ'
    header = dword(60)
    assert pe[header:header+4] == b'PE\0\0'
    opt = header+24
    assert word(opt) == 0x10b, 'Reviewed fixture is PE32, not an inferred ABI'
    sections = []
    for index in range(word(header+6)):
        p = opt+word(header+20)+index*40
        sections.append((dword(p+12), max(dword(p+8), dword(p+16)), dword(p+20)))
    def offset(rva):
        for address, size, raw in sections:
            if address <= rva < address+size:
                return raw+rva-address
        raise ValueError('Unmapped RVA')
    resource = offset(dword(opt+96+2*8))
    def entries(relative):
        p = resource+relative
        for i in range(word(p+12)+word(p+14)):
            e = p+16+i*8
            key, value = dword(e), dword(e+4)
            if key & 0x80000000:
                k = resource+(key & 0x7fffffff)
                key = pe[k+2:k+2+word(k)*2].decode('utf-16-le')
            yield key, value
    type_entry = next(v for k, v in entries(0) if k == 'TYPELIB')
    assert type_entry & 0x80000000
    ident, directory = next(iter(entries(type_entry & 0x7fffffff)))
    language, data = next(iter(entries(directory & 0x7fffffff)))
    assert not data & 0x80000000
    data += resource
    start, size = offset(dword(data)), dword(data+4)
    b = pe[start:start+size]
    assert b[:4] == b'MSFT'
    def u(p): return struct.unpack_from('<I', b, p)[0]
    def h(p): return struct.unpack_from('<H', b, p)[0]
    n = u(32)
    directory = 84+4*n+(4 if u(20) & 256 else 0)
    segments = [(u(directory+i*16), u(directory+i*16+4)) for i in range(15)]
    def name(relative):
        p = segments[7][0]+relative
        return b[p+12:p+12+(u(p+8) & 255)].decode('cp1252')
    def datatype(value):
        if value & 0x80000000:
            return {3: 'LONG', 8: 'BSTR', 11: 'VARIANT_BOOL', 25: 'HRESULT'}[value & 4095]
        p = segments[9][0]+value
        assert h(p) & 4095 == 26, 'Expected pointer typedesc'
        return 'PTR '+datatype(u(p+4))
    methods = []
    for i in range(n):
        p = segments[0][0]+i*100
        count, memory = u(p+24) & 65535, u(p+4)
        if not count or u(p) & 15 == 5:
            continue
        cursor, records = memory+4, []
        for _ in range(count):
            records.append(cursor)
            cursor += h(cursor)
        for j, r in enumerate(records):
            method = name(u(cursor+count*4+j*4))
            if method not in ('SetDaylight', 'GetDaylight'):
                continue
            size, argc, flags = h(r), h(r+20), u(r+16)
            extra = size//4-6-argc*(4 if flags & 4096 else 3)
            args = r+24+extra*4+(argc*4 if flags & 4096 else 0)
            methods.append({'interface': name(u(p+52)), 'method': method,
                            'dispid': u(cursor+j*4), 'recordOffset': r, 'return': datatype(u(r+4)),
                            'parameters': [{'name': name(u(args+k*12+4)), 'type': datatype(u(args+k*12)),
                                            'flags': u(args+k*12+8)} for k in range(argc)]})
    return {'source': str(path.relative_to(ROOT)), 'dllSha256': hashlib.sha256(pe).hexdigest(),
            'resourceId': ident, 'languageId': language, 'typelibBytes': len(b),
            'typelibSha256': hashlib.sha256(b).hexdigest(), 'methods': methods,
            'executedBinary': False, 'originalSourceAvailable': False, 'hardwareVerified': False}


if __name__ == '__main__':
    result = extract(DLL)
    assert result['dllSha256'] == EXPECTED_DLL and result['typelibSha256'] == EXPECTED_TLB
    setter, getter = result['methods']
    assert (setter['method'], setter['dispid'], getter['method'], getter['dispid']) == ('SetDaylight', 178, 'GetDaylight', 179)
    assert [(x['type'], x['flags']) for x in getter['parameters']] == [('LONG', 1), ('PTR LONG', 3), ('PTR BSTR', 3), ('PTR BSTR', 3), ('PTR VARIANT_BOOL', 10)]
    rendered = json.dumps(result, ensure_ascii=False, indent=2)+'\n'
    if sys.argv[1:] == ['--verify']:
        saved = ROOT/'ReCode/docs/part-03/s11-daylight-typelib-evidence.json'
        assert json.loads(saved.read_text()) == result
        other = extract(ROOT/'NewPack Dlls 10.06.1398/zkemkeeper.dll')
        assert other['dllSha256'] == '261b769b6923a4df8fa266955f18b3431f6c6bd202130f996640e367dfe9c5e2'
        assert other['typelibSha256'] == 'ecbc3b9f6301bd49e36f9738bd83c98c8b7f8954469de270a4525137d0ccd414'
        assert [{k:v for k,v in m.items() if k != 'recordOffset'} for m in other['methods']] == [{k:v for k,v in m.items() if k != 'recordOffset'} for m in result['methods']]
        assert json.loads((ROOT/'ReCode/docs/part-03/s11-daylight-newpack-typelib-evidence.json').read_text()) == other
        print('PASS two read-only daylight typelib hashes, exact signatures and checked-in evidence; no SDK execution')
    elif sys.argv[1:] == ['--newpack']:
        print(json.dumps(extract(ROOT/'NewPack Dlls 10.06.1398/zkemkeeper.dll'), ensure_ascii=False, indent=2))
    else:
        assert not sys.argv[1:]
        print(rendered, end='')
