#!/usr/bin/env python3
"""Hash-pinned ABI extraction, no COM execution, SDK connection or destructive command."""
import importlib.util,json,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('reader',ROOT/'ReCode/tools/inspect-zkem-daylight-typelib.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
names=('ClearData','Connect_Net','Connect_USB','Connect_Com','Disconnect','SetCommPassword','GetDeviceStatus','ReadAllGLogData','SSR_GetGeneralLogData','GetGeneralLogData','ReadAllUserID','GetUserTmpStr','GetUserTmpExStr')
result={'scope':'R15 fixed ClearData(machine,5), NOT factory reset. Original endpoint/key reconnect is read-only and is not a command retry. R16 ABI proves available Connect_USB/Connect_Com signatures, NOT model/driver/transport support, exclusive port ownership or deployment approval.', 'sources':[m.extract(ROOT/p,names,True) for p in ['SDK 6.3.1.45/zkemkeeper.dll','NewPack Dlls 10.06.1398/zkemkeeper.dll']], 'nativeExecuted':False,'hardwareVerified':False}
p=ROOT/'ReCode/docs/part-03/r15-r16-native-abi-evidence.json'
if sys.argv[1:]==['--write']:p.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
elif sys.argv[1:]==['--verify']:
 assert json.loads(p.read_text(encoding='utf-8'))==result
 for source in result['sources']:
  methods={x['method']:x for x in source['methods']}
  for method,signature in {'ClearData':[('LONG',1),('LONG',1),('PTR VARIANT_BOOL',10)],'Connect_Net':[('BSTR',1),('LONG',1),('PTR VARIANT_BOOL',10)],'SetCommPassword':[('LONG',1),('PTR VARIANT_BOOL',10)],'Disconnect':[],'Connect_USB':[('LONG',1),('PTR VARIANT_BOOL',10)],'Connect_Com':[('LONG',1),('LONG',1),('LONG',1),('PTR VARIANT_BOOL',10)]}.items():
   assert methods[method]['interfaceGuid']=='102f4206-e43d-4fc9-bab0-331cffe4d25b'
   assert [(x['type'],x['flags']) for x in methods[method]['parameters']]==signature
 print('PASS R15 fixed clear/read-only reconnect and R16 transport ABI in two pinned type libraries; not factory reset or installed transport acceptance')
else:raise SystemExit('Use --write or --verify')
