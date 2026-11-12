'use strict';
const fs=require('node:fs'),assert=require('node:assert/strict'),{JSDOM}=require('jsdom');
const root=__dirname+'/../../src/IO.Web/Ui/';
async function wait(test){for(let i=0;i<250;i++){if(test())return;await new Promise(r=>setTimeout(r,10));}throw Error('Upload DOM contract timeout');}
(async()=>{
 const dom=new JSDOM(fs.readFileSync(root+'DeviceUserUploads.html','utf8'),{url:'https://io.example/workspace/device-user-uploads',runScripts:'outside-only'}),w=dom.window;
 let granted=true,unknown=true;const posts=[];
 const row={id:'54f77dd3-1b80-44cb-b034-3844cd2132e8',batchId:'625bc102-d2b9-4878-9b45-dc3cafbf5547',deviceId:'cbba270c-6ce7-42ac-922e-4536a139ef07',employeeId:'85383165-f4c8-4f3f-88ed-f591b3fbac68',personIdentifier:'0001',state:'<img src=x>',version:'AQIDBAUGBwg=',desired:{Name:'<img src=x>'},expected:null};
 w.confirm=()=>true;
 w.fetch=async(path,options={})=>{
  let data={};if(path==='/workspace/capabilities')data={permissions:granted?['devices.users.read','devices.users.upload']:[]};
  else if(path==='/auth/csrf')data={token:'csrf-test'};
  else if(!granted)return{ok:false,status:403};
  else if(options.method==='POST'){
   assert.equal(options.headers['X-CSRF-TOKEN'],'csrf-test');posts.push({path,body:JSON.parse(options.body)});
   if(path==='/device-user-uploads/preview')data={previewHash:'HASH',items:[row]};
   else if(path==='/device-user-uploads'&&unknown){unknown=false;throw new w.TypeError('unknown');}
   else data={result:row.batchId};
  }else data={items:[row]};return{ok:true,status:200,json:async()=>data};
 };
 w.eval(fs.readFileSync(root+'device-user-uploads.js','utf8'));
 const f=w.document.getElementById('upload-form'),notice=w.document.getElementById('notice');
 await wait(()=>!f.hidden&&!w.document.getElementById('preview').disabled&&w.document.querySelector('#jobs li'));
 assert.equal(w.document.querySelector('#jobs img'),null);
 f.elements.items.value=JSON.stringify([{id:row.id,deviceId:row.deviceId,personIdentifier:'0001',card:'123'}]);
 f.elements.confirmed.checked=true;f.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));assert.equal(posts.length,0);
 w.document.getElementById('preview').click();await wait(()=>w.document.getElementById('intent-preview').textContent.includes('img')&&!w.document.getElementById('preview').disabled);
 assert.equal(w.document.querySelector('#intent-preview img'),null);assert.equal(f.elements.confirmed.checked,false);
 f.elements.confirmed.checked=true;f.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>notice.textContent.includes('پاسخ نامعلوم')&&!f.querySelector('button:not([type])').disabled);
 assert.equal(w.document.getElementById('preview').disabled,true);
 const old=f.elements.batchId.value;f.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));await wait(()=>notice.textContent.includes('صف SQL ثبت شد')&&!w.document.getElementById('preview').disabled);
 assert.deepEqual(posts[1],posts[2]);assert.equal(posts[1].body.previewHash,'HASH');assert.notEqual(f.elements.batchId.value,old);
 granted=false;w.dispatchEvent(new w.Event('focus'));await wait(()=>f.hidden);assert.equal(w.document.getElementById('jobs').textContent,'');assert.equal(f.elements.items.value,'');assert.equal(w.document.getElementById('intent-preview').textContent,'');dom.window.close();
 console.log('PASS S08 upload offline DOM: exact server preview explicit confirmation unknown retry CSRF safe personal data and revocation clearing');
})().catch(e=>{console.error(e);process.exit(1);});
