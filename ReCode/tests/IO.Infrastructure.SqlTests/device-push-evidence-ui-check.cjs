'use strict';
const fs=require('node:fs'),assert=require('node:assert/strict'),{JSDOM}=require('jsdom');
const root=__dirname+'/../../src/IO.Web/Ui/';
async function wait(test){for(let i=0;i<300;i++){if(test())return;await new Promise(resolve=>setTimeout(resolve,10));}throw Error('PUSH review DOM timeout');}
(async()=>{
 const dom=new JSDOM(fs.readFileSync(root+'DevicePushEvidence.html','utf8'),{url:'https://io.example/workspace/device-push-evidence?deviceId=abc',runScripts:'outside-only'}),w=dom.window,d=w.document;
 let granted=false,delay=false,release,denyRows=false;const calls=[],downloads=[];
 const id='849f5d11-06d5-4dbe-aea0-a12b83322598';
 const item={id,deviceId:'abc',sourceId:'abc',retainedAtUtc:'2026-11-08T00:00:00Z',payloadHash:'A'.repeat(64),rowCount:55,parsedCount:54,timeZoneId:'UTC'};
 const page={...item,serial:'<img src=x>',stamp:'not-a-nonce',sameTarget:true,nextAfter:50,hasMore:true,deviceIdentityVerified:false,attendanceWritten:false,vendorAckAvailable:false,items:[{lineNumber:1,pin:'<img src=x>',multiplicityRank:1,localTime:'2026-11-08 08:00:00',occurredAtUtc:'2026-11-08T08:00:00Z',punchState:'0',verifyMode:'15',workCode:'000',wireStatus:'parsed-requires-mapping-review',mappingStatus:'exact-mapping-found',quarantineStatus:'unverified-device-identity'},{lineNumber:2,pin:'001',multiplicityRank:2,quarantineStatus:'repeated-row-requires-review',mappingStatus:'exact-mapping-found'}]};
 w.URL.createObjectURL=blob=>{downloads.push(blob);return 'blob:synthetic';};w.URL.revokeObjectURL=()=>{};w.HTMLAnchorElement.prototype.click=function(){downloads.push(this.download);};
 w.fetch=async(path,options={})=>{
  assert.equal(options.method,undefined,'review must not mutate');assert.equal(options.credentials,'same-origin');assert.equal(options.cache,'no-store');calls.push(path);
  let data;
  if(path==='/workspace/capabilities')data={permissions:granted?['devices.push.read']:['devices.push.retain']};
  else if(path.includes('/rows?')){
   if(denyRows)return{ok:false,status:403};
   data=path.endsWith('after=50')?{...page,hasMore:false,nextAfter:55,items:[{lineNumber:51,wireStatus:'layout-mismatch',mappingStatus:'not-reviewable',quarantineStatus:'layout-mismatch'}]}:page;
   if(delay)return new Promise(resolve=>{release=()=>resolve({ok:true,status:200,json:async()=>data});});
  }else if(path.endsWith('/original'))data={id,originalBase64:'MDAxDQo='};
  else data={items:[item],hasMore:path.includes('offset=0')};
  return{ok:true,status:200,json:async()=>data};
 };
 w.eval(fs.readFileSync(root+'device-push-evidence.js','utf8'));
 await wait(()=>d.getElementById('notice').textContent.includes('حق مستقل'));
 assert.equal(d.getElementById('review').hidden,true);assert.equal(calls.filter(x=>x.startsWith('/device-push-evidence')).length,0);
 granted=true;w.dispatchEvent(new w.Event('focus'));await wait(()=>d.querySelector('#evidence button')&&!d.querySelector('#evidence button').disabled);
 const f=d.getElementById('filter-form');f.elements.deviceId.value='abc';f.dispatchEvent(new w.Event('submit',{cancelable:true}));await wait(()=>calls.some(x=>x.includes('deviceId=abc'))&&!d.querySelector('#evidence button').disabled);
 d.querySelector('#evidence button').click();await wait(()=>d.querySelectorAll('#rows tr').length===2&&!d.getElementById('original').disabled);
 assert.equal(d.querySelector('#rows img'),null);assert.equal(d.querySelector('#snapshot img'),null);assert.match(d.getElementById('rows').textContent,/هویت دستگاه تأیید نشده/);assert.match(d.getElementById('rows').textContent,/سطر تکراری/);assert.match(d.getElementById('rows').textContent,/نه مجوز ثبت/);
 assert.equal(d.querySelector('[id*=commit]'),null);assert.equal(d.querySelector('[id*=retain]'),null);
 d.getElementById('next-rows').click();await wait(()=>d.getElementById('rows').textContent.includes('51 /')&&!d.getElementById('original').disabled);assert.equal(d.getElementById('next-rows').disabled,true);
 d.getElementById('previous-rows').click();await wait(()=>d.querySelectorAll('#rows tr').length===2&&!d.getElementById('original').disabled);
 d.getElementById('original').click();await wait(()=>downloads.length===2&&!d.getElementById('original').disabled);assert.match(downloads[1],/^unverified-push-/);assert.equal(downloads[0].size,5);
 d.getElementById('next').click();await wait(()=>calls.some(x=>x.includes('offset=50'))&&!d.getElementById('previous').disabled);assert.equal(d.getElementById('rows').textContent,'');assert.equal(d.getElementById('detail').hidden,true);
 delay=true;d.querySelector('#evidence button').click();await wait(()=>release);
 granted=false;w.dispatchEvent(new w.Event('focus'));await wait(()=>d.getElementById('review').hidden&&d.getElementById('notice').textContent.includes('حق مستقل'));
 release();await new Promise(resolve=>setTimeout(resolve,30));assert.equal(d.getElementById('rows').textContent,'');assert.equal(d.getElementById('snapshot').textContent,'');assert.equal(d.getElementById('evidence').textContent,'');assert.equal(f.elements.deviceId.value,'');
 delay=false;granted=true;w.dispatchEvent(new w.Event('focus'));await wait(()=>d.querySelector('#evidence button')&&!d.querySelector('#evidence button').disabled);
 denyRows=true;d.querySelector('#evidence button').click();await wait(()=>d.getElementById('review').hidden);assert.equal(d.getElementById('evidence').textContent,'');
 dom.window.close();console.log('PASS R08 read-only PUSH review DOM: exact permission, safe text, scoped filter, paging, original download, no writes and late-response/revoke clearing');
})().catch(error=>{console.error(error);process.exit(1);});
