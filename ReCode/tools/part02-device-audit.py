#!/usr/bin/env python3
"""Read-only, reproducible device evidence extraction. Never load or execute legacy binaries."""
from pathlib import Path
import csv, hashlib, re, struct, json
ROOT=Path(__file__).resolve().parents[2]; OUT=ROOT/'ReCode/docs/part-02';OUT.mkdir(parents=True,exist_ok=True)
def write(name,headers,rows):
 with (OUT/name).open('w',encoding='utf-8-sig',newline='') as f:
  w=csv.writer(f,lineterminator='\n');w.writerow(headers);w.writerows(rows)
views=ROOT/'T5Web [3.5.7]/App/Views'; files=[]
for folder in ['Device','DeviceOperations','USBManagement','iclock','UserInfo','Gate']:
 files.extend(sorted((views/folder).glob('*.cshtml')))
files.extend([views/'Shared/_DeviceSelection.cshtml',views/'Shared/_SelectUser.cshtml'])
rows=[];coverage=[]
patterns={'field':r'\b(?:name|id)\s*=\s*["\']([^"\'<>\s]+)', 'razor-control':r'@Html\.(?:DropDownList|TextBox|CheckBox)(?:For)?\("([^"]+)"', 'function':r'function\s+(\w+)\s*\(', 'endpoint':r'(?:url|action)\s*[:=]\s*["\'](/[^"\']+)', 'route-literal':r'["\'](/(?:Device|DeviceOperations|USBManagement|iclock)/[^"\']*)', 'operation':r'onclick\s*=\s*"([\w]+)\(', 'caption':r'T5V3Web\.Content\.Persian\.(\w+)'}
for p in files:
 rel=str(p.relative_to(ROOT))
 if not p.exists():coverage.append([rel,'missing','not present in supplied snapshot']);continue
 try:text=p.read_text(encoding='utf-8-sig')
 except UnicodeError as e:coverage.append([rel,'unreadable',type(e).__name__]);continue
 coverage.append([rel,'read',hashlib.sha256(p.read_bytes()).hexdigest()])
 for n,line in enumerate(text.splitlines(),1):
  for kind,pat in patterns.items():
   for m in re.finditer(pat,line):rows.append([rel,n,kind,m[1], 'outside physical gate scope' if '/Gate/' in rel else 'reference evidence; not an IO implementation'])
write('device-view-evidence.csv',['file','line','kind','identifier','disposition'],rows)
write('device-view-coverage.csv',['file','status','sha256_or_reason'],coverage)
# Parse PE headers and native exported names only. No plaintext configuration, keys or payloads are exported.
def pe(data):
 u16=lambda o:struct.unpack_from('<H',data,o)[0];u32=lambda o:struct.unpack_from('<I',data,o)[0]
 if data[:2]!=b'MZ':raise ValueError('not PE')
 base=u32(0x3c)
 if data[base:base+4]!=b'PE\0\0':raise ValueError('invalid PE signature')
 machine=u16(base+4); count=u16(base+6); opts=u16(base+20); opt=base+24; magic=u16(opt); dd=opt+(112 if magic==0x20b else 96)
 sections=[]
 for i in range(count):
  s=opt+opts+i*40;sections.append((u32(s+12),max(u32(s+8),u32(s+16)),u32(s+20)))
 def off(rva):
  for start,size,raw in sections:
   if start<=rva<start+size:return raw+rva-start
  if rva<len(data):return rva
  raise ValueError('RVA outside sections')
 exp=u32(dd); names=[]
 if exp:
  e=off(exp);total=u32(e+24);ptr=off(u32(e+32))
  for i in range(min(total,50000)):
   n=off(u32(ptr+i*4));end=data.index(b'\0',n);names.append(data[n:end].decode('ascii','replace'))
 clr=u32(dd+14*8) if dd+15*8<=opt+opts else 0
 return {0x14c:'x86',0x8664:'x64',0xaa64:'arm64'}.get(machine,hex(machine)),bool(clr),names
binary=[];exports=[];symbols=[]
candidates=[]
for folder in ['SDK 6.3.1.45','NewPack Dlls 10.06.1398']:
 candidates+=sorted((ROOT/folder).glob('*.dll'))+sorted((ROOT/folder).glob('*.ocx'))
for folder in ['T5V3.9.2','Link5 v4.0.4','T5Web [3.5.7]/App/bin','TWF 4.28/App/bin']:
 for name in ['T5V3.exe','Link5.exe','Comm5Serv v3.exe','Device.dll','T5V3Web.dll','T5V3.Model.dll','Personnel.dll','MyTime3.dll','TD5.dll','T5V3.UI.DLL','T5V3.ZK.DLL']:
  p=ROOT/folder/name
  if p.exists():candidates.append(p)
