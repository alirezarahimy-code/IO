'use strict';
const fs=require('node:fs'),assert=require('node:assert/strict'),{JSDOM}=require('jsdom');
const root=__dirname+'/../../src/IO.Web/Ui/';
async function wait(test){for(let i=0;i<250;i++){if(test())return;await new Promise(r=>setTimeout(r,10));}throw Error('Device scope DOM contract timeout');}
(async()=>{
 const dom=new JSDOM(fs.readFileSync(root+'Devices.html','utf8'),{url:'https://io.example/workspace/devices',runScripts:'outside-only'}),w=dom.window;
 let allowed=true,restricted=false,version=null,unknown=true;const posts=[],rows=[];
 w.confirm=()=>true;w.fetch=async(path,options={})=>{
  let data={};if(path==='/workspace/capabilities')data={permissions:allowed?['devices.access.manage']:[],deviceAccessRestricted:false};
  else if(path==='/auth/csrf')data={token:'scope-csrf'};
  else if(!allowed)return{ok:false,status:403};
  else if(options.method==='POST'){
   assert.equal(options.headers['X-CSRF-TOKEN'],'scope-csrf');const body=JSON.parse(options.body);posts.push({path,body});
   if(path.endsWith('/policy')){assert.equal(body.confirmation,'RESTRICT-DEVICE-OPERATOR');assert.equal(body.restricted,true);assert.equal(body.expectedVersion,null);restricted=true;version='AQIDBAUGBwg=';}
   else if(path.endsWith('/revoke')){assert.equal(body.expectedVersion,'AgIDBAUGBwg=');rows[0].revokedAtUtc='2026-11-08T12:00:00Z';}
   else{if(rows.length===0)rows.push({...body,version:'AgIDBAUGBwg=',revokedAtUtc:null});if(unknown){unknown=false;throw new w.TypeError('unknown');}}
   data={result:'retained'};
  }else data={restricted,version,items:rows};
  return{ok:true,status:200,json:async()=>data};
 };
 w.eval(fs.readFileSync(root+'device-access.js','utf8'));
 const section=w.document.getElementById('device-access'),operator=w.document.getElementById('scope-operator'),mode=w.document.getElementById('scope-mode'),grant=w.document.getElementById('scope-grant'),notice=w.document.getElementById('scope-notice');
 await wait(()=>!section.hidden&&!operator.querySelector('button').disabled);
 operator.elements.userId.value='b184266d-b253-4bd5-a025-9136e9570a52';operator.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));
 await wait(()=>w.document.getElementById('scope-policy').textContent.includes('b184266d')&&!mode.querySelector('button').disabled);
 mode.elements.restricted.value='true';mode.elements.confirmed.checked=true;mode.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));
 await wait(()=>notice.textContent.includes('دامنه ثبت شد')&&!grant.querySelector('button').disabled);assert.equal(mode.elements.confirmed.checked,false);
 grant.elements.deviceId.value='<img src=x onerror=alert(1)>';const id=grant.elements.id.value;grant.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));
 await wait(()=>notice.textContent.includes('نامعلوم')&&!grant.querySelector('button').disabled);
 grant.elements.deviceId.value='changed';grant.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));assert.equal(posts.length,2);
 grant.elements.deviceId.value='<img src=x onerror=alert(1)>';grant.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));
 await wait(()=>notice.textContent.includes('انتساب دستگاه ثبت شد')&&!grant.querySelector('button').disabled);assert.deepEqual(posts[1],posts[2]);assert.notEqual(grant.elements.id.value,id);assert.equal(w.document.querySelector('#scope-grants img'),null);
 w.document.querySelector('#scope-grants button').click();await wait(()=>notice.textContent.includes('دسترسی لغو شد')&&!grant.querySelector('button').disabled);assert.equal(w.document.querySelector('#scope-grants button'),null);
 allowed=false;w.dispatchEvent(new w.Event('focus'));await wait(()=>section.hidden);assert.equal(w.document.getElementById('scope-grants').textContent,'');assert.equal(operator.elements.userId.value,'');assert.equal(w.document.getElementById('scope-policy').textContent,'');
 dom.window.close();console.log('PASS device scope offline DOM: CSRF explicit policy optimistic version exact unknown grant retry safe history versioned revoke and permission-loss clearing');
})().catch(e=>{console.error(e);process.exit(1);});
