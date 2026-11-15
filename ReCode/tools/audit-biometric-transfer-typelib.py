#!/usr/bin/env python3
"""Read-only ABI evidence generation/verification. No COM/device/template execution."""
import importlib.util,json,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('reader',ROOT/'ReCode/tools/inspect-zkem-daylight-typelib.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
names=('ReadAllUserID','SSR_GetAllUserInfo','ReadAllTemplate','GetUserTmpExStr','SetUserTmpExStr','GetUserFaceStr','SetUserFaceStr','ReadFile','SendFile')
result={'scope':'Fingerprint/face BSTR exchange ABIs plus historical photo file methods; metadata is not installed-model/format compatibility or authorization. Photo executor intentionally not yet implemented.',
 'sources':[m.extract(ROOT/p,names,True) for p in ['SDK 6.3.1.45/zkemkeeper.dll','NewPack Dlls 10.06.1398/zkemkeeper.dll']],
 'nativeExecuted':False,'hardwareVerified':False}
p=ROOT/'ReCode/docs/part-03/r10-biometric-typelib-evidence.json'
if sys.argv[1:]==['--write']:
 p.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
elif sys.argv[1:]==['--verify']:
 assert json.loads(p.read_text(encoding='utf-8'))==result
 expected={'GetUserTmpExStr':236,'SetUserTmpExStr':238,'GetUserFaceStr':233,'SetUserFaceStr':234}
 for source in result['sources']:
  methods={x['method']:x for x in source['methods']}
  for name,dispid in expected.items():assert methods[name]['dispid']==dispid and methods[name]['interfaceGuid']=='102f4206-e43d-4fc9-bab0-331cffe4d25b'
  assert [(x['type'],x['flags']) for x in methods['GetUserFaceStr']['parameters']]==[('LONG',1),('BSTR',1),('LONG',1),('PTR BSTR',3),('PTR LONG',3),('PTR VARIANT_BOOL',10)]
  assert [(x['type'],x['flags']) for x in methods['SetUserTmpExStr']['parameters']]==[('LONG',1),('BSTR',1),('LONG',1),('LONG',1),('BSTR',1),('PTR VARIANT_BOOL',10)]
 print('PASS R10 two pinned type libraries, exact BSTR getter/setter ABIs; no SDK/model/biometric execution')
else:raise SystemExit('Use --write for extraction or --verify for CI')
