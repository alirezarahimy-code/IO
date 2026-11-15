#!/usr/bin/env python3
"""Read-only ABI evidence generation/verification. No COM/device/template execution."""
import importlib.util,json,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('reader',ROOT/'ReCode/tools/inspect-zkem-daylight-typelib.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
names=('SSR_DeleteEnrollDataExt','DeleteEnrollData','GetUserTmpStr','GetUserTmpExStr','ReadAllTemplate','ReadAllUserID','SSR_GetAllUserInfo','GetAllUserInfo','GetStrCardNumber','GetLastError','GetDeviceStatus','GetSerialNumber','GetProductCode','GetFirmwareVersion')
result={'scope':'R13 core only: SSR_DeleteEnrollDataExt(machine,string,12 user or13 fingerprints) versus DeleteEnrollData(machine,int,machine,12 user or11 fingerprints); no fallback or flag equivalence. Complete transient protected catalogue reads. Legacy reference flags are not installed effect, no-other-stores, absence-error, authorization or physical exclusive-control proof.',
 'sources':[m.extract(ROOT/p,names,True) for p in ['SDK 6.3.1.45/zkemkeeper.dll','NewPack Dlls 10.06.1398/zkemkeeper.dll']],
 'nativeExecuted':False,'hardwareVerified':False}
p=ROOT/'ReCode/docs/part-03/r13-user-erase-typelib-evidence.json'
if sys.argv[1:]==['--write']:
 p.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
elif sys.argv[1:]==['--verify']:
 assert json.loads(p.read_text(encoding='utf-8'))==result
 for source in result['sources']:
  methods={x['method']:x for x in source['methods']}
  for method,signature in {
   'SSR_DeleteEnrollDataExt':[('LONG',1),('BSTR',1),('LONG',1),('PTR VARIANT_BOOL',10)],
   'DeleteEnrollData':[('LONG',1),('LONG',1),('LONG',1),('LONG',1),('PTR VARIANT_BOOL',10)],
   'GetUserTmpExStr':[('LONG',1),('BSTR',1),('LONG',1),('PTR LONG',2),('PTR BSTR',2),('PTR LONG',2),('PTR VARIANT_BOOL',10)],
   'GetUserTmpStr':[('LONG',1),('LONG',1),('LONG',1),('PTR BSTR',3),('PTR LONG',3),('PTR VARIANT_BOOL',10)]}.items():
   assert methods[method]['interfaceGuid']=='102f4206-e43d-4fc9-bab0-331cffe4d25b'
   assert [(x['type'],x['flags']) for x in methods[method]['parameters']]==signature
 print('PASS R13 two pinned type libraries, distinct SSR/legacy deletion and protected-read ABI; core only, no installed effect proof or native dispatch')

else:raise SystemExit('Use --write for extraction or --verify for CI')
