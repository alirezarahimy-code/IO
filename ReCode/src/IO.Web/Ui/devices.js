'use strict';
(() => {
 const $=id=>document.getElementById(id),form=$('device-form'),credentials=$('credential-form'),assignment=$('group-assignment-form');
 let permissions=new Set(),selected=null,offset=0,count=0,locationOffset=0,locationCount=0,busy=false,epoch=0,uncertain=null;
 const has=code=>permissions.has(code);
 const notice=text=>{$('notice').textContent=text;};
 function controls(){
  for(const e of form.querySelectorAll('input,select,button'))e.disabled=busy||!has(selected?'devices.update':'devices.create');
  form.hidden=!has('devices.create')&&!has('devices.update');
  // A reader may select a device solely to manage its credential without metadata-update permission.
  credentials.hidden=!selected||!has('devices.credentials.manage');
  for(const e of credentials.querySelectorAll('input,button'))e.disabled=busy||!selected||!has('devices.credentials.manage');
  assignment.hidden=!selected||!has('devices.groups.assign');
  for(const e of assignment.querySelectorAll('input,button'))e.disabled=busy||!selected||!has('devices.groups.assign');
  $('previous').disabled=busy||!has('devices.read')||offset===0;$('next').disabled=busy||!has('devices.read')||count<50||offset>=100000;
  $('locations-previous').disabled=busy||locationOffset===0;$('locations-next').disabled=busy||locationCount<50||locationOffset>=100000;
  $('refresh').disabled=busy;$('filter').querySelector('button').disabled=busy||!has('devices.read');
  const tcp=form.elements.profile.value==='zk-standalone';
  for(const key of ['host','port']){form.elements[key].disabled=busy||!tcp||!has(selected?'devices.update':'devices.create');form.elements[key].required=tcp;}
  for(const button of $('body').querySelectorAll('button'))button.disabled=busy;
 }
 function reset(){selected=null;form.reset();form.elements.operationId.value=crypto.randomUUID();form.elements.version.value='';credentials.reset();assignment.reset();$('form-title').textContent='معرفی دستگاه';controls();}
 function clearPrivate(){permissions=new Set();$('body').replaceChildren();$('locations').replaceChildren();count=0;locationCount=0;uncertain=null;reset();}
 async function request(url,options={}){
  const r=await fetch(url,{...options,credentials:'same-origin',cache:'no-store'});
  if(!r.ok){if(r.status===401||r.status===403){++epoch;clearPrivate();}throw new Error(r.status===409?'تعارض نسخه/شماره/محل دارای سابقه؛ برای تغییر مقصد دارای رمز، ابتدا رمز را صریحاً پاک کنید.':r.status===401||r.status===403?'نشست یا مجوز این عملیات معتبر نیست؛ دادهٔ قبلی پاک شد.':'شناسه، پروفایل، مقصد یا دادهٔ فرم معتبر نیست.');}
  return r.json();
 }
 async function post(url,data){const csrf=await request('/auth/csrf');return request(url,{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':csrf.token},body:JSON.stringify(data)});}
 function select(device){
  if(uncertain){notice('ابتدا ثبت نامعلوم را با همان داده و شناسه تکرار یا فهرست را بررسی کنید.');return;}
  selected=device;credentials.reset();assignment.elements.groupId.value=device.groupId||'';assignment.reset();$('form-title').textContent='ویرایش دستگاه '+device.number;
  for(const key of ['siteId','number','name','model','profile','host','port'])form.elements[key].value=device[key]??'';
  form.elements.enabled.checked=device.enabled;form.elements.operationId.value=device.id;form.elements.version.value=device.version;controls();
 }
 function render(items){$('body').replaceChildren();for(const d of items){const row=document.createElement('tr');
  for(const value of [d.number,d.name+' / '+d.model,d.siteId+' / '+d.companyId,d.profile+' / '+(d.host??'—')+':'+(d.port??'—'),(d.enabled?'فعال':'غیرفعال')+' / '+(d.hasCredential?'رمز دارد':'بدون رمز')+' / آزموده‌نشده',d.sourceId,(d.groupName||'بدون گروه')+' / '+(d.groupId||'—')]){const td=document.createElement('td');td.textContent=value;row.append(td);}
  const td=document.createElement('td');if(has('devices.update')||has('devices.credentials.manage')||has('devices.groups.assign')){const b=document.createElement('button');b.type='button';b.textContent='انتخاب';b.addEventListener('click',()=>select(d));td.append(b);}if(has('devices.connection.test')||has('devices.connection.read')){const link=document.createElement('a');link.textContent='تست و تاریخچهٔ ارتباط';link.href='/workspace/device-connection?'+new URLSearchParams({deviceId:d.id,version:d.version});td.append(link);}if(has('devices.info.read')||has('devices.clock.set')){const link=document.createElement('a');link.textContent='مشخصات و ساعت';link.href='/workspace/device-information?'+new URLSearchParams({deviceId:d.id,version:d.version});td.append(link);}if(has('devices.users.read')||has('devices.users.download')){const link=document.createElement('a');link.textContent='دریافت کاربران و تطبیق';link.href='/workspace/device-users?'+new URLSearchParams({deviceId:d.id,version:d.version});td.append(link);}if(has('devices.users.read')||has('devices.users.upload')){const link=document.createElement('a');link.textContent='ارسال عادی کاربران';link.href='/workspace/device-user-uploads?'+new URLSearchParams({deviceId:d.id,version:d.version});td.append(link);}if(has('devices.schedules.read')||has('devices.schedules.manage')){const link=document.createElement('a');link.textContent='زمان‌بندی SDK';link.href='/workspace/device-schedules?'+new URLSearchParams({deviceId:d.id,version:d.version});td.append(link);}if(has('devices.attendance.read')||has('devices.attendance.extract')||has('devices.attendance.commit')){const link=document.createElement('a');link.textContent='استخراج بازه‌ای تردد SDK';link.href='/workspace/device-attendance?'+new URLSearchParams({deviceId:d.id,version:d.version});td.append(link);}if(has('devices.files.read')||has('devices.files.preview')||has('devices.files.commit')){const link=document.createElement('a');link.textContent='ورود فایل و قرنطینه';link.href='/workspace/device-files?'+new URLSearchParams({deviceId:d.id,version:d.version});td.append(link);}row.append(td);$('body').append(row);
 }}
 async function load(){const ticket=epoch;if(!has('devices.read')){$('body').replaceChildren();count=0;return;}const company=$('filter').elements.companyId.value.trim();
  const data=await request('/devices?offset='+offset+(company?'&companyId='+encodeURIComponent(company):''));if(ticket!==epoch)return;
  count=data.items.length;render(data.items);$('page-label').textContent='ردیف '+(offset+1)+' تا '+(offset+count);
 }
 async function locations(){if(!has('devices.read')&&!has('devices.create')){$('locations').replaceChildren();locationCount=0;return;}const ticket=epoch;const data=await request('/devices/locations?offset='+locationOffset);if(ticket!==epoch)return;
  locationCount=data.items.length;$('locations').replaceChildren();for(const s of data.items){const li=document.createElement('li');li.textContent=s.companyName+' / '+s.name+' / '+s.timeZoneId+' — '+s.id;$('locations').append(li);}}
 async function refresh(){if(busy)return;busy=true;controls();const ticket=++epoch;try{const data=await request('/workspace/capabilities');if(ticket!==epoch)return;permissions=new Set(data.permissions);if(!['devices.read','devices.create','devices.update','devices.credentials.manage','devices.groups.assign'].some(has)){clearPrivate();return;}if(selected)reset();credentials.reset();await load();await locations();}catch(e){notice(e instanceof TypeError?'ارتباط برقرار نشد؛ وضعیت ثبت را از فهرست بررسی کنید.':e.message);}finally{busy=false;controls();}}
 form.elements.profile.addEventListener('change',controls);
 $('new-device').addEventListener('click',()=>{if(uncertain){notice('ثبت نامعلوم: تغییر شناسه پیش از بررسی یا تکرار همان درخواست مجاز نیست.');return;}reset();});
 form.addEventListener('submit',async e=>{e.preventDefault();if(busy||!has(selected?'devices.update':'devices.create'))return;
  const f=form.elements,tcp=f.profile.value==='zk-standalone',device={siteId:f.siteId.value.trim(),number:f.number.value,name:f.name.value,model:f.model.value,profile:f.profile.value,host:tcp?f.host.value:null,port:tcp?Number(f.port.value):null,enabled:f.enabled.checked};
  const payload=selected?{expectedVersion:f.version.value,device}:{operationId:f.operationId.value,device},serialized=JSON.stringify(payload);
  if(uncertain&&uncertain!==serialized){notice('دادهٔ ثبت نامعلوم را به حالت قبلی برگردانید؛ همان شناسه باید با همان داده تکرار شود.');return;}
  const editing=!!selected;busy=true;controls();try{if(!editing)uncertain=serialized;await post(editing?'/devices/'+selected.id:'/devices',payload);uncertain=null;reset();notice('مشخصات دستگاه ثبت شد؛ ارتباط هنوز آزموده‌نشده است.');await load();}
  catch(error){notice(error instanceof TypeError?(editing?'نتیجهٔ ویرایش نامعلوم است؛ پیش از تکرار، فهرست را تازه‌سازی کنید.':'ثبت نامعلوم است؛ همان شناسه و داده را نگه دارید و تکرار کنید.'):error.message);if(!(error instanceof TypeError))uncertain=null;}
  finally{busy=false;controls();}
 });
 credentials.addEventListener('submit',async e=>{e.preventDefault();if(busy||!selected||!has('devices.credentials.manage'))return;const f=credentials.elements;
  if((f.remove.checked&&f.secret.value)||(!f.remove.checked&&!f.secret.value)){notice('رمز جدید یا پاک‌کردن صریح را انتخاب کنید؛ نه هر دو.');return;}
  const data={expectedVersion:selected.version,secret:f.remove.checked?null:f.secret.value},id=selected.id;f.secret.value='';busy=true;controls();
  try{await post('/devices/'+id+'/credential',data);reset();notice('تغییر رمز ثبت شد؛ مقدار رمز قابل بازخوانی نیست.');await load();}
  catch(error){notice(error instanceof TypeError?'نتیجهٔ تغییر رمز نامعلوم است؛ ابتدا فهرست را تازه‌سازی کنید.':error.message);}finally{data.secret=null;busy=false;controls();}
 });
 assignment.addEventListener('submit',async e=>{e.preventDefault();if(busy||!selected||!has('devices.groups.assign'))return;const id=selected.id,data={expectedVersion:selected.version,groupId:assignment.elements.groupId.value.trim()||null};busy=true;controls();
  try{await post('/devices/'+id+'/group',data);reset();notice('گروه دستگاه ثبت شد؛ محل و ارتباط تغییری نکرد.');await load();}catch(error){notice(error instanceof TypeError?'نتیجهٔ انتساب نامعلوم است؛ ابتدا فهرست را تازه‌سازی کنید.':error.message);}finally{busy=false;controls();}
 });
 $('filter').addEventListener('submit',e=>{e.preventDefault();offset=0;refresh();});$('refresh').addEventListener('click',refresh);
 for(const [id,delta]of [['previous',-50],['next',50]])$(id).addEventListener('click',()=>{if(!busy){offset=Math.max(0,Math.min(100000,offset+delta));refresh();}});
 for(const [id,delta]of [['locations-previous',-50],['locations-next',50]])$(id).addEventListener('click',()=>{if(!busy){locationOffset=Math.max(0,Math.min(100000,locationOffset+delta));refresh();}});
 window.addEventListener('focus',refresh);setInterval(refresh,30000);reset();refresh();
})();
