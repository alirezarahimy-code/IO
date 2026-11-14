'use strict';
(()=>{
 const $=id=>document.getElementById(id),form=$('filter-form');
 let epoch=0,busy=false,allowed=false,offset=0,more=false,selected=null,after=0,nextAfter=0,moreRows=false;
 const notice=text=>$('notice').textContent=text;
 const labels={'parsed-requires-mapping-review':'قالب خوانده شد؛ نیازمند بازبینی','exact-mapping-found':'نگاشت دقیق یافت شد؛ نه مجوز ثبت','unknown-employee':'کارمند ناشناس','mapping-conflict':'تعارض نگاشت','not-reviewable':'قابل بررسی نیست','unsupported-person-length':'طول شناسه برای مسیر نگاشت پشتیبانی نمی‌شود','unverified-device-identity':'هویت دستگاه تأیید نشده','repeated-row-requires-review':'سطر تکراری؛ نیازمند بازبینی','target-changed':'مقصد/پروفایل/منطقهٔ زمانی تغییر کرده','layout-mismatch':'قالب متفاوت','malformed-line':'سطر خراب','invalid-person':'شناسهٔ نامعتبر','invalid-time':'زمان نامعتبر','ambiguous-local-time':'ساعت محلی مبهم DST','nonexistent-local-time':'ساعت محلی ناموجود DST','invalid-fields':'فیلد نامعتبر'};
 const label=value=>labels[value]||value||'—';
 function controls(){
  $('review').hidden=!allowed;$('detail').hidden=!allowed||!selected;
  for(const e of form.querySelectorAll('input,button'))e.disabled=busy||!allowed;
  for(const b of $('evidence').querySelectorAll('button'))b.disabled=busy||!allowed;
  $('previous').disabled=busy||!allowed||offset===0;$('next').disabled=busy||!allowed||!more||offset>=100000;
  $('previous-rows').disabled=busy||!allowed||!selected||after===0;
  $('next-rows').disabled=busy||!allowed||!selected||!moreRows;$('original').disabled=busy||!allowed||!selected;
 }
 function clearDetails(){selected=null;after=nextAfter=0;moreRows=false;$('rows').replaceChildren();$('snapshot').textContent='';}
 function revoke(){++epoch;busy=false;allowed=false;offset=0;more=false;form.reset();$('evidence').replaceChildren();clearDetails();controls();}
 async function request(path,ticket){
  const response=await fetch(path,{credentials:'same-origin',cache:'no-store'});
  if(response.status===401||response.status===403){if(ticket===epoch){revoke();notice('مجوز/نشست معتبر نیست؛ شواهد شخصی پاک شدند.');}throw Error('دسترسی رد شد.');}
  if(!response.ok)throw Error('درخواست خواندن رد شد: '+response.status);
  return response.json();
 }
 async function list(ticket){
  const query=new URLSearchParams({offset:String(offset)}),device=form.elements.deviceId.value.trim();if(device)query.set('deviceId',device);
  const data=await request('/device-push-evidence?'+query,ticket);if(ticket!==epoch||!allowed)return;
  more=data.hasMore;$('evidence').replaceChildren();
  for(const item of data.items){
   const li=document.createElement('li'),p=document.createElement('p'),button=document.createElement('button');button.type='button';
   p.textContent=item.id+' | دستگاه '+item.deviceId+' | '+item.retainedAtUtc+' | hash '+item.payloadHash+' | '+item.timeZoneId+' | سطر '+item.rowCount+' / قالب‌خوانده‌شده '+item.parsedCount+' | تأییدنشده';
   button.textContent='بازبینی سطرهای تأییدنشده';button.addEventListener('click',()=>perform(async t=>{clearDetails();selected=item.id;await rows(t);}));
   li.append(p,button);$('evidence').append(li);
  }
 }
 async function rows(ticket){
  if(!selected)return;const id=selected;
  const data=await request('/device-push-evidence/'+encodeURIComponent(id)+'/rows?after='+after,ticket);
  if(ticket!==epoch||!allowed||selected!==id)return;
  nextAfter=data.nextAfter;moreRows=data.hasMore;
  $('snapshot').textContent='پیام '+id+' | hash '+data.payloadHash+' | منبع '+data.sourceId+' | timezone '+data.timeZoneId+' | Serial ادعاشده '+data.serial+' | Stamp کدر '+(data.stamp??'—')+' | مقصد یکسان '+(data.sameTarget?'بله':'خیر')+' | هویت تأییدنشده؛ بدون ثبت تردد/ACK';
  $('rows').replaceChildren();
  for(const item of data.items){
   const tr=document.createElement('tr');
   for(const value of [item.lineNumber+' / '+(item.multiplicityRank??'—'),item.pin??'نامعتبر',(item.localTime??'—')+' / '+(item.occurredAtUtc??'—'),[item.punchState,item.verifyMode,item.workCode].map(x=>x??'—').join(' / '),label(item.wireStatus),label(item.mappingStatus),label(item.quarantineStatus)]){
    const td=document.createElement('td');td.textContent=value;tr.append(td);
   }
   $('rows').append(tr);
  }
 }
 async function perform(action){
  if(busy||!allowed)return;const ticket=epoch;busy=true;controls();
  try{await action(ticket);}catch(error){if(ticket===epoch)notice(error.message);}
  finally{if(ticket===epoch){busy=false;controls();}}
 }
 async function refresh(){
  // Focus may invalidate an in-flight read. Old responses must never restore erased personal data.
  const ticket=++epoch;busy=true;allowed=false;more=false;$('evidence').replaceChildren();clearDetails();controls();
  try{
   const caps=await request('/workspace/capabilities',ticket);if(ticket!==epoch)return;
   allowed=caps.permissions.includes('devices.push.read');
   if(!allowed){revoke();notice('حق مستقل خواندن شواهد PUSH در دسترس نیست.');return;}
   await list(ticket);if(ticket===epoch)notice('شواهد فقط خواندنی‌اند؛ هیچ هویت دستگاه یا ترددی تأیید نشده است.');
  }catch(error){if(ticket===epoch)notice(error.message);}
  finally{if(ticket===epoch){busy=false;controls();}}
 }
 form.addEventListener('submit',event=>{event.preventDefault();perform(async t=>{offset=0;clearDetails();await list(t);});});
 $('refresh').addEventListener('click',refresh);window.addEventListener('focus',refresh);
 for(const[id,delta]of[['previous',-50],['next',50]])$(id).addEventListener('click',()=>perform(async t=>{offset=Math.max(0,Math.min(100000,offset+delta));clearDetails();await list(t);}));
 for(const[id,next]of[['previous-rows',false],['next-rows',true]])$(id).addEventListener('click',()=>perform(async t=>{after=next?nextAfter:Math.max(0,after-50);await rows(t);}));
 $('original').addEventListener('click',()=>perform(async t=>{
  const id=selected,data=await request('/device-push-evidence/'+encodeURIComponent(id)+'/original',t);
  if(t!==epoch||!allowed||id!==selected)return;
  const bytes=Uint8Array.from(atob(data.originalBase64),c=>c.charCodeAt(0));
  const url=URL.createObjectURL(new Blob([bytes],{type:'application/octet-stream'})),a=document.createElement('a');
  a.href=url;a.download='unverified-push-'+id+'.bin';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
 }));
 form.elements.deviceId.value=new URLSearchParams(location.search).get('deviceId')||'';refresh();
})();
