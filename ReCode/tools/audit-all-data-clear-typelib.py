#!/usr/bin/env python3
"""Hash-pinned ABI extraction, no COM execution, SDK connection or destructive command."""
import hashlib,importlib.util,json,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('reader',ROOT/'ReCode/tools/inspect-zkem-daylight-typelib.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
names=('ClearData','Connect_Net','Connect_USB','Connect_Com','Disconnect','SetCommPassword','GetDeviceStatus','ReadAllGLogData','SSR_GetGeneralLogData','GetGeneralLogData','ReadAllUserID','GetUserTmpStr','GetUserTmpExStr')
result={'scope':'R15 fixed ClearData(machine,5), NOT factory reset. Original endpoint/key reconnect is read-only and is not a command retry. R16 ABI proves available Connect_USB/Connect_Com signatures, NOT model/driver/transport support, exclusive port ownership or deployment approval.', 'sources':[m.extract(ROOT/p,names,True) for p in ['SDK 6.3.1.45/zkemkeeper.dll','NewPack Dlls 10.06.1398/zkemkeeper.dll']], 'nativeExecuted':False,'hardwareVerified':False}
reference=ROOT/'T5V3.9.2/Personnel.DLL'
assert hashlib.sha256(reference.read_bytes()).hexdigest()=='185b084072a552e92ab0c8b0c2a03ca926b2aa362b5aada1ca8a39f998144be4'
result['reference']={'path':'T5V3.9.2/Personnel.DLL','sha256':'185b084072a552e92ab0c8b0c2a03ca926b2aa362b5aada1ca8a39f998144be4','type':'Personnel.ZK.ZKDevice','decompiledBodySha256':'58e33195ab271660e2f48b81d80167b91c693cb7986b955ee14c94d1b58b7eb5','provenance':'ILSpy v9.1 scratch read-only decompilation recorded in zk-reference-contract.json and model-and-policy-decisions.fa.md. Verification checks binary hash and native TypeLib signatures, not a new runtime or decompiler execution.','observed':[{'lines':'216-226','calls':['Connect_Net(IP, Port)','Connect_USB(MachineNumber)','Connect_Com(1, MachineNumber, 115200)']},{'lines':'708-716','calls':['ClearData(MachineNumber, 5)']}],'limits':'Reference branch and ABI are real; fixed COM1/115200 is not installed port ownership/driver support. ClearData5 is not evidence of factory reset.'}
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
