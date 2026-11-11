'use strict';
const fs=require('node:fs'),assert=require('node:assert/strict'),{JSDOM}=require('jsdom');
const root=__dirname+'/../../src/IO.Web/Ui/';
async function wait(test){for(let i=0;i<250;i++){if(test())return;await new Promise(r=>setTimeout(r,10));}throw new Error('Group DOM contract timed out');}
(async()=>{
 const dom=new JSDOM(fs.readFileSync(root+'DeviceGroups.html','utf8'),{url:'https://io.example/workspace/device-groups',runScripts:'outside-only'}),w=dom.window;
 const group={id:'62a98682-fd31-4d47-8212-a170f3e16570',companyId:'914a60ac-8521-4e85-961b-ddecc27ba7bf',parentId:null,name:'<img src=x onerror=alert(1)>',version:'AQIDBAUGBwg=',deviceCount:0};
 let granted=true,createFailure=true,deleteFailure=true,deleted=false;const writes=[];
 w.fetch=async(url,options={})=>{let data={};
  if(url==='/workspace/capabilities')data={permissions:granted?['devices.groups.read','devices.groups.manage']:[]};
  else if(url==='/auth/csrf')data={token:'csrf-test'};
  else if(!granted)return{ok:false,status:403};
  else if(options.method==='POST'){
   writes.push({url,body:JSON.parse(options.body)});
   if(url==='/device-groups'&&createFailure){createFailure=false;throw new w.TypeError('unknown create');}
   if(url.endsWith('/delete')){if(deleteFailure){deleteFailure=false;throw new w.TypeError('unknown deletion');}deleted=true;}
   data={result:group.id};
  }else data={parent:null,items:deleted?[]:[group]};
  return{ok:true,status:200,json:async()=>data};
 };
 w.eval(fs.readFileSync(root+'device-groups.js','utf8'));
 const form=w.document.getElementById('group-form'),deletion=w.document.getElementById('delete-form'),notice=w.document.getElementById('notice');
 await wait(()=>w.document.querySelector('#body [data-action=select]')&&!form.querySelector('button').disabled);
 assert.equal(w.document.querySelector('#body img'),null,'reference names must be text');
 form.elements.companyId.value=group.companyId;form.elements.name.value='New group';
 const id=form.elements.operationId.value;form.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>notice.textContent.includes('قطع شد'));
 form.elements.name.value='Changed';form.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>notice.textContent.includes('حالت قبلی'));assert.equal(writes.length,1);
 form.elements.name.value='New group';form.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>notice.textContent.includes('گروه ذخیره شد')&&!form.querySelector('button').disabled);
 assert.deepEqual(writes[0],writes[1]);assert.notEqual(form.elements.operationId.value,id);
 w.document.querySelector('#body [data-action=select]').click();assert.equal(form.elements.companyId.readOnly,true);
 deletion.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));assert.equal(writes.length,2,'delete requires explicit confirmation');
 deletion.elements.confirmed.checked=true;deletion.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>notice.textContent.includes('قطع شد'));
 deletion.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>notice.textContent.includes('گروه خالی حذف شد')&&!form.querySelector('button').disabled);
 assert.deepEqual(writes[2],writes[3],'unknown deletion retries exact group and rowversion');assert.equal(w.document.querySelector('#body tr'),null);
 granted=false;w.dispatchEvent(new w.Event('focus'));await wait(()=>form.hidden);assert.equal(deletion.hidden,true);assert.equal(w.document.getElementById('delete-target').textContent,'');
 dom.window.close();console.log('PASS reference group offline DOM: safe names exact create/delete retries confirmation immutable company and revoke clearing');
})().catch(e=>{console.error(e);process.exit(1);});
