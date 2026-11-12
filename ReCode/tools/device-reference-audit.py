#!/usr/bin/env python3
"""Verify the reviewed device crosswalk against immutable reference hashes.
--inventory also repeats read-only decompilation (IO_ILSPY_RUNNER + dotnet required).
Never executes or edits legacy assemblies, config, services, or databases.
Review dispositions are human decisions, not inferred from identifier presence.
"""
import argparse,csv,hashlib,json,os,pathlib,re,subprocess
ROOT=pathlib.Path(__file__).resolve().parents[2]
OUT=ROOT/'ReCode/docs/part-03'
COMMIT='2c01d1074ebf18adc3c43063dd14dbe639246e04'
BINARY='T5Web [3.5.7]/App/bin/T5V3Web.dll'
TYPES=['DeviceController','DeviceOperationsController','iclockController','USBManagementController']
ARIA='src/T5Web/T5Web.Web/Controllers/DeviceController.cs'
def sha(b):return hashlib.sha256(b).hexdigest()
def git_source(path):return subprocess.check_output(['git','show',COMMIT+':'+path],cwd=ROOT)
def declarations(text):
    # Inventory only; not a C# semantic/authorization analyzer. Overloads kept.
    seen={};rows=[]
    for m in re.finditer(r'^[ \t]*public (?:async )?(?:ActionResult|JsonResult) (\w+)\(([^\n]*)\)',text,re.M):
        name=m.group(1);seen[name]=seen.get(name,0)+1
        rows.append({'member':name,'overload':seen[name],'line':text[:m.start()].count('\n')+1,'signature':m.group(0).strip()})
    return rows

def verify(repeat=False):
    manifest=json.loads((OUT/'device-reference-inventory.json').read_text(encoding='utf-8'))
    assert manifest['originalSourceAvailable'] is False and manifest['part3Complete'] is False
    expected=[]
    for source in manifest['sources']:
        if source.get('commit'):
            data=git_source(source['path'])
        else:
            # GitHub Windows checkout may convert tracked text LF to CRLF.
            # Provenance is the exact Git blob; local edits still fail, except EOL.
            data=subprocess.check_output(['git','show','HEAD:'+source['path']],cwd=ROOT)
            working=(ROOT/source['path']).read_bytes()
            if pathlib.Path(source['path']).suffix=='.cshtml':
                assert working.replace(b'\r\n',b'\n')==data.replace(b'\r\n',b'\n'),f"Working reference edited: {source['path']}"
            else:
                assert working==data,f"Working binary edited: {source['path']}"
        assert sha(data)==source['sha256'],f"Reference changed: {source['path']}"
        if source.get('type'):
            if repeat:
                runner=os.environ['IO_ILSPY_RUNNER'];dotnet=os.environ.get('IO_DOTNET','dotnet')
                text=subprocess.check_output([dotnet,runner,str(ROOT/source['path']),source['type']],cwd=ROOT,text=True)
                assert sha(text.encode())==source['decompiledSha256'],f"Decompiler output changed: {source['type']}"
                assert declarations(text)==source['actions']
            for action in source['actions']:expected.append((source['label'],source['type'].split('.')[-1],action['member'],str(action['overload'])))
        elif source.get('controller'):
            assert declarations(data.decode('utf-8-sig'))==source['actions']
            for a in source['actions']:expected.append((source['label'],source['controller'],a['member'],str(a['overload'])))
    with (OUT/'device-reference-crosswalk.csv').open(encoding='utf-8-sig',newline='') as f:rows=list(csv.DictReader(f))
    actual=[(r['source'],r['controller'],r['member'],r['overload'])for r in rows if r['source']!='AriaAdmin-catalog']
    assert len(actual)==len(set(actual)) and set(actual)==set(expected),'Missing duplicate or unreviewed reference action'
    assert len(rows)==len(expected)+5
    assert {r['member']for r in rows if r['source']=='AriaAdmin-catalog'}=={'sync-users','sync-time','pull-punches','pull-users','clear-punches'}
    statuses={'software-verified','partial-software','intentional-replacement','pending-contract','pending-sensitive','pending-secure-push'}
    for r in rows:
        assert r['status'] in statuses and r['evidence'] and r['remaining'] and r['targetFiles']
        for field in ['targetFiles','tests']:
            for name in r[field].split(';'):
                if name:assert (ROOT/name.strip()).is_file(),name
        assert r['hardwareVerified']=='false'
    print(f"PASS device reference audit: {len(expected)} action declarations + 5 AriaAdmin catalog entries, exact hash provenance, explicit dispositions; NOT feature or hardware completion")
    return manifest,rows
if __name__=='__main__':
    parser=argparse.ArgumentParser();parser.add_argument('--inventory',action='store_true');args=parser.parse_args()
    try:verify(args.inventory)
    except Exception as error:
        if os.environ.get('GITHUB_ACTIONS')=='true':
            message=str(error).replace('%','%25').replace('\r','%0D').replace('\n','%0A')
            print('::error title=Device reference audit::'+message)
        raise
