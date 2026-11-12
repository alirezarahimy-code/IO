'use strict';
const fs=require('node:fs'),assert=require('node:assert/strict'),{JSDOM}=require('jsdom');
const root=__dirname+'/../../src/IO.Web/Ui/';
async function wait(test){for(let i=0;i<250;i++){if(test())return;await new Promise(r=>setTimeout(r,10));}throw Error('S06 DOM contract timeout');}
(async()=>{
 const dom=new JSDOM(fs.readFileSync(root+'DeviceInformation.html','utf8'),{url:'https://io.example/workspace/device-information',runScripts:'outside-only'}),w=dom.window;
 let granted=true,unknown=true,serverFailure=true;const posts=[];let row={id:'80e21cae-1680-4450-a4b2-692ce87e8284',deviceId:'b5d05212-7b3b-4779-83ca-6707ef1e00a3',kind:'information.read',state:'succeeded',requestedAtUtc:'2026-11-08',timeZoneId:'UTC',version:'AQIDBAUGBwg=',information:{serial:'<img src=x onerror=alert(1)>',users:0,userCapacity:null}};
 w.confirm=()=>true;
 w.fetch=async(path,options={})=>{
  let data={};if(path==='/workspace/capabilities')data={permissions:granted?['devices.info.read','devices.clock.set']:[]};
  else if(path==='/auth/csrf')data={token:'csrf-test'};
  else if(!granted)return{ok:false,status:403};
  else if(options.method==='POST'){assert.equal(options.headers['X-CSRF-TOKEN'],'csrf-test');posts.push({path,body:JSON.parse(options.body)});if(path==='/device-operations'&&unknown){unknown=false;throw new w.TypeError('unknown');}if(path==='/device-operations'&&serverFailure){serverFailure=false;return{ok:false,status:503};}if(path.endsWith('/cancel'))row={...row,state:'cancelled'};if(path.endsWith('/resolve-clock'))row={...row,resolvedAtUtc:'2026-11-08',resolutionInfoId:row.id};data={result:row.id};}
  else data={items:[row]};return{ok:true,status:200,json:async()=>data};
 };
 w.eval(fs.readFileSync(root+'device-information.js','utf8'));
 const f=w.document.getElementById('operation-form'),status=w.document.getElementById('notice');
 await wait(()=>!f.hidden&&!f.querySelector('button').disabled&&w.document.querySelector('#items li'));
 assert.equal(w.document.querySelector('#items img'),null);assert.match(w.document.getElementById('items').textContent,/گزارش نشده/);assert.match(w.document.getElementById('items').textContent,/0/);
 f.elements.deviceId.value=row.deviceId;f.elements.expectedDeviceVersion.value=row.version;
 const id=f.elements.operationId.value;f.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>status.textContent.includes('قطع شد')&&!f.querySelector('button').disabled);
 f.elements.timeoutSeconds.value='20';f.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));assert.equal(posts.length,1);assert.match(status.textContent,/همان شناسه/);
 f.elements.timeoutSeconds.value='15';f.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>posts.length===2&&status.textContent.includes('قطع شد')&&!f.querySelector('button').disabled);
 f.elements.timeoutSeconds.value='20';f.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));assert.equal(posts.length,2);assert.match(status.textContent,/همان شناسه/);
 f.elements.timeoutSeconds.value='15';f.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>status.textContent.includes('ثبت شد')&&!f.querySelector('button').disabled);assert.deepEqual(posts[0],posts[1]);assert.deepEqual(posts[1],posts[2]);assert.notEqual(f.elements.operationId.value,id);
 f.elements.kind.value='clock.set';f.elements.kind.dispatchEvent(new w.Event('change'));f.elements.targetUtc.value='2026-11-08T10:00';
 const before=posts.length;f.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));assert.equal(posts.length,before);
 f.elements.confirmed.checked=true;f.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>posts.length===before+1&&!f.querySelector('button').disabled);assert.equal(posts.at(-1).body.targetUtc,'2026-11-08T10:00:00Z');assert.equal(posts.at(-1).body.confirmation,'SET-CLOCK');
 row={...row,state:'pending',information:null};w.document.getElementById('refresh').click();await wait(()=>w.document.querySelector('#items button'));
 w.document.querySelector('#items button').click();await wait(()=>w.document.getElementById('items').textContent.includes('لغو شد'));assert.equal(posts.at(-1).body.expectedVersion,row.version);
 row={...row,state:'unknown',kind:'clock.set'};w.document.getElementById('refresh').click();await wait(()=>w.document.querySelector('#items button'));
 w.document.querySelector('#items button').click();const r=w.document.getElementById('resolve-form');assert.equal(r.elements.operationId.value,row.id);r.elements.informationId.value=row.id;r.elements.reviewed.checked=true;r.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>w.document.getElementById('items').textContent.includes('بازبینی شده'));assert.equal(posts.at(-1).body.confirmation,'REVIEWED-CLOCK');
 granted=false;w.dispatchEvent(new w.Event('focus'));await wait(()=>f.hidden);assert.equal(w.document.getElementById('items').textContent,'');assert.equal(f.elements.deviceId.value,'');assert.equal(r.elements.informationId.value,'');dom.window.close();
 console.log('PASS S06 offline DOM: safe nullable snapshots exact retry UTC explicit clock confirmation versioned cancellation unknown review and revoke clearing');
})().catch(e=>{console.error(e);process.exit(1);});
