'use strict';
const fs=require('node:fs'),assert=require('node:assert/strict'),{JSDOM}=require('jsdom');const root=__dirname+'/../../src/IO.Web/Ui/';
async function wait(test){for(let i=0;i<250;i++){if(test())return;await new Promise(r=>setTimeout(r,10));}throw Error('SDK attendance DOM contract timeout');}
(async()=>{
 const dom=new JSDOM(fs.readFileSync(root+'DeviceAttendance.html','utf8'),{url:'https://io.example/workspace/device-attendance',runScripts:'outside-only'}),w=dom.window;
 let granted=true,unknown=true,commitUnknown=true,committed=false;const posts=[];const job={id:'ef7edeed-a334-4fa0-9ef4-167d5b98f3cb',deviceId:'5972ddba-92cf-41be-b567-8085e451286d',state:'succeeded',version:'AQIDBAUGBwg=',fromUtc:'2026-11-08T00:00:00Z',untilUtcExclusive:'2026-11-09T00:00:00Z',receivedCount:1,timeZoneId:'UTC'};
 w.confirm=()=>true;w.fetch=async(path,options={})=>{
  let data={};if(path==='/workspace/capabilities')data={permissions:granted?['devices.attendance.read','devices.attendance.extract','devices.attendance.commit']:[]};else if(path==='/auth/csrf')data={token:'csrf-test'};else if(!granted)return{ok:false,status:403};
  else if(options.method==='POST'){assert.equal(options.headers['X-CSRF-TOKEN'],'csrf-test');posts.push({path,body:JSON.parse(options.body)});if(path==='/device-attendance-downloads'&&unknown){unknown=false;throw new w.TypeError('unknown');}if(path.endsWith('/commit-page')&&commitUnknown){commitUnknown=false;committed=true;throw new w.TypeError('unknown');}data={result:job.id};}
  else if(path.includes('/rows?'))data={...job,version:committed?'AgIDBAUGBwg=':job.version,sameTarget:true,nextAfter:1,items:[{sequence:1,raw:{personIdentifier:'<img src=x onerror=alert(1)>',year:2026},status:committed?'imported':'ready',rawEventId:committed?'5ddcd10b-f32d-4645-b862-d3505c87d6d2':null}]};else data={items:[job]};return{ok:true,status:200,json:async()=>data};
 };
 w.eval(fs.readFileSync(root+'device-attendance.js','utf8'));const f=w.document.getElementById('extract-form'),notice=w.document.getElementById('notice'),cf=w.document.getElementById('commit-form');
 await wait(()=>!f.hidden&&!f.querySelector('button').disabled&&w.document.querySelector('#jobs li'));
 Object.assign(f.elements.deviceId,{value:job.deviceId});f.elements.expectedDeviceVersion.value=job.version;f.elements.fromUtc.value=job.fromUtc;f.elements.untilUtcExclusive.value=job.untilUtcExclusive;f.elements.inCode.value='4';f.elements.outCode.value='5';
 f.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>notice.textContent.includes('پاسخ نامعلوم')&&!f.querySelector('button').disabled);const id=f.elements.operationId.value;
 f.elements.outCode.value='6';f.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));assert.equal(posts.length,1);assert.match(notice.textContent,/همان شناسه/);
 f.elements.outCode.value='5';f.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>notice.textContent.includes('SQL ثبت شد')&&!f.querySelector('button').disabled);assert.deepEqual(posts[0],posts[1]);assert.notEqual(f.elements.operationId.value,id);
 w.document.querySelector('#jobs button').click();await wait(()=>!cf.hidden&&!cf.querySelector('button').disabled);assert.equal(w.document.querySelector('#rows img'),null);assert.match(w.document.getElementById('rows').textContent,/<img/);
 cf.elements.confirmed.checked=true;cf.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>notice.textContent.includes('رسیدها و نسخه')&&cf.hidden);assert.equal(posts.at(-1).body.confirmation,'INGEST-SDK-ROWS');assert.equal(posts.at(-1).body.expectedVersion,job.version);
 w.document.getElementById('refresh').click();await wait(()=>w.document.getElementById('rows').textContent.includes('imported')&&!cf.hidden);assert.equal(cf.elements.confirmed.checked,false);assert.equal(posts.filter(x=>x.path.endsWith('/commit-page')).length,1);
 granted=false;w.dispatchEvent(new w.Event('focus'));await wait(()=>f.hidden);assert.equal(w.document.getElementById('rows').textContent,'');assert.equal(w.document.getElementById('jobs').textContent,'');assert.equal(f.elements.deviceId.value,'');dom.window.close();
 console.log('PASS S09 attendance offline DOM: explicit UTC exact unknown retry safe SDK tuples versioned confirmation uncertain commit receipt refresh and revoke clearing');
})().catch(e=>{console.error(e);process.exit(1);});
