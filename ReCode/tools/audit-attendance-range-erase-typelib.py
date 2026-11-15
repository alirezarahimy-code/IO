#!/usr/bin/env python3
"""Read-only ABI evidence generation/verification. No COM/device/template execution."""
import importlib.util,json,sys
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('reader',ROOT/'ReCode/tools/inspect-zkem-daylight-typelib.py')
m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m)
names=('DeleteAttlogBetweenTheDate','GetDeviceStatus','ReadAllGLogData','SSR_GetGeneralLogData','GetGeneralLogData','GetSerialNumber','GetProductCode','GetFirmwareVersion')
result={'scope':'R12 exact DeleteAttlogBetweenTheDate(machine,from,through) ABI and full log read ABI. Legacy yyyy-MM-dd HH:mm:ss format is reference only, not installed inclusive-boundary, precision or physical capture-stop proof.',
 'sources':[m.extract(ROOT/p,names,True) for p in ['SDK 6.3.1.45/zkemkeeper.dll','NewPack Dlls 10.06.1398/zkemkeeper.dll']],
 'nativeExecuted':False,'hardwareVerified':False}
p=ROOT/'ReCode/docs/part-03/r12-attendance-range-erase-typelib-evidence.json'
if sys.argv[1:]==['--write']:
 p.write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
elif sys.argv[1:]==['--verify']:
 assert json.loads(p.read_text(encoding='utf-8'))==result
 for source in result['sources']:
  methods={x['method']:x for x in source['methods']}
  assert methods['DeleteAttlogBetweenTheDate']['interfaceGuid']=='102f4206-e43d-4fc9-bab0-331cffe4d25b'
  assert [(x['type'],x['flags']) for x in methods['DeleteAttlogBetweenTheDate']['parameters']]==[('LONG',1),('BSTR',1),('BSTR',1),('PTR VARIANT_BOOL',10)]
 print('PASS R12 two pinned type libraries, DeleteAttlogBetweenTheDate LONG/BSTR/BSTR ABI and full native read ABI; no installed boundary proof or device execution')

else:raise SystemExit('Use --write for extraction or --verify for CI')
