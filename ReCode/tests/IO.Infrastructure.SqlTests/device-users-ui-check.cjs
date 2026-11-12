'use strict';
const fs=require('node:fs'),assert=require('node:assert/strict'),{JSDOM}=require('jsdom');
const root=__dirname+'/../../src/IO.Web/Ui/';
async function wait(test){for(let i=0;i<250;i++){if(test())return;await new Promise(r=>setTimeout(r,10));}throw Error('Connection DOM contract timeout');}
(async()=>{
 const dom=new JSDOM(fs.readFileSync(root+'DeviceUsers.html','utf8'),{url:'https://io.example/workspace/device-users',runScripts:'outside-only'}),w=dom.window;
 let granted=true,unknown=true;const posts=[];let row={id:'80e21cae-1680-4450-a4b2-692ce87e8284',deviceId:'b5d05212-7b3b-4779-83ca-6707ef1e00a3',state:'<img src=x onerror=alert(1)>',timeoutSeconds:1,requestedAtUtc:'2026-11-08',version:'AQIDBAUGBwg='};
 w.confirm=()=>true;
 w.fetch=async(path,options={})=>{
  let data={};if(path==='/workspace/capabilities')data={permissions:granted?['devices.users.read','devices.users.download']:[]};
  else if(path==='/auth/csrf')data={token:'csrf-test'};
  else if(!granted)return{ok:false,status:403};
  else if(options.method==='POST'){assert.equal(options.headers['X-CSRF-TOKEN'],'csrf-test');posts.push({path,body:JSON.parse(options.body)});if(path==='/device-user-downloads'&&unknown){unknown=false;throw new w.TypeError('unknown');}if(path.endsWith('/cancel'))row={...row,state:'cancelled'};data={result:row.id};}
  else data={items:[row]};return{ok:true,status:200,json:async()=>data};
 };
 w.eval(fs.readFileSync(root+'device-users.js','utf8'));
 const f=w.document.getElementById('download-form'),status=w.document.getElementById('notice');
 await wait(()=>!f.hidden&&!f.querySelector('button').disabled&&w.document.querySelector('#jobs li'));
 assert.equal(w.document.querySelector('#jobs img'),null);
 f.elements.deviceId.value=row.deviceId;f.elements.expectedDeviceVersion.value=row.version;
 const id=f.elements.operationId.value;f.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>status.textContent.includes('قطع شد')&&!f.querySelector('button').disabled);
 f.elements.timeoutSeconds.value='20';f.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));assert.equal(posts.length,1);assert.match(status.textContent,/همان شناسه/);
 f.elements.timeoutSeconds.value='60';f.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>status.textContent.includes('ثبت شد')&&!f.querySelector('button').disabled);assert.deepEqual(posts[0],posts[1]);assert.notEqual(f.elements.operationId.value,id);
 row={...row,state:'pending'};w.document.getElementById('refresh').click();await wait(()=>[...w.document.querySelectorAll('#jobs button')].some(b=>b.textContent.includes('لغو')));
 [...w.document.querySelectorAll('#jobs button')].find(b=>b.textContent.includes('لغو')).click();await wait(()=>w.document.getElementById('jobs').textContent.includes('پیش از اجرا لغو شد'));
 assert.equal(posts.at(-1).body.expectedVersion,row.version);
 granted=false;w.dispatchEvent(new w.Event('focus'));await wait(()=>f.hidden);assert.equal(w.document.getElementById('jobs').textContent,'');assert.equal(f.elements.deviceId.value,'');dom.window.close();
 console.log('PASS S07 users offline DOM: CSRF exact unknown retry safe status versioned cancel and revoke clearing');
})().catch(e=>{console.error(e);process.exit(1);});
