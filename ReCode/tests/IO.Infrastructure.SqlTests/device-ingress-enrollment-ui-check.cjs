'use strict';
const fs=require('node:fs'),assert=require('node:assert/strict'),{JSDOM}=require('jsdom');
const root=__dirname+'/../../src/IO.Web/Ui/';
async function wait(test){for(let i=0;i<300;i++){if(test())return;await new Promise(r=>setTimeout(r,10));}throw Error('Gateway/enrollment DOM timeout');}
(async()=>{
 const dom=new JSDOM(fs.readFileSync(root+'DeviceIngressEnrollment.html','utf8'),{url:'https://io.example/workspace/device-ingress-enrollment',runScripts:'outside-only'}),w=dom.window,d=w.document;
 let rights=[],deny=false,hold=false,release;const posts=[],gets=[];
 const id='11111111-1111-4111-8111-111111111111',version='AQIDBAUGBwg=';
 w.fetch=async(path,options={})=>{
  assert.equal(options.credentials,'same-origin');assert.equal(options.cache,'no-store');
  if(path==='/workspace/capabilities')return{ok:true,status:200,json:async()=>({permissions:rights})};
  if(deny)return{ok:false,status:403};
  if(path==='/auth/csrf')return{ok:true,status:200,json:async()=>({token:'synthetic-csrf'})};
  if(options.method==='POST'){assert.equal(options.headers['X-CSRF-TOKEN'],'synthetic-csrf');posts.push({path,body:JSON.parse(options.body)});return{ok:true,status:200,json:async()=>({result:id})};}
  gets.push(path);const data=path.includes('gateway-bindings')?{result:[{id,deviceId:id,keyVersion:'<img src=x>',version}]}:{result:{items:[{id,deviceId:id,personIdentifier:'<script>bad()</script>',state:'running',version}]}};
  if(hold)return new Promise(resolve=>{release=()=>resolve({ok:true,status:200,json:async()=>data});});
  return{ok:true,status:200,json:async()=>data};
 };
 w.eval(fs.readFileSync(root+'device-ingress-enrollment.js','utf8'));
 await wait(()=>!d.getElementById('refresh').disabled);assert.equal(gets.length,0);assert.equal(d.getElementById('gateway').hidden,true);assert.equal(d.getElementById('enrollment').hidden,true);
 rights=['devices.credentials.manage'];w.dispatchEvent(new w.Event('focus'));await wait(()=>d.querySelector('#bindings li')&&!d.getElementById('refresh').disabled);assert.equal(d.getElementById('binding-form').hidden,true);assert.equal(d.querySelector('#bindings img'),null);
 rights=['devices.credentials.manage','devices.push.retain','devices.enrollment.request','devices.enrollment.read'];w.dispatchEvent(new w.Event('focus'));await wait(()=>d.querySelector('#jobs button')&&!d.getElementById('refresh').disabled);assert.equal(d.querySelector('#jobs script'),null);
 const binding=d.getElementById('binding-form');for(const name of ['id','gatewayId','deviceId','serviceUserId'])binding.elements[name].value=id;binding.elements.deviceVersion.value=version;binding.elements.serial.value='SYNTHETIC';binding.elements.keyVersion.value='key-1';d.getElementById('new-key').click();assert.equal(w.atob(binding.elements.keyBase64.value).length,32);
 const key=binding.elements.keyBase64.value;binding.dispatchEvent(new w.Event('submit',{cancelable:true}));await wait(()=>posts.length===1&&!d.getElementById('refresh').disabled);assert.equal(posts[0].body.keyBase64,key);assert.equal(binding.elements.keyBase64.value,'');assert.equal(d.body.textContent.includes(key),false);
 const enroll=d.getElementById('enroll-form');for(const name of ['id','deviceId','employeeId'])enroll.elements[name].value=id;enroll.elements.deviceVersion.value=version;enroll.elements.personIdentifier.value='001';for(const name of ['serial','product','firmware','modelEvidence','authorizationReference','retentionAndDeletionPolicyReference'])enroll.elements[name].value='SYNTHETIC';for(const [name,value]of Object.entries({fingerIndex:1,successActionCode:0,emptySlotErrorCode:-404,eventMask:65535,timeoutSeconds:60}))enroll.elements[name].value=value;enroll.elements.confirmation.value='ENROLL-NEW-FINGER-NO-OVERWRITE';enroll.dispatchEvent(new w.Event('submit',{cancelable:true}));await wait(()=>posts.length===2&&!d.getElementById('refresh').disabled);assert.equal(posts[1].body.personIdentifier,'001');assert.equal(posts[1].body.approval.eventMask,65535);assert.equal(enroll.elements.personIdentifier.value,'');assert.equal(enroll.elements.confirmation.value,'');
 d.querySelector('#jobs button').click();await wait(()=>posts.length===3&&!d.getElementById('refresh').disabled);assert.match(posts[2].path,/\/cancel$/);assert.equal(posts[2].body.version,version);
 hold=true;w.dispatchEvent(new w.Event('focus'));await wait(()=>release);rights=[];hold=false;w.dispatchEvent(new w.Event('focus'));await wait(()=>d.getElementById('gateway').hidden&&!d.getElementById('refresh').disabled);release();await new Promise(r=>setTimeout(r,30));assert.equal(d.getElementById('bindings').textContent,'');assert.equal(d.getElementById('jobs').textContent,'');
 rights=['devices.enrollment.read'];deny=true;w.dispatchEvent(new w.Event('focus'));await wait(()=>d.getElementById('notice').textContent.includes('لغو'));assert.equal(d.getElementById('jobs').textContent,'');assert.equal(d.getElementById('enrollment').hidden,true);
 dom.window.close();console.log('PASS operational gateway/enrollment DOM: independent rights, CSRF, random key clearing, exact identity, explicit approval, cancellation version, safe text and late-response/revoke clearing');
})().catch(e=>{console.error(e);process.exit(1);});
