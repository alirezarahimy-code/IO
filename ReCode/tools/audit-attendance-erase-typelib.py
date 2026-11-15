#!/usr/bin/env python3
"""Read-only ABI evidence generation/verification. No COM/device/template execution."""
import importlib.util,json,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('reader',ROOT/'ReCode/tools/inspect-zkem-daylight-typelib.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
names=('ClearData','GetDeviceStatus','ReadAllGLogData','SSR_GetGeneralLogData','GetGeneralLogData','GetSerialNumber','GetProductCode','GetFirmwareVersion')
result={'scope':'R11 fixed ClearData(machine,1) plus exact native log read ABI. Legacy Personnel DeleteAllRecords uses flag1; flag5 belongs to all-data clear, not factory-reset proof. No installed quiescence or firmware support inferred.',
 'sources':[m.extract(ROOT/p,names,True) for p in ['SDK 6.3.1.45/zkemkeeper.dll','NewPack Dlls 10.06.1398/zkemkeeper.dll']],
 'nativeExecuted':False,'hardwareVerified':False}
p=ROOT/'ReCode/docs/part-03/r11-attendance-erase-typelib-evidence.json'
if sys.argv[1:]==['--write']:
 p.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
elif sys.argv[1:]==['--verify']:
 assert json.loads(p.read_text(encoding='utf-8'))==result
 for source in result['sources']:
  methods={x['method']:x for x in source['methods']}
  assert methods['ClearData']['interfaceGuid']=='102f4206-e43d-4fc9-bab0-331cffe4d25b'
  assert [(x['type'],x['flags']) for x in methods['ClearData']['parameters']]==[('LONG',1),('LONG',1),('PTR VARIANT_BOOL',10)]
 print('PASS R11 two pinned type libraries, ClearData LONG/LONG ABI and full native read ABI; no device/capture-quiescence execution')

else:raise SystemExit('Use --write for extraction or --verify for CI')
