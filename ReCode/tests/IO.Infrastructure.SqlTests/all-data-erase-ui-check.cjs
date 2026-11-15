'use strict';
const fs=require('node:fs'),assert=require('node:assert/strict'),{JSDOM}=require('jsdom');
const root=__dirname+'/../../src/IO.Web/Ui/';
async function wait(f){for(let n=0;n<300;n++){if(f())return;await new Promise(r=>setTimeout(r,10));}throw Error('Erase DOM timeout');}
(async()=>{
 const dom=new JSDOM(fs.readFileSync(root+'DeviceAllDataErases.html','utf8'),{url:'https://io.example/workspace/device-all-data-erases',runScripts:'outside-only'}),w=dom.window,d=w.document,$=id=>d.getElementById(id);
 let rights=[],deny=false,hold=false,release,observed=false;const posts=[],id='11111111-1111-4111-8111-111111111111',version='AQIDBAUGBwg=';
 w.fetch=async(path,options={})=>{
  assert.equal(options.credentials,'same-origin');assert.equal(options.cache,'no-store');
  if(path==='/workspace/capabilities')return{ok:true,status:200,json:async()=>({permissions:rights})};
  if(deny)return{ok:false,status:403};
  if(path==='/auth/csrf')return{ok:true,status:200,json:async()=>({token:'SYNTHETIC-CSRF'})};
  if(options.method==='POST'){assert.equal(options.headers['X-CSRF-TOKEN'],'SYNTHETIC-CSRF');posts.push({path,body:JSON.parse(options.body)});return{ok:true,status:200,json:async()=>({result:path.endsWith('/preview')?{previewHash:'A'.repeat(64),archiveCount:2,meaning:'<img src=x>'}:id})};}
  const result={items:[{id,deviceId:id,archiveId:id,state:observed?'observed':'proposed',recoveryOfId:observed?id:null,version,evidenceReference:'<script>bad()</script>'}]};
  if(hold)return new Promise(resolve=>{release=()=>resolve({ok:true,status:200,json:async()=>({result})});});
  return{ok:true,status:200,json:async()=>({result})};
 };
 w.eval(fs.readFileSync(root+'device-all-data-erases.js','utf8'));await wait(()=>!$('refresh').disabled);assert.equal($('proposal').hidden,true);
 rights=['devices.all-data.erase.read'];w.dispatchEvent(new w.Event('focus'));await wait(()=>d.querySelector('#jobs li')&&!$('refresh').disabled);assert.equal($('proposal').hidden,true);assert.equal(d.querySelector('#jobs script'),null);assert.equal(d.querySelector('#jobs button'),null);
 rights.push('devices.users.read','devices.attendance.read','devices.all-data.erase.propose','devices.all-data.erase.approve','devices.all-data.erase.recover.propose','devices.all-data.erase.recover.approve');w.dispatchEvent(new w.Event('focus'));await wait(()=>!$('proposal').hidden&&!$('refresh').disabled);
 const f=$('intent-form');for(const name of ['id','deviceId','archiveId','attendanceArchiveId'])f.elements[name].value=id;for(const name of ['deviceVersion','archiveVersion','attendanceArchiveVersion'])f.elements[name].value=version;f.elements.quiescedAtUtc.value='2026-11-08T01:00:00Z';f.elements.evidenceReference.value='SYNTHETIC-STOP';f.elements.approval.value=JSON.stringify({family:'all-data'});
 f.dispatchEvent(new w.Event('submit',{cancelable:true}));await wait(()=>posts.length===1&&!$('refresh').disabled);assert.equal(posts[0].body.recoveryOfId,undefined);assert.equal(posts[0].body.attendanceArchiveId,id);assert.equal(posts[0].body.attendanceArchiveVersion,version);assert.equal(posts[0].path,'/devices/all-data-erases/preview');assert.equal(d.querySelector('#preview img'),null);assert.equal($('submit-proposal').disabled,false);
 f.elements.evidenceReference.value='SYNTHETIC-CHANGED';f.dispatchEvent(new w.Event('input',{bubbles:true}));assert.equal($('submit-proposal').disabled,true);
 f.elements.recoveryOfId.value=id;f.dispatchEvent(new w.Event('submit',{cancelable:true}));await wait(()=>posts.length===2&&!$('refresh').disabled);$('confirmation').value='RELEASE-ALL-DATA-FENCE-NOT-DELETE-SUCCESS';$('submit-proposal').click();await wait(()=>posts.length===3&&!$('refresh').disabled);assert.equal(posts[2].body.intent.recoveryOfId,id);assert.equal(posts[2].body.intent.archiveVersion,version);assert.equal(f.elements.approval.value,'');assert.equal($('preview').textContent,'');assert.equal($('confirmation').value,'');
 const r=$('review-form');r.elements.id.value=id;r.elements.version.value=version;r.elements.evidenceReference.value='SYNTHETIC-REVIEW';r.elements.confirmation.value='DELETE-DEVICE-ALL-DATA-IRREVERSIBLY';r.dispatchEvent(new w.Event('submit',{cancelable:true}));await wait(()=>posts.length===4&&!$('refresh').disabled);assert.equal(posts[3].body.version,version);assert.match(posts[3].path,/\/approve$/);
 d.querySelector('#jobs button').click();await wait(()=>posts.length===5&&!$('refresh').disabled);assert.equal(posts[4].body.version,version);assert.match(posts[4].path,/\/cancel$/);
 observed=true;w.dispatchEvent(new w.Event('focus'));await wait(()=>!$('refresh').disabled&&d.querySelector('#jobs button'));assert.match(d.querySelector('#jobs button').textContent,/unknown/);d.querySelector('#jobs button').click();await wait(()=>posts.length===6&&!$('refresh').disabled);assert.equal(posts[5].body.version,version);assert.match(posts[5].path,/\/cancel$/);observed=false;
 hold=true;w.dispatchEvent(new w.Event('focus'));await wait(()=>release);hold=false;rights=[];w.dispatchEvent(new w.Event('focus'));await wait(()=>!$('refresh').disabled&&$('history').hidden);release();await new Promise(r=>setTimeout(r,20));assert.equal($('jobs').textContent,'');assert.equal($('preview').textContent,'');
 rights=['devices.all-data.erase.read'];deny=true;w.dispatchEvent(new w.Event('focus'));await wait(()=>$('notice').textContent.includes('لغو'));assert.equal($('history').hidden,true);
 dom.window.close();console.log('PASS R15 all-data clear DOM: two archives, independent roles, CSRF, archive/version binding, no implicit delete, recovery mode, preview invalidation, review/cancel versions, safe text and late/revoked clearing');
})().catch(e=>{console.error(e);process.exit(1);});
