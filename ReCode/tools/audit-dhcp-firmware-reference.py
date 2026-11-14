#!/usr/bin/env python3
"""Read-only, hash-pinned audit of a public historical firmware source reference.
No downloaded source is executed, built, installed or incorporated in the product.
The source is vendor-attributed, not an authenticated official vendor release.
"""
import argparse
import base64
import hashlib
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[2]
COMMIT = 'de2de02fd0224f96d65a350ecad5efbc84ab230e'
REPO = 'eric4409/jz4725'
PINS = {
    'options.c': ('9012121dcfe65c8c2ba69018e74bb68751e5051b', 'b9009e251848894d7ac8e3cec166664eea71844537134c12249528f8dce72cd5'),
    'options.h': ('9515b1dd072574e05d3af2b3827e84797446c371', '9ea4173052814bdb6c1a4ad7ce6f3d1f30e26bbc8874adf175ed132df5a4338c'),
    'commu.c': ('ae6e480646f90037cd6be641efd53c7515cf8c3e', '17c3980461e29c0a6bed14ab4f3c5d3dc72adc07b224f72d831b1808b44acacf'),
    'commu.h': ('e6020bc23600a53d1103172b62633010d0859069', '4d641208bc20b8dc9da1e010c315cd4ec1fd09534a2a9afcb192c0a1ecfc3a54'),
}


def verify(source_dir=None):
    evidence = json.loads((ROOT / 'ReCode/docs/part-03/s11-dhcp-firmware-reference-evidence.json').read_text())
    assert evidence['repository'] == REPO and evidence['commit'] == COMMIT
    assert evidence['targetFirmwareVerified'] is False and evidence['deviceExecuted'] is False
    assert len(evidence['sources']) == len(PINS)
    sources = {}
    for entry in evidence['sources']:
        name = Path(entry['path']).name
        assert name in PINS and name not in sources
        blob, sha = PINS[name]
        assert entry['path'] == 'jz4740/firmware/' + name
        assert entry['gitBlob'] == blob and entry['sha256'] == sha
        if source_dir:
            raw = (source_dir / name).read_bytes()
        else:
            # Use raw Git blobs: Contents API may transcode legacy non-UTF8 source.
            response = json.loads(subprocess.check_output(
                ['gh', 'api', f'repos/{REPO}/git/blobs/{blob}'], timeout=30))
            assert response['sha'] == blob and response['encoding'] == 'base64'
            raw = base64.b64decode(response['content'])
            assert len(raw) == response['size']
        assert hashlib.sha1(b'blob ' + str(len(raw)).encode() + b'\0' + raw).hexdigest() == blob, name
        assert hashlib.sha256(raw).hexdigest() == sha, name
        text = raw.decode('gb18030', errors='strict')
        lines = text.splitlines()
        for excerpt in entry['excerpts']:
            first, last = excerpt['firstLine'], excerpt['lastLine']
            assert 1 <= first <= last <= len(lines)
            assert excerpt['text'] == '\n'.join(lines[first-1:last]), (name, first)
        sources[name] = text
    header, options, comms, codes = (sources[n] for n in ['options.h', 'options.c', 'commu.c', 'commu.h'])
    # Table fields are interpreted by the real struct, NOT guessed as access flags.
    structure = re.search(r'typedef struct __OPTIONSRESINT__\{(.*?)\}TOptionsResInt', header, re.S).group(1)
    assert re.findall(r'\b(?:int|char)\s*\*?\s*(\w+)\s*;', structure) == [
        'OptionName', 'DefaultValue', 'IsNeedRestoreFactory', 'Offset', 'MenuResID', 'MaxValue', 'MinValue']
    assert re.search(r'\{"DHCP",\s*0,\s*1,\s*optoffset\(DHCP\),\s*0,\s*1,\s*0\s*\}', options)
    assert re.search(r'int\s+DHCP;[^\n]*DHCP enable or disable', header)
    assert re.search(r'#define\s+CMD_OPTIONS_WRQ\s+12\b', codes)
    handler = comms.split('case CMD_OPTIONS_WRQ:', 1)[1].split('case CMD_ATTLOG_RRQ:', 1)[0]
    assert 'SaveStr(p,value,TRUE)' in handler and "'='==*value++" in handler
    save = options.split('BOOL SaveStr(', 1)[1].split('char *GetCardKeyStr', 1)[0]
    assert 'write(fdOptions, buffer, strlen(buffer))' in save and 'yaffs_flush(fdOptions)' in save
    load = options.split('POptions LoadOptions(', 1)[1].split('char GetLangFileType', 1)[0]
    assert 'strcmp(name1, OptionsResInt[i].OptionName)==0' in load
    assert 'memcpy(((char*)opts)+OptionsResInt[i].Offset, &value, 4)' in load
    # Explicit caveat: storage is not activation. No implicit refresh/reboot is added.
    assert 'LoadOptions(&gOptions)' in comms.split('case CMD_REFRESHOPTION:', 1)[1].split('break;', 1)[0]
    print('PASS DHCP firmware reference:4 exact Git blobs/SHA256/excerpts; recognized0..1, command12 persistence and reload path; NOT target firmware/device acceptance')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-dir', type=Path, help='Local raw blobs for offline reproduction; otherwise reads pinned GitHub blobs using gh')
    verify(parser.parse_args().source_dir)
