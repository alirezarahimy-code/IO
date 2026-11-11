'use strict';
// Fast offline DOM contract check. npm install jsdom@26.1.0 in an isolated prefix.
// This deliberately mocks HTTP and does not replace real SQL/HTTPS/Playwright CI.
const {JSDOM}=require('jsdom'),fs=require('node:fs'),assert=require('node:assert/strict');
const root=__dirname+'/../../src/IO.Web/Ui/';
const decision='77777777-7777-4777-8777-777777777777',requestId='88888888-8888-4888-8888-888888888888';
async function wait(fn){for(let i=0;i<100;i++){if(fn())return;await new Promise(r=>setTimeout(r,2));}throw new Error('DOM condition timed out');}
async function create(own=false){
 const dom=new JSDOM(fs.readFileSync(root+'Reversals.html','utf8'),{url:'https://io.example/workspace/reversals'+(own?'?kind=correction&own='+requestId:''),runScripts:'outside-only'});
 const w=dom.window;let reversed=false,failOnce=true,denied=false;const writes=[];
 w.fetch=async (url,options={})=>{
  let data={};
  if(denied)return{ok:false,status:403};
  if(url==='/workspace/capabilities')data={permissions:['correction.reverse']};
  else if(url==='/auth/csrf')data={token:'test-token'};
  else if(options.method==='POST'){
   writes.push(JSON.parse(options.body));
   if(failOnce){failOnce=false;throw new w.TypeError('simulated network loss');}
   reversed=true;data={result:writes.at(-1).operationId};
  }else data={items:[{id:decision,decisionId:decision,requestId,employeeId:'test employee',workDate:'2027-01-01',sequence:1,decision:1,atUtc:'2027-01-01T00:00:00Z',reason:'<img src=x onerror=alert(1)>',canReverse:!reversed,reversalId:reversed?'test-reversal':null,reversalReason:reversed?'review reason':null}]};
  return{ok:true,status:200,json:async()=>data};
 };
 w.eval(fs.readFileSync(root+'reversals.js','utf8'));
 await wait(()=>w.document.querySelector('#body tr'));
 return{dom,w,writes,deny:()=>{denied=true;}};
}
async function accessContract(){
 const dom=new JSDOM('<body><a id="history" href="/workspace/reversals?kind=leave&own='+requestId+'#history">history</a><a id="denied" href="/workspace/reversals-other?kind=leave">denied</a></body>',{url:'https://io.example/workspace/my-leave',runScripts:'outside-only'});
 const w=dom.window;let revoked=false;
 w.fetch=async()=>({ok:true,json:async()=>({permissions:[],pages:revoked?[]:[{route:'/workspace/reversals'}]})});
 w.eval(fs.readFileSync(root+'access.js','utf8'));
 await wait(()=>w.document.getElementById('denied').hidden);
 assert.equal(w.document.getElementById('history').hidden,false,'authorized deep link uses pathname, not query/hash');
 revoked=true;w.dispatchEvent(new w.Event('focus'));await wait(()=>w.document.getElementById('history').hidden);
 dom.window.close();
}
async function leaveSubmissionContract(){
 const dom=new JSDOM(fs.readFileSync(root+'MyLeave.html','utf8'),{url:'https://io.example/workspace/my-leave',runScripts:'outside-only'});
 const w=dom.window;const writes=[];let failOnce=true;
 w.fetch=async(url,options={})=>{
  let data={};
  if(url.startsWith('/leave/own-types'))data={items:[{id:decision,name:'test leave',symbol:'TEST'}]};
  else if(url==='/auth/csrf')data={token:'test-token'};
  else if(options.method==='POST'){writes.push(JSON.parse(options.body));if(failOnce){failOnce=false;throw new w.TypeError('lost response');}data={result:requestId};}
  else data={items:[]};
  return{ok:true,status:200,json:async()=>data};
 };
 w.eval(fs.readFileSync(root+'my-leave.js','utf8'));
 const form=w.document.getElementById('request-form'),notice=w.document.getElementById('notice');
 await wait(()=>form.elements.leaveTypeId.options.length===2&&!form.querySelector('button').disabled);
 form.elements.leaveTypeId.value=decision;form.elements.workDate.value='2027-01-01';form.elements.reason.value='retain payload';
 assert.equal(form.elements.fromTime.disabled,true,'daily submission does not enable hourly inputs');
 form.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));
 await wait(()=>notice.textContent.includes('ثبت نامعلوم'));
 const operation=form.elements.operationId.value;assert.equal(writes.length,1);assert.equal(writes[0].fromAtUtc,null);
 form.elements.reason.value='different payload';form.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));
 await wait(()=>notice.textContent.includes('به حالت قبلی'));assert.equal(writes.length,1,'uncertain payload cannot change under same operation');
 form.elements.reason.value='retain payload';form.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));
 await wait(()=>notice.textContent.includes('درخواست مرخصی ثبت شد'));
 assert.deepEqual(writes[0],writes[1],'leave retry retains payload and operation');assert.notEqual(form.elements.operationId.value,operation,'new operation starts only after acknowledged success');
 form.elements.kind.value='0:1';form.elements.kind.dispatchEvent(new w.Event('change'));assert.equal(form.elements.fromTime.disabled,false);
 form.elements.fromTime.value='08:00';form.elements.untilTime.value='09:00';form.elements.reason.value='hourly payload';notice.textContent='';form.dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));
 await wait(()=>notice.textContent.includes('درخواست مرخصی ثبت شد'));
 assert.equal(writes[2].fromAtUtc,'2027-01-01T08:00:00Z');assert.equal(writes[2].untilAtUtcExclusive,'2027-01-01T09:00:00Z');
 dom.window.close();
}
(async()=>{
 const {dom,w,writes,deny}=await create();const $=id=>w.document.getElementById(id);
 assert.equal(w.document.querySelector('#body img'),null,'history evidence is text, not HTML');
 w.document.querySelector('#body button').click();$('reverse-form').elements.reason.value='review reason';
 $('reverse-form').dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));
 await wait(()=>$('notice').textContent.includes('ارتباط قطع شد'));
 assert.equal(writes.length,1);assert.equal(writes[0].expectedDecisionId,decision);assert.ok(writes[0].operationId);
 $('reverse-form').dispatchEvent(new w.Event('submit',{bubbles:true,cancelable:true}));
 await wait(()=>$('notice').textContent.includes('بازگشت ثبت شد'));
 assert.deepEqual(writes[0],writes[1],'uncertain retry retains exact operation and payload');
 assert.equal(w.document.querySelector('#body button'),null,'committed reversal cannot be offered twice');
 deny();$('refresh').click();await wait(()=>$('notice').textContent.includes('مجوز'));assert.equal(w.document.querySelector('#body tr'),null,'denial clears prior evidence');
 dom.window.close();
 const own=await create(true);assert.equal(own.w.document.querySelector('#body button'),null,'self history never offers reversal');assert.equal(own.w.document.querySelector('#kind').disabled,true);own.dom.window.close();
 await accessContract();await leaveSubmissionContract();
 console.log('PASS reversal offline DOM: safe evidence, expected decision, stable retry, refresh revocation and own read-only history, deep-link grant/revoke and stable daily/hourly leave submission');
})().catch(e=>{console.error(e);process.exitCode=1;});
