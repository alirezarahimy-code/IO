'use strict';
(() => {
  // Contract names and paths correspond to the existing schedule API.
  const specs = {
    calendars:{label:'تقویم‌ها',single:'تقویم',endpoint:'calendars',columns:[['name','نام'],['companyId','شناسهٔ شرکت'],['id','شناسهٔ تقویم']],fields:[['companyId','شناسهٔ شرکت','text',true,36],['name','نام تقویم','text',true,200]]},
    days:{label:'روزهای تقویم',single:'روز تقویم',endpoint:'calendar-days',columns:[['calendarId','شناسهٔ تقویم'],['date','تاریخ میلادی'],['kind','نوع روز'],['note','یادداشت'],['id','شناسه']],fields:[['calendarId','شناسهٔ تقویم','text',true,36],['date','تاریخ میلادی','date',true],['kind','نوع روز','kind',true],['note','یادداشت (اختیاری)','text',false,500]]},
    shifts:{label:'شیفت‌ها',single:'شیفت ثابت',endpoint:'shifts',columns:[['name','نام'],['companyId','شرکت'],['siteId','سایت'],['startsAt','شروع'],['endsAt','پایان'],['endDayOffset','روز پایان'],['id','شناسه']],fields:[['companyId','شناسهٔ شرکت','text',true,36],['siteId','شناسهٔ سایت','text',true,36],['name','نام شیفت','text',true,200],['startsAt','ساعت شروع','time',true],['endsAt','ساعت پایان','time',true],['endDayOffset','روز پایان','offset',true]]},
    assignments:{label:'برنامه‌های کاری',single:'انتساب برنامه',endpoint:'assignments',columns:[['employeeId','شناسهٔ کارمند'],['calendarId','تقویم'],['shiftId','شیفت'],['validFrom','شروع میلادی'],['validUntilExclusive','پایان انحصاری'],['id','شناسه']],fields:[['employeeId','شناسهٔ کارمند','text',true,36],['calendarId','شناسهٔ تقویم','text',true,36],['shiftId','شناسهٔ شیفت','text',true,36],['validFrom','شروع میلادی','date',true],['validUntilExclusive','پایان انحصاری میلادی (اختیاری)','date',false]]},
    orgAssignments:{label:'انتساب واحد',single:'انتساب واحد',catalog:'/org/catalog/assignments',post:'/org/assignments',columns:[['employeeId','کارمند'],['orgUnitId','واحد'],['validFrom','شروع میلادی'],['validUntilExclusive','پایان انحصاری'],['id','شناسه']],fields:[['employeeId','شناسهٔ کارمند','text',true,36],['unitId','شناسهٔ واحد','text',true,36],['validFrom','شروع میلادی','date',true],['validUntilExclusive','پایان انحصاری میلادی (اختیاری)','date',false]]},
    siteAssignments:{label:'انتساب محل کار',single:'انتساب محل کار',catalog:'/org/catalog/site-assignments',post:'/org/site-assignments',columns:[['employeeId','کارمند'],['siteId','سایت'],['validFrom','شروع میلادی'],['validUntilExclusive','پایان انحصاری'],['id','شناسه']],fields:[['employeeId','شناسهٔ کارمند','text',true,36],['siteId','شناسهٔ سایت','text',true,36],['validFrom','شروع میلادی','date',true],['validUntilExclusive','پایان انحصاری میلادی (اختیاری)','date',false]]},
    employmentProfiles:{label:'پرونده‌های استخدام',single:'پروندهٔ استخدام',catalog:'/org/catalog/employment-profiles',post:'/org/employment-profiles',columns:[['employeeId','کارمند'],['statusCode','کد وضعیت'],['title','عنوان'],['validFrom','شروع میلادی'],['validUntilExclusive','پایان انحصاری'],['id','شناسه']],fields:[['employeeId','شناسهٔ کارمند','text',true,36],['statusCode','کد وضعیت','text',true,80],['title','عنوان (اختیاری)','text',false,200],['validFrom','شروع میلادی','date',true],['validUntilExclusive','پایان انحصاری میلادی (اختیاری)','date',false]]}
  };
  const routes = {
    '/workspace/employee-history':{title:'تاریخچهٔ انتساب کارکنان',description:'انتساب واحد و محل کار و پروندهٔ استخدام با بازهٔ تاریخ‌دار.',kinds:['orgAssignments','siteAssignments','employmentProfiles'],org:true},
    '/workspace/calendars':{title:'تقویم و روزهای کاری',description:'تعریف تقویم و تعیین نوع روزها پیش از انتساب برنامه.',kinds:['calendars','days']},
    '/workspace/shifts':{title:'شیفت‌های ثابت',description:'تعریف شیفت ثابت با زمان و روز پایان صریح.',kinds:['shifts']},
    '/workspace/schedules':{title:'برنامه و انتساب شیفت',description:'ثبت برنامهٔ تاریخ‌دار برای یک کارمند.',kinds:['assignments']}
  };
  const page = routes[location.pathname]; if (!page) return;
  document.getElementById('section-name').textContent = page.org ? 'سازمان و کارکنان' : 'برنامه‌ریزی';
  document.getElementById('page-footnote').textContent = page.org
    ? 'این امکانات آزمایشی و نیازمند مجوز organization.manage هستند. انتقال و تغییر پرونده از پنل ابزار موجود است و هنوز در این صفحه پیاده نشده است.'
    : 'این امکانات آزمایشی و نیازمند مجوز schedule.manage هستند. تقویم انتساب‌یافته قابل ویرایش نیست؛ نسخه‌سازی و جایگزینی برنامه در پنل ابزار موجود است و هنوز در این صفحه پیاده نشده است.';
  document.title = page.title + ' | سامانهٔ حضور'; document.getElementById('page-title').textContent = page.title; document.getElementById('page-description').textContent = page.description;
  const notice = document.getElementById('notice'), head = document.getElementById('head'), body = document.getElementById('body');
  const form = document.getElementById('create-form'), fields = document.getElementById('create-fields'), tabsContainer = document.getElementById('tabs');
  const previous = document.getElementById('previous'), next = document.getElementById('next'), refresh = document.getElementById('refresh');
  let kind = page.kinds[0], offset = 0, count = 0, busy = false, authorized = false;
  for (const key of page.kinds) { const button = document.createElement('button'); button.type='button'; button.dataset.kind=key; button.textContent=specs[key].label; tabsContainer.append(button); }
  const tabs = [...tabsContainer.querySelectorAll('button')]; document.getElementById('tabs-section').hidden = tabs.length < 2;
  function updateControls() {
    for (const tab of tabs) { tab.disabled = busy; tab.setAttribute('aria-pressed', String(tab.dataset.kind === kind)); }
    refresh.disabled=busy; previous.disabled=busy || !authorized || offset===0; next.disabled=busy || !authorized || count<100 || offset>=1000000;
    form.querySelector('button').disabled=busy || !authorized; for (const input of fields.querySelectorAll('input,select')) input.disabled=busy || !authorized;
  }
  function resetData() { authorized=false; count=0; body.replaceChildren(); document.getElementById('empty').hidden=true; form.reset(); }
  function setNotice(message) { notice.textContent=message; }
  async function request(url, options={}) {
    const response = await fetch(url, {...options,credentials:'same-origin',cache:'no-store'});
    if (!response.ok) {
      if (response.status===401 || response.status===403) resetData();
      const message = response.status===401 ? 'نشست پایان یافته؛ دوباره وارد شوید.' : response.status===403 ? (page.org ? 'مجوز مدیریت سازمان ندارید یا باید گذرواژه را تغییر دهید.' : 'مجوز مدیریت برنامه ندارید یا باید گذرواژه را تغییر دهید.') : response.status===409 ? 'تعارض داده یا تغییر هم‌زمان؛ فهرست را بررسی کنید.' : response.status===400 ? 'داده‌ها یا شناسه‌ها معتبر نیستند؛ فیلدها را بررسی کنید.' : 'درخواست انجام نشد؛ پیش از تلاش مجدد فهرست را بررسی کنید.';
      throw new Error(message);
    }
    return response.json();
  }
  async function perform(action) { if (busy) return; busy=true; updateControls();
    try { await action(); } catch(e) { setNotice(e instanceof TypeError ? 'ارتباط قطع شد؛ وضعیت ثبت نامعلوم است. پیش از تلاش مجدد فهرست را بررسی کنید.' : e.message); }
    finally { busy=false; updateControls(); }
  }
  function drawFields() {
    const spec=specs[kind]; fields.replaceChildren(); head.replaceChildren();
    for (const [name,label,type,required,max] of spec.fields) {
      const wrapper=document.createElement('label'); wrapper.textContent=label; let input;
      if (type==='kind' || type==='offset') {
        input=document.createElement('select');
        for (const [value,title] of (type==='kind' ? [['0','کاری'],['1','تعطیل'],['2','استراحت']] : [['0','همان روز'],['1','روز بعد']])) input.add(new Option(title,value));
      } else { input=document.createElement('input'); input.type=type; if(max) input.maxLength=max; }
      input.name=name; input.required=required; wrapper.append(input); fields.append(wrapper);
    }
    document.getElementById('list-title').textContent='فهرست ' + spec.label;
    document.getElementById('create-title').textContent='ثبت ' + spec.single;
    const tr=document.createElement('tr'); for(const [,label] of spec.columns){ const th=document.createElement('th'); th.scope='col'; th.textContent=label; tr.append(th); } head.append(tr);
  }
  async function load() {
    const result=await request((specs[kind].catalog || '/scheduling/catalog/' + kind) + '?offset=' + offset); authorized=true; count=result.items.length; body.replaceChildren();
    document.getElementById('page-label').textContent='از ردیف ' + (offset+1).toLocaleString('fa-IR'); document.getElementById('empty').hidden=count!==0;
    for(const item of result.items) { const tr=document.createElement('tr');
      for(const [key] of specs[kind].columns) { const td=document.createElement('td'); let value=item[key];
        if(key==='kind') value=['کاری','تعطیل','استراحت'][value] ?? value;
        if(key==='endDayOffset') value=value===0?'همان روز':value===1?'روز بعد':value;
        td.textContent=value ?? '—'; tr.append(td); } body.append(tr); }
  }
  for(const tab of tabs) tab.addEventListener('click',()=>perform(async()=>{kind=tab.dataset.kind;offset=0;resetData();drawFields();await load();setNotice('');}));
  refresh.addEventListener('click',()=>perform(async()=>{await load();setNotice('اطلاعات به‌روزرسانی شد.');}));
  previous.addEventListener('click',()=>perform(async()=>{offset-=100;try{await load();setNotice('');}catch(e){offset+=100;throw e;}}));
  next.addEventListener('click',()=>perform(async()=>{offset+=100;try{await load();setNotice('');}catch(e){offset-=100;throw e;}}));
  form.addEventListener('submit',event=>{event.preventDefault();perform(async()=>{
    const payload={}; for(const [name,,type,required] of specs[kind].fields){const value=form.elements[name].value.trim();payload[name]=(type==='kind'||type==='offset')?Number(value):(value || (required?'':null));}
    const csrf=await request('/auth/csrf'); await request(specs[kind].post || '/scheduling/' + specs[kind].endpoint,{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':csrf.token},body:JSON.stringify(payload)});
    form.reset();setNotice('ثبت انجام شد؛ در حال به‌روزرسانی فهرست…');
    try{await load();setNotice('ثبت انجام شد؛ ممکن است رکورد در صفحهٔ دیگری باشد.');}catch{setNotice('ثبت انجام شد، اما به‌روزرسانی فهرست ناموفق بود.');}
  });});
  drawFields(); perform(async()=>{await load();setNotice('');});
})();
