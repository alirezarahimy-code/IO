#!/usr/bin/env python3
"""Read-only pinned RemoteEnroll signatures. No COM load, device, subject or template data."""
import importlib.util
import json
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('io_typelib',ROOT/'ReCode/tools/inspect-zkem-daylight-typelib.py')
reader=importlib.util.module_from_spec(spec)
spec.loader.exec_module(reader)
names=('StartEnrollEx','CancelOperation','OnEnrollFingerEx')
evidence=json.loads((ROOT/'ReCode/docs/part-03/s09-remote-enroll-typelib-evidence.json').read_text(encoding='utf-8'))
assert evidence['nativeExecuted'] is False and evidence['hardwareVerified'] is False
pins={
 'SDK 6.3.1.45/zkemkeeper.dll':('33b5d535cffdd14dd123d22d8a069004add468c71a7c8411a906f641b7ef2399','f7b8fba2f3d5352b9482a0f6394b916f998aac469ec08c0bd175f7b5c2fbd395'),
 'NewPack Dlls 10.06.1398/zkemkeeper.dll':('261b769b6923a4df8fa266955f18b3431f6c6bd202130f996640e367dfe9c5e2','ecbc3b9f6301bd49e36f9738bd83c98c8b7f8954469de270a4525137d0ccd414')}
assert len(evidence['sources'])==2
for source in evidence['sources']:
    actual=reader.extract(ROOT/source['source'],names)
    assert actual==source
    assert (actual['dllSha256'],actual['typelibSha256'])==pins[source['source']]
    methods={m['method']:m for m in actual['methods']}
    assert set(methods)==set(names)
    assert methods['StartEnrollEx']['dispid']==203 and methods['CancelOperation']['dispid']==91 and methods['OnEnrollFingerEx']['dispid']==18
    def params(name):return [(p['name'],p['type'],p['flags']) for p in methods[name]['parameters']]
    assert params('StartEnrollEx')==[('UserID','BSTR',1),('FingerID','LONG',1),('Flag','LONG',1),('pVal','PTR VARIANT_BOOL',10)]
    assert params('CancelOperation')==[('pVal','PTR VARIANT_BOOL',10)]
    assert params('OnEnrollFingerEx')==[('EnrollNumber','BSTR',1),('FingerIndex','LONG',1),('ActionResult','LONG',1),('TemplateLength','LONG',1)]
    assert methods['OnEnrollFingerEx']['return']=='VOID'
print('PASS RemoteEnroll two pinned type libraries: BSTR StartEnrollEx, CancelOperation and completion metadata; no native GUID, template bytes, SDK execution or hardware acceptance')

adapter=json.loads((ROOT/'ReCode/docs/part-03/s09-enroll-adapter-typelib-evidence.json').read_text(encoding='utf-8'))
assert len(adapter['sources'])==2
for source in adapter['sources']:
    actual=reader.extract(ROOT/source['source'],('RegEvent','GetUserTmpExStr','GetLastError','OnEnrollFingerEx'),True)
    assert actual==source and (actual['dllSha256'],actual['typelibSha256'])==pins[source['source']]
    methods={m['method']:m for m in actual['methods']}
    assert methods['OnEnrollFingerEx']['interfaceGuid']=='cf83b580-5d32-4c65-b44e-bedc750cdfa8'
    assert [(p['type'],p['flags']) for p in methods['GetUserTmpExStr']['parameters']]==[('LONG',1),('BSTR',1),('LONG',1),('PTR LONG',2),('PTR BSTR',2),('PTR LONG',2),('PTR VARIANT_BOOL',10)]
    assert [(p['type'],p['flags']) for p in methods['GetLastError']['parameters']]==[('PTR LONG',3)]
    assert [(p['type'],p['flags']) for p in methods['RegEvent']['parameters']]==[('LONG',1),('LONG',1),('PTR VARIANT_BOOL',10)]
print('PASS two pinned adapter ABIs/event IID; presence probe may transiently materialize a template only inside child; no firmware error semantics or execution claimed')