needles=['ActiveDevices','AddUploadCommand','ConnectAndReadAllRecords','ConnectUDPAndReadAllRecords','MachineNumber','ClearTimeRecords','HTTPPushServer','ConfigPushServer','GetDeviceData','GetDeviceParam','CZKEM','iclockController','DeviceOperationsController','DownloadUsers','UploadUsers','RemoteEnroll','AutoDownload','AutoDeleteRecordsAfterDownload','CommPassword','PushServer']
for p in candidates:
 data=p.read_bytes();rel=str(p.relative_to(ROOT));sha=hashlib.sha256(data).hexdigest()
 try:arch,managed,names=pe(data);binary.append([rel,len(data),sha,arch,'managed metadata present' if managed else 'native/COM; not portable .NET source','headers/exports only'])
 except (ValueError,struct.error,IndexError) as e:binary.append([rel,len(data),sha,'unknown','unparsed',type(e).__name__]);continue
 for name in names:exports.append([rel,name,'PE export; signature/semantics not verified'])
 for name in needles:
  if name.encode() in data or name.encode('utf-16le') in data:symbols.append([rel,name,'literal ASCII/UTF16 binary symbol; no method body or end-to-end behavior inferred'])
write('device-binaries.csv',['file','size','sha256','pe_machine','kind','coverage'],binary)
write('device-sdk-exports.csv',['file','export','limit'],exports)
write('device-binary-symbols.csv',['file','symbol','limit'],symbols)
summary={'views':len(coverage),'view_evidence':len(rows),'binaries':len(binary),'exports':len(exports),'symbols':len(symbols)}
(OUT/'device-audit-summary.json').write_text(json.dumps(summary,indent=2)+'\n')
print(summary)
# Deterministic metadata identifiers via dnfile (optional install: dnfile==0.17.0).
# No IL method bodies, configuration values, biometrics, keys or network access.
import dnfile
selected=[
 'TWF 4.28/App/bin/MyTime3.dll', 'TWF 4.28/App/bin/TD5.dll',
 'T5V3.9.2/T5V3.UI.DLL','T5V3.9.2/T5V3.ZK.DLL',
 'T5Web [3.5.7]/App/bin/T5V3Web.dll','T5Web [3.5.7]/App/bin/Personnel.dll',
 'Link5 v4.0.4/Device.dll','Link5 v4.0.4/Link5.exe',
 'Link5 v4.0.4/Link5.DB.dll','Link5 v4.0.4/Link5.RecordTransfer.dll',
 'Link5 v4.0.4/service/Comm5Service v4.exe']
metadata=[];meta_coverage=[]
for rel in selected:
 p=ROOT/rel
 if not p.exists():meta_coverage.append([rel,'missing','','no supplied file at exact path']);continue
 try:
  assembly=dnfile.dnPE(str(p));sha=hashlib.sha256(p.read_bytes()).hexdigest()
  if not assembly.net:meta_coverage.append([rel,'native',sha,'no CLR metadata']);continue
  meta_coverage.append([rel,'metadata read',sha,'type/method/field identifiers only; signatures and behavior not inferred'])
  for t in assembly.net.mdtables.TypeDef:
   full=str(t.TypeNamespace)+'.'+str(t.TypeName)
   if not re.search(r'device|iclock|usb|push|comm5|zkem|download|upload',full,re.I):continue
   metadata.append([rel,full,'type',str(t.TypeName),'metadata identifier only'])
   for m in t.MethodList:
    metadata.append([rel,full,'method',str(m.row.Name),'metadata identifier only; body not decompiled'])
   for m in t.FieldList:
    metadata.append([rel,full,'field',str(m.row.Name),'metadata identifier only; value not exported'])
  assembly.close()
 except Exception as e:meta_coverage.append([rel,'unreadable','',type(e).__name__])
write('device-managed-metadata.csv',['file','declaring_type','kind','identifier','limit'],metadata)
write('device-metadata-coverage.csv',['file','status','sha256','limit'],meta_coverage)
# Every extracted lexical UI operation, route and control has a disposition, including helpers.
coverage_rows=[]
for rel,n,kind,name,_ in rows:
 status='outside-scope' if '/Gate/' in rel else 'must-build'
 target='excluded: physical door/gate control' if status=='outside-scope' else ('S04' if '/Device/' in rel else 'S06-S13')
 if kind=='function' and name in ['submitForm','shw','updaterTimer','selectAllDeviceUsers','reverseAllDeviceUsers','selectAll','reverseAll']:
  target='UI helper for S04-S13'
 coverage_rows.append([rel,n,kind,name,status,target,'lexical evidence; comments/duplicate references are not proof of a live endpoint'])
write('device-operation-coverage.csv',['file','line','kind','identifier','status','target','limit'],coverage_rows)
summary.update(metadata_rows=len(metadata),metadata_files=len(meta_coverage),operation_coverage=len(coverage_rows))
(OUT/'device-audit-summary.json').write_text(json.dumps(summary,indent=2)+'\n');print(summary)
