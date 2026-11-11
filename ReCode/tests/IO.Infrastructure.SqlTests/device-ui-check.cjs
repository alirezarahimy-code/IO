'use strict';
const fs=require('node:fs'),assert=require('node:assert/strict'),{JSDOM}=require('jsdom');
const root=__dirname+'/../../src/IO.Web/Ui/';
const pause=()=>new Promise(resolve=>setTimeout(resolve,10));
async function wait(test){for(let i=0;i<200;i++){if(test())return;await pause();}throw new Error('Device DOM contract timed out');}
(async()=>{
 const dom=new JSDOM(fs.readFileSync(root+'Devices.html','utf8'),{url:'https://io.example/workspace/devices',runScripts:'outside-only'}),w=dom.window;
 let granted=true,failOnce=true,hasCredential=false;const writes=[];
 const device={id:'ad938a2b-f780-47ab-b8b6-9b7f22536a90',sourceId:'ad938a2b-f780-47ab-b8b6-9b7f22536a90',siteId:'78ee56ac-8ff7-4f10-9080-0031c3642814',companyId:'c15d2a3d-daa8-4d51-b18d-b94577c026d1',number:'DOM-01',name:'<img src=x onerror=alert(1)>',model:'unverified',profile:'offline-file',host:null,port:null,enabled:true,version:'AQIDBAUGBwg=',connectionStatus:'not-tested'};
 w.fetch=async(url,options={})=>{
  let data={};
  if(url==='/workspace/capabilities')data={permissions:granted?['devices.read','devices.create','devices.update','devices.credentials.manage']:[],pages:[]};
  else if(url==='/auth/csrf')data={token:'csrf-test'};
  else if(!granted)return{ok:false,status:403};
  else if(options.method==='POST'){
   const body=JSON.parse(options.body);writes.push({url,body});
   if(url==='/devices'&&failOnce){failOnce=false;throw new w.TypeError('lost acknowledgement');}
   if(url.endsWith('/credential'))hasCredential=true;
   data={result:device.id};
  }else if(url.startsWith('/devices/locations'))data={items:[{...device,id:device.siteId,companyName:'fixture',timeZoneId:'UTC'}]};
  else data={items:[{...device,hasCredential}]};
  return{ok:true,status:200,json:async()=>data};
 };
 w.eval(fs.readFileSync(root+'devices.js','utf8'));
 const form=w.document.getElementById('device-form'),notice=w.document.getElementById('notice'),credential=w.document.getElementById('credential-form');
 await wait(()=>w.document.querySelector('#body button')&&!form.querySelector('button').disabled);
 assert.equal(w.document.querySelector('#body img'),null,'device metadata must render as text');
 assert.equal(credential.hidden,true,'credential form needs explicit device selection');
 form.elements.siteId.value=device.siteId;form.elements.number.value='DOM-NEW';form.elements.name.value='new device';form.elements.model.value='new model';form.elements.profile.value='offline-file';form.elements.profile.dispatchEvent(new w.Event('change'));
 assert.equal(form.elements.host.disabled,true,'non-network profile cannot submit endpoint');
 const operation=form.elements.operationId.value;
 form.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>notice.textContent.includes('ثبت نامعلوم'));
 form.elements.name.value='changed';form.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>notice.textContent.includes('حالت قبلی'));
 assert.equal(writes.length,1,'unknown create must not reuse UUID with changed data');
 form.elements.name.value='new device';form.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>notice.textContent.includes('مشخصات دستگاه ثبت شد')&&!form.querySelector('button').disabled);
 assert.deepEqual(writes[0],writes[1],'unknown device create retry preserves exact request');assert.notEqual(form.elements.operationId.value,operation,'new operation rotates after acknowledgement');
 w.document.querySelector('#body button').click();assert.equal(credential.hidden,false);
 credential.elements.secret.value='synthetic DOM secret';credential.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));
 assert.equal(credential.elements.secret.value,'','credential input clears before awaiting network');
 await wait(()=>notice.textContent.includes('تغییر رمز ثبت شد')&&!form.querySelector('button').disabled);
 assert.equal(credential.hidden,true,'selection resets after credential mutation');
 granted=false;w.dispatchEvent(new w.Event('focus'));await wait(()=>form.hidden);
 assert.equal(w.document.querySelector('#body tr'),null,'revocation clears old inventory');
 assert.equal(w.document.querySelector('#locations li'),null,'revocation clears location details');
 assert.equal(credential.elements.secret.value,'');
 dom.window.close();
 console.log('PASS device offline DOM: safe metadata, exact create retry, profile controls, credential clearing and grant revocation');
})().catch(error=>{console.error(error);process.exit(1);});
