'use strict';
(() => {
 const $=id=>document.getElementById(id),form=$('group-form'),deletion=$('delete-form');
 let rights=new Set(),busy=false,selected=null,parentId=null,parent=null,offset=0,count=0,epoch=0,uncertainCreate=null,uncertainDelete=null;
 const read=()=>rights.has('devices.groups.read'),manage=()=>rights.has('devices.groups.manage'),message=text=>{$('notice').textContent=text;};
 function controls(){form.hidden=!manage();deletion.hidden=!manage()||!selected;for(const e of form.querySelectorAll('input,button'))e.disabled=busy||!manage();
  form.elements.companyId.readOnly=!!selected;for(const e of deletion.querySelectorAll('input,button'))e.disabled=busy||!manage()||!selected;
  for(const id of ['refresh','root'])$(id).disabled=busy;$('up').disabled=busy||!read()||!parentId;$('filter').querySelector('button').disabled=busy||!read();
  $('previous').disabled=busy||!read()||offset===0;$('next').disabled=busy||!read()||count<50||offset>=100000;for(const b of $('body').querySelectorAll('button'))b.disabled=busy;
 }
 function reset(){selected=null;form.reset();form.elements.operationId.value=crypto.randomUUID();form.elements.version.value='';form.elements.companyId.value=parent?.companyId||$('filter').elements.companyId.value.trim();form.elements.parentId.value=parentId||'';deletion.reset();$('delete-target').textContent='';$('form-title').textContent='ایجاد گروه';controls();}
 function clear(){rights=new Set();$('body').replaceChildren();$('branch').textContent='';parent=null;parentId=null;count=0;uncertainCreate=null;uncertainDelete=null;reset();}
 function uncertain(){if(uncertainCreate||uncertainDelete){message('نتیجهٔ عملیات نامعلوم است؛ ابتدا همان درخواست را تکرار کنید یا پس از بررسی وضعیت دوباره تصمیم بگیرید.');return true;}return false;}
 async function request(url,options={}){const r=await fetch(url,{...options,credentials:'same-origin',cache:'no-store'});if(!r.ok){if(r.status===401||r.status===403){++epoch;clear();}throw new Error(r.status===409?'تعارض نسخه/نام تکراری/چرخه/گروه دارای فرزند یا دستگاه؛ شاخه را تازه‌سازی کنید.':r.status===401||r.status===403?'نشست یا مجوز معتبر نیست؛ دادهٔ قبلی پاک شد.':'شناسه، شرکت، والد، نام یا نسخه معتبر نیست.');}return r.json();}
 async function post(url,data){const csrf=await request('/auth/csrf');return request(url,{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':csrf.token},body:JSON.stringify(data)});}
 function select(item){if(uncertain())return;selected=item;form.elements.operationId.value=item.id;form.elements.version.value=item.version;form.elements.companyId.value=item.companyId;form.elements.parentId.value=item.parentId||'';form.elements.name.value=item.name;deletion.reset();$('delete-target').textContent=item.name+' — '+item.id;$('form-title').textContent='ویرایش گروه';controls();}
 function draw(items){$('body').replaceChildren();for(const item of items){const row=document.createElement('tr');for(const v of [item.name,item.id,item.companyId,item.deviceCount]){const td=document.createElement('td');td.textContent=String(v);row.append(td);}const td=document.createElement('td');
  const open=document.createElement('button');open.type='button';open.textContent='بازکردن شاخه';open.dataset.action='open';open.addEventListener('click',()=>{if(!busy&&!uncertain()){parentId=item.id;offset=0;run(async()=>{await load();reset();});}});td.append(open);
  if(manage()){const edit=document.createElement('button');edit.type='button';edit.textContent='انتخاب';edit.dataset.action='select';edit.addEventListener('click',()=>select(item));td.append(edit);}row.append(td);$('body').append(row);
 }}
 async function load(){if(!read()){$('body').replaceChildren();$('branch').textContent='برای مشاهدهٔ شاخه‌ها مجوز مستقل خواندن لازم است.';count=0;return;}const ticket=epoch,company=$('filter').elements.companyId.value.trim();
  const data=await request('/device-groups?offset='+offset+(company?'&companyId='+encodeURIComponent(company):'')+(parentId?'&parentId='+encodeURIComponent(parentId):''));if(ticket!==epoch)return;parent=data.parent;count=data.items.length;draw(data.items);$('branch').textContent=parent?'شاخهٔ '+parent.name+' — '+parent.id+'؛ والد: '+(parent.parentId||'ریشه'):'ریشه‌های گروه‌ها';$('page-label').textContent='ردیف '+(offset+1)+' تا '+(offset+count);
 }
 async function run(action){if(busy)return;busy=true;controls();try{await action();}catch(e){message(e instanceof TypeError?'ارتباط قطع شد؛ نتیجه ممکن است نامعلوم باشد؛ شناسه/نسخه را نگه دارید.':e.message);}finally{busy=false;controls();}}
 async function refresh(){await run(async()=>{const ticket=++epoch;const caps=await request('/workspace/capabilities');if(ticket!==epoch)return;rights=new Set(caps.permissions);if(!read()&&!manage()){clear();return;}if(!uncertainCreate&&!uncertainDelete)reset();await load();});}
 form.addEventListener('submit',e=>{e.preventDefault();if(busy||!manage()||uncertainDelete)return;const f=form.elements,editing=!!selected,data={companyId:f.companyId.value.trim(),parentId:f.parentId.value.trim()||null,name:f.name.value},payload=editing?{expectedVersion:f.version.value,group:data}:{operationId:f.operationId.value,group:data},serialized=JSON.stringify(payload);
  if(uncertainCreate&&uncertainCreate!==serialized){message('دادهٔ ثبت نامعلوم را به حالت قبلی برگردانید؛ شناسه نباید با payload دیگر تکرار شود.');return;}
  run(async()=>{if(!editing)uncertainCreate=serialized;try{await post(editing?'/device-groups/'+selected.id:'/device-groups',payload);uncertainCreate=null;reset();message('گروه ذخیره شد.');await load();}catch(error){if(!(error instanceof TypeError))uncertainCreate=null;throw error;}});
 });
 deletion.addEventListener('submit',e=>{e.preventDefault();if(busy||!manage()||!selected||!deletion.elements.confirmed.checked)return;const intent={id:selected.id,version:selected.version};if(uncertainDelete&&(intent.id!==uncertainDelete.id||intent.version!==uncertainDelete.version))return;
  run(async()=>{uncertainDelete=intent;try{await post('/device-groups/'+intent.id+'/delete',{expectedVersion:intent.version});uncertainDelete=null;reset();message('گروه خالی حذف شد؛ سابقه محفوظ است.');await load();}catch(error){if(!(error instanceof TypeError))uncertainDelete=null;throw error;}});
 });
 $('new-group').addEventListener('click',()=>{if(!busy&&!uncertain())reset();});$('refresh').addEventListener('click',refresh);
 $('filter').addEventListener('submit',e=>{e.preventDefault();if(!busy&&!uncertain()){parentId=null;parent=null;offset=0;refresh();}});
 $('root').addEventListener('click',()=>{if(!busy&&!uncertain()){parentId=null;parent=null;offset=0;run(async()=>{await load();reset();});}});
 $('up').addEventListener('click',()=>{if(!busy&&!uncertain()){parentId=parent?.parentId||null;offset=0;run(async()=>{await load();reset();});}});
 for(const [id,delta]of [['previous',-50],['next',50]])$(id).addEventListener('click',()=>{if(!busy&&!uncertain()){offset=Math.max(0,Math.min(100000,offset+delta));run(load);}});
 window.addEventListener('focus',refresh);setInterval(refresh,30000);reset();refresh();
})();
