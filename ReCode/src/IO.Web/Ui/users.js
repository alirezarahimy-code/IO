'use strict';
(() => {
  const $=id=>document.getElementById(id),create=$('create-form'),state=$('state-form'),role=$('role-form'),
    search=$('search-form'),scope=$('scope-form'),password=$('password-form');
  let offset=0,count=0,query=null,authorized=false,busy=false,selected=null;
  const detailForms=()=>[...state.querySelectorAll('button'),...role.querySelectorAll('input,select,button'),
    ...scope.querySelectorAll('input,button'),...password.querySelectorAll('input,button')];
  function controls(){$('refresh').disabled=busy;$('previous').disabled=busy||!authorized||offset===0;
    $('next').disabled=busy||!authorized||count<100||offset>=1000000;
    $('search-form').querySelector('button[type=submit],button').disabled=busy||!authorized;
    $('clear-search').disabled=busy||!authorized||query===null;
    for(const e of create.querySelectorAll('input,button'))e.disabled=busy||!authorized;
    for(const e of detailForms())e.disabled=busy||!authorized||!selected;}
  function cancelSelection(){selected=null;$('selected-section').hidden=true;$('selected-label').textContent='';
    $('detail-status').textContent='در حال دریافت…';$('roles-body').replaceChildren();$('scopes-body').replaceChildren();
    role.reset();scope.reset();password.reset();}
  function clear(){authorized=false;count=0;$('body').replaceChildren();$('page-label').textContent='';$('empty').hidden=true;
    create.reset();cancelSelection();}
  function show(s){$('notice').textContent=s;}
  async function request(url,options={}){const r=await fetch(url,{...options,credentials:'same-origin',cache:'no-store'});
    if(!r.ok){if(r.status===401||r.status===403)clear();
      throw new Error(r.status===401?'نشست پایان یافته؛ دوباره وارد شوید.':r.status===403?'مجوز مدیریت هویت ندارید یا باید گذرواژه را تغییر دهید.':r.status===400?'اطلاعات معتبر نیست، عملیات تکراری است یا تغییر مجاز نیست.':r.status===409?'تعارض رخ داد؛ فهرست را بررسی کنید.':'درخواست انجام نشد؛ پیش از تلاش مجدد وضعیت را بررسی کنید.');}
    return r.status===204?null:r.json();}
  async function post(url,data){const token=await request('/auth/csrf');
    return request(url,{method:'POST',headers:{'X-CSRF-TOKEN':token.token,'Content-Type':'application/json'},body:JSON.stringify(data)});}
  async function run(action){if(busy)return;busy=true;controls();try{await action();}catch(e){
    show(e instanceof TypeError?'ارتباط قطع شد؛ وضعیت عملیات نامعلوم است. پیش از تلاش مجدد فهرست و رویدادها را بررسی کنید.':e.message);}finally{busy=false;controls();}}
  async function load(){const url='/admin/catalog/users?offset='+offset+(query?'&q='+encodeURIComponent(query):'');
    const data=await request(url);authorized=true;count=data.items.length;$('body').replaceChildren();$('empty').hidden=count!==0;
    $('page-label').textContent=(query?'نتیجهٔ جست‌وجو ':'از ردیف ')+(offset+1).toLocaleString('fa-IR');
    for(const item of data.items){const tr=document.createElement('tr');
      for(const value of [item.userName,item.employeeId??'—',item.isActive?'فعال':'غیرفعال',item.id]){
        const td=document.createElement('td');td.textContent=value;tr.append(td);}
      const td=document.createElement('td'),button=document.createElement('button');
      button.type='button';button.textContent='انتخاب';
      button.setAttribute('aria-label','انتخاب حساب '+item.userName);
      button.addEventListener('click',()=>{if(busy)return;selected=item;role.reset();scope.reset();password.reset();
        $('selected-label').textContent=item.userName+' — '+item.id+' — '+(item.isActive?'فعال':'غیرفعال');
        state.querySelector('button[type=submit]').textContent=item.isActive?'غیرفعال‌سازی حساب':'فعال‌سازی حساب';
        $('selected-section').hidden=false;controls();run(async()=>{await Promise.all([loadDetail(),loadRoleOptions()]);});});
      td.append(button);tr.append(td);$('body').append(tr);}}
  async function loadDetail(){const data=await request('/admin/users/'+encodeURIComponent(selected.id));
    $('roles-body').replaceChildren();$('scopes-body').replaceChildren();
    $('detail-status').textContent='نقش‌ها: '+data.roles.length.toLocaleString('fa-IR')+' · دامنه‌های سازمانی: '+data.scopes.length.toLocaleString('fa-IR');
    for(const item of data.roles){const tr=document.createElement('tr');
      for(const value of [item.roleName,item.roleId]){const td=document.createElement('td');td.textContent=value;tr.append(td);}
      const td=document.createElement('td'),button=document.createElement('button');
      button.type='button';button.textContent='لغو';
      button.addEventListener('click',()=>run(async()=>{await post('/admin/user-roles/revoke',{userId:selected.id,roleId:item.roleId});
        show('نقش از حساب لغو شد.');await loadDetail();}));
      td.append(button);tr.append(td);$('roles-body').append(tr);}
    for(const item of data.scopes){const tr=document.createElement('tr');
      for(const value of [item.orgUnitId,item.validFrom,item.validUntilExclusive??'تا لغو']){
        const td=document.createElement('td');td.textContent=value;tr.append(td);}
      const td=document.createElement('td'),button=document.createElement('button');
      button.type='button';button.textContent='لغو';
      button.addEventListener('click',()=>run(async()=>{await post('/admin/user-scopes/'+encodeURIComponent(item.id)+'/revoke',null);
        show('دامنهٔ دسترسی لغو شد.');await loadDetail();}));
      td.append(button);tr.append(td);$('scopes-body').append(tr);}}
  async function loadRoleOptions(){try{const data=await request('/admin/catalog/roles'),list=$('role-options');
    list.replaceChildren();for(const item of data.items){const option=document.createElement('option');
      option.value=item.id;option.label=item.name;list.append(option);}}catch{/* فهرست نقش‌ها اختیاری است؛ ورود دستی شناسه همچنان کار می‌کند. */}}
  $('refresh').addEventListener('click',()=>run(async()=>{cancelSelection();await load();show('اطلاعات به‌روزرسانی شد.');}));
  $('previous').addEventListener('click',()=>run(async()=>{offset-=100;cancelSelection();try{await load();show('');}catch(e){offset+=100;throw e;}}));
  $('next').addEventListener('click',()=>run(async()=>{offset+=100;cancelSelection();try{await load();show('');}catch(e){offset-=100;throw e;}}));
  $('cancel-selection').addEventListener('click',()=>{cancelSelection();controls();});
  search.addEventListener('submit',e=>{e.preventDefault();run(async()=>{if(!authorized)return;
    query=search.elements.q.value.trim()||null;offset=0;cancelSelection();await load();
    show(count===0&&query?'حسابی با این نام کاربری یافت نشد.':'');});});
  $('clear-search').addEventListener('click',()=>run(async()=>{if(!authorized)return;
    query=null;search.reset();offset=0;cancelSelection();await load();show('');}));
  create.addEventListener('submit',e=>{e.preventDefault();run(async()=>{if(!authorized)return;
    const payload={userName:create.elements.userName.value.trim(),password:create.elements.password.value,
      employeeId:create.elements.employeeId.value.trim()||null};
    create.elements.password.value='';
    const result=await post('/admin/users',payload);create.reset();cancelSelection();offset=0;
    try{await load();show('حساب ساخته شد؛ شناسه: '+result.result+'؛ ممکن است در صفحهٔ دیگری باشد.');}
    catch(e){show('حساب ساخته شد اما دریافت فهرست ناموفق بود: '+e.message+' پیش از تلاش مجدد بررسی کنید.');}});});
  state.addEventListener('submit',e=>{e.preventDefault();run(async()=>{if(!authorized||!selected)return;
    const id=selected.id,operation=selected.isActive?'disable':'enable';
    await post('/admin/users/'+encodeURIComponent(id)+'/'+operation,null);cancelSelection();
    try{await load();show('وضعیت حساب تغییر کرد و نشست‌های آن باطل شد.');}
    catch(e){show('وضعیت حساب تغییر کرد اما دریافت فهرست ناموفق بود: '+e.message);}});});
  role.addEventListener('submit',e=>{e.preventDefault();run(async()=>{if(!authorized||!selected)return;
    const operation=role.elements.operation.value;
    await post('/admin/user-roles'+(operation==='revoke'?'/revoke':''),{userId:selected.id,roleId:role.elements.roleId.value.trim()});
    role.reset();show(operation==='grant'?'نقش به حساب اعطا شد.':'نقش از حساب لغو شد.');await loadDetail();});});
  scope.addEventListener('submit',e=>{e.preventDefault();run(async()=>{if(!authorized||!selected)return;
    const pattern=/^\d{4}-\d{2}-\d{2}$/,until=scope.elements.validUntilExclusive.value.trim();
    if(!pattern.test(scope.elements.validFrom.value.trim())||(until&&!pattern.test(until)))
      throw new Error('تاریخ‌ها باید به شکل 2026-11-08 میلادی باشند.');
    await post('/admin/user-scopes',{userId:selected.id,unitId:scope.elements.unitId.value.trim(),
      validFrom:scope.elements.validFrom.value.trim(),validUntilExclusive:until||null});
    scope.reset();show('دامنهٔ دسترسی اعطا شد.');await loadDetail();});});
  password.addEventListener('submit',e=>{e.preventDefault();run(async()=>{if(!authorized||!selected)return;
    if(password.elements.newPassword.value!==password.elements.confirm.value)
      throw new Error('تکرار گذرواژه با گذرواژهٔ جدید یکسان نیست.');
    const value=password.elements.newPassword.value;
    await post('/admin/users/'+encodeURIComponent(selected.id)+'/password',{newPassword:value});
    password.reset();password.elements.confirm.value='';
    show('گذرواژهٔ موقت تنظیم شد؛ نشست‌های حساب باطل و تغییر گذرواژه در ورود بعدی الزامی است.');});});
  controls();run(async()=>{await load();show('');});
})();
