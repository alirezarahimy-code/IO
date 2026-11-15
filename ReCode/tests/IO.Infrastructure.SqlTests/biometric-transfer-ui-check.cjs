'use strict';
const fs=require('node:fs'),assert=require('node:assert/strict'),{JSDOM}=require('jsdom');
const root=__dirname+'/../../src/IO.Web/Ui/';
async function wait(test){for(let i=0;i<300;i++){if(test())return;await new Promise(r=>setTimeout(r,10));}throw Error('Biometric DOM timeout');}
(async()=>{
 const dom=new JSDOM(fs.readFileSync(root+'DeviceIngressEnrollment.html','utf8'),{url:'https://io.example/workspace/device-ingress-enrollment',runScripts:'outside-only'}),w=dom.window,d=w.document;
 const id='11111111-1111-4111-8111-111111111111',version='AQIDBAUGBwg=';let rights=[],deny=false,hold=false,release;const posts=[];
 w.fetch=async(path,options={})=>{
  assert.equal(options.credentials,'same-origin');assert.equal(options.cache,'no-store');
  if(path==='/workspace/capabilities')return{ok:true,status:200,json:async()=>({permissions:rights})};
  if(deny)return{ok:false,status:403};
  if(path==='/auth/csrf')return{ok:true,status:200,json:async()=>({token:'SYNTHETIC-CSRF'})};
  if(options.method==='POST'){assert.equal(options.headers['X-CSRF-TOKEN'],'SYNTHETIC-CSRF');posts.push({path,body:JSON.parse(options.body)});return{ok:true,status:200,json:async()=>({result:path.endsWith('/preview')?{previewHash:'A'.repeat(64),sourcePerson:'001',kind:'fingerprint',model:'<img src=x>'}:id})};}
  const result={items:[{id,sourceDeviceId:id,targetDeviceId:id,sourcePerson:'<script>bad()</script>',targetPerson:'0002',state:'running',version}]};
  if(hold)return new Promise(resolve=>{release=()=>resolve({ok:true,status:200,json:async()=>({result})});});
  return{ok:true,status:200,json:async()=>({result})};
 };
 w.eval(fs.readFileSync(root+'device-ingress-enrollment.js','utf8'));await wait(()=>!d.getElementById('refresh').disabled);assert.equal(d.getElementById('biometric').hidden,true);
 rights=['devices.biometrics.jobs.read'];w.dispatchEvent(new w.Event('focus'));await wait(()=>d.querySelector('#biometric-jobs li')&&!d.getElementById('refresh').disabled);assert.equal(d.getElementById('biometric-form').hidden,true);assert.equal(d.querySelector('#biometric-jobs script'),null);assert.equal(d.querySelector('#biometric-jobs button'),null);
 rights.push('devices.biometrics.transfer','devices.biometrics.source.read');w.dispatchEvent(new w.Event('focus'));await wait(()=>!d.getElementById('biometric-form').hidden&&!d.getElementById('refresh').disabled);
 const form=d.getElementById('biometric-form');for(const name of ['id','sourceDeviceId','targetDeviceId','employeeId'])form.elements[name].value=id;for(const name of ['sourceDeviceVersion','targetDeviceVersion'])form.elements[name].value=version;form.elements.sourcePerson.value='001';form.elements.targetPerson.value='0002';form.elements.approval.value=JSON.stringify({kind:'fingerprint',slot:1,allowTransfer:true});form.dispatchEvent(new w.Event('submit',{cancelable:true}));await wait(()=>posts.length===1&&!d.getElementById('refresh').disabled);assert.equal(posts[0].body.sourcePerson,'001');assert.equal(d.querySelector('#biometric-preview img'),null);assert.equal(d.getElementById('biometric-request').disabled,false);
 form.elements.targetPerson.value='0003';form.dispatchEvent(new w.Event('input',{bubbles:true}));assert.equal(d.getElementById('biometric-request').disabled,true);form.dispatchEvent(new w.Event('submit',{cancelable:true}));await wait(()=>posts.length===2&&!d.getElementById('refresh').disabled);d.getElementById('biometric-confirmation').value='COPY-BIOMETRIC-TO-EMPTY-SLOT';d.getElementById('biometric-request').click();await wait(()=>posts.length===3&&!d.getElementById('refresh').disabled);assert.equal(posts[2].body.intent.targetPerson,'0003');assert.equal(posts[2].body.previewHash,'A'.repeat(64));assert.equal(d.getElementById('biometric-preview').textContent,'');assert.equal(form.elements.approval.value,'');assert.equal(d.getElementById('biometric-confirmation').value,'');
 d.querySelector('#biometric-jobs button').click();await wait(()=>posts.length===4&&!d.getElementById('refresh').disabled);assert.equal(posts[3].body.version,version);assert.match(posts[3].path,/\/cancel$/);
 hold=true;w.dispatchEvent(new w.Event('focus'));await wait(()=>release);hold=false;rights=[];w.dispatchEvent(new w.Event('focus'));await wait(()=>!d.getElementById('refresh').disabled&&d.getElementById('biometric').hidden);release();await new Promise(r=>setTimeout(r,20));assert.equal(d.getElementById('biometric-jobs').textContent,'');assert.equal(d.getElementById('biometric-preview').textContent,'');
 rights=['devices.biometrics.jobs.read'];deny=true;w.dispatchEvent(new w.Event('focus'));await wait(()=>d.getElementById('notice').textContent.includes('لغو'));assert.equal(d.getElementById('biometric').hidden,true);assert.equal(posts.some(p=>JSON.stringify(p).includes('TemplateData')),false);
 dom.window.close();console.log('PASS biometric transfer DOM: independent grants, exact identities, CSRF, preview invalidation, state-only output, cancellation version and late/revoked clearing');
})().catch(e=>{console.error(e);process.exit(1);});
