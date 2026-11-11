'use strict';
(() => {
  const $=id=>document.getElementById(id),form=$('filter');let busy=false,authorized=false,offset=0,count=0;
  function controls(){for(const e of form.querySelectorAll('input,select,button'))e.disabled=busy||!authorized;$('refresh').disabled=busy;$('previous').disabled=busy||!authorized||offset===0;$('next').disabled=busy||!authorized||count<50||offset>=100000;}
  function clearBalance(){$('balance').hidden=true;$('body').replaceChildren();}
  function clear(){authorized=false;count=0;clearBalance();form.reset();form.elements.leaveTypeId.replaceChildren(new Option('انتخاب نوع',''));$('type-empty').hidden=true;$('page-label').textContent='';}
  async function request(url){const r=await fetch(url,{credentials:'same-origin',cache:'no-store'});if(!r.ok){if(r.status===401||r.status===403)clear();throw new Error(r.status===401?'نشست پایان یافته؛ دوباره وارد شوید.':r.status===403?'مجوز مرخصی شخصی یا اتصال حساب به کارمند وجود ندارد.':r.status===400?'نوع یا تاریخ معتبر نیست.':r.status===409?'تعارض رخ داد؛ دوباره تلاش کنید.':'دریافت اطلاعات ناموفق بود.');}return r.json();}
  async function run(fn){if(busy)return;busy=true;controls();try{await fn();}catch(e){$('notice').textContent=e instanceof TypeError?'ارتباط برقرار نشد.':e.message;}finally{busy=false;controls();}}
  async function load(){const data=await request('/leave/own-types?offset='+offset);authorized=true;count=data.items.length;form.elements.leaveTypeId.replaceChildren(new Option('انتخاب نوع',''));for(const x of data.items)form.elements.leaveTypeId.add(new Option(x.name+' — '+x.symbol,x.id));$('type-empty').hidden=count!==0;$('page-label').textContent='از ردیف '+(offset+1).toLocaleString('fa-IR');}
  $('refresh').addEventListener('click',()=>run(async()=>{clearBalance();await load();$('notice').textContent='';}));$('previous').addEventListener('click',()=>run(async()=>{clearBalance();offset-=50;try{await load();}catch(e){offset+=50;throw e;}}));$('next').addEventListener('click',()=>run(async()=>{clearBalance();offset+=50;try{await load();}catch(e){offset-=50;throw e;}}));
  form.addEventListener('input',clearBalance);form.addEventListener('change',clearBalance);form.addEventListener('submit',e=>{e.preventDefault();run(async()=>{clearBalance();const result=await request('/leave/own-balance/'+encodeURIComponent(form.elements.leaveTypeId.value)+'?'+new URLSearchParams({asOf:form.elements.asOf.value}));for(const [label,value] of [['نوع',result.name],['سال شمسی',result.persianYear],['دقیقه در روز',result.minutesPerDay],['ابتدای سال',result.openingMinutes??'ثبت نشده'],['اعطا',result.grantedMinutes],['مصرف دستی',result.usedMinutes],['تعدیل',result.adjustmentMinutes],['ماندهٔ دفتر دستی',result.balanceMinutes??'نامشخص']]){const tr=document.createElement('tr'),th=document.createElement('th'),td=document.createElement('td');th.scope='row';th.textContent=label;td.textContent=String(value);tr.append(th,td);$('body').append(tr);}$('balance').hidden=false;$('notice').textContent='خلاصهٔ آزمایشی است و ماندهٔ قطعی نیست.';});});controls();run(load);
})();

// Leave request workflow: submit with idempotent operation, list own, cancel pending.
(() => {
  const $=id=>document.getElementById(id),form=$('request-form');let busy=false,offset=0,items=[];
  const stateText=s=>['معلق','تأییدشده','ردشده','لغوشده'][s]??String(s);
  const categoryText=c=>c===1?'اضافه‌کار':c===2?'مأموریت':'مرخصی';
  const kindText=k=>k===0?'روزانه':'ساعتی';
  async function request(url,options={}){const r=await fetch(url,{...options,credentials:'same-origin',cache:'no-store'});if(!r.ok)
    throw new Error(r.status===401?'نشست پایان یافته؛ دوباره وارد شوید.':r.status===403?'مجوز لازم را ندارید یا باید گذرواژه را تغییر دهید.':r.status===400?'اطلاعات معتبر نیست یا درخواست تکراری/تصمیم‌گرفته است.':r.status===409?'تعارض رخ داد؛ فهرست را دوباره بگیرید.':'درخواست انجام نشد.');return r.status===204?null:r.json();}
  async function post(url,data){const token=await request('/auth/csrf');return request(url,{method:'POST',headers:{'X-CSRF-TOKEN':token.token,'Content-Type':'application/json'},body:JSON.stringify(data)});}
  function controls(){for(const e of form.querySelectorAll('input,select,button'))e.disabled=busy||!form.elements.leaveTypeId.options.length;
    $('requests-previous').disabled=busy||offset===0;$('requests-next').disabled=busy||items.length<50||offset>=100000;}
  function rows(){$('requests-body').replaceChildren();$('requests-empty').hidden=items.length!==0;
    for(const item of items){const tr=document.createElement('tr');
      const cells=[item.workDate,categoryText(item.category)+' '+kindText(item.kind),item.minutes.toLocaleString('fa-IR'),stateText(item.state),
        item.decision?(stateText(item.decision)+(item.decisionReason?' — '+item.decisionReason:'')+' — '+(item.decisionAtUtc??'')):'—'];
      for(const v of cells){const td=document.createElement('td');td.textContent=v;tr.append(td);}
      const td=document.createElement('td');const history=document.createElement('a');history.textContent='تاریخچهٔ کامل';history.href='/workspace/reversals?kind=leave&own='+encodeURIComponent(item.id);td.append(history);
      if(item.state===0){const b=document.createElement('button');b.type='button';b.textContent='لغو';
        b.addEventListener('click',async()=>{if(busy)return;busy=true;controls();try{await post('/leave/requests/'+encodeURIComponent(item.id)+'/cancel',{reason:'لغو توسط درخواست‌دهنده'});await load();}catch(e){$('notice').textContent=e instanceof TypeError?'ارتباط قطع شد؛ وضعیت نامعلوم است.':e.message;}finally{busy=false;controls();}});
        td.append(b);}
      tr.append(td);$('requests-body').append(tr);}}
  async function load(){const data=await request('/leave/requests?offset='+offset);items=data.items;rows();controls();}
  form.addEventListener('submit',async e=>{e.preventDefault();if(busy)return;busy=true;controls();
    const operationId=crypto.randomUUID();
    try{const [category,kind]=form.elements.kind.value.split(':').map(Number),wd=form.elements.workDate.value;
      if(kind===1&&(!form.elements.fromTime.value||!form.elements.untilTime.value))throw new Error('برای مرخصی ساعتی هر دو ساعت الزامی است.');
      const payload={operationId,leaveTypeId:form.elements.leaveTypeId.value,category,kind,workDate:wd,
        fromAtUtc:kind===1?wd+'T'+form.elements.fromTime.value+':00Z':null,
        untilAtUtcExclusive:kind===1?wd+'T'+form.elements.untilTime.value+':00Z':null,reason:form.elements.reason.value.trim()};
      await post('/leave/requests',payload);form.elements.reason.value='';offset=0;await load();
      $('notice').textContent='درخواست مرخصی ثبت شد و در انتظار تصمیم مدیر است.';}
    catch(err){$('notice').textContent=err instanceof TypeError?'ارتباط قطع شد؛ وضعیت ثبت نامعلوم است.':err.message;}
    finally{busy=false;controls();}});
  form.elements.kind.addEventListener('change',()=>{const hourly=form.elements.kind.value.split(':')[1]==='1';
    form.elements.fromTime.disabled=busy||!hourly;form.elements.untilTime.disabled=busy||!hourly;});
  $('requests-previous').addEventListener('click',async()=>{if(busy)return;busy=true;controls();offset-=50;try{await load();}catch(e){offset+=50;$('notice').textContent=e.message;}finally{busy=false;controls();}});
  $('requests-next').addEventListener('click',async()=>{if(busy)return;busy=true;controls();offset+=50;try{await load();}catch(e){offset-=50;$('notice').textContent=e.message;}finally{busy=false;controls();}});
  (async()=>{try{const data=await request('/leave/own-types?offset=0');const select=form.elements.leaveTypeId;
      for(const item of data.items)select.append(new Option(item.name+' ('+item.symbol+')',item.id));
      select.disabled=false;form.elements.workDate.disabled=false;form.elements.reason.disabled=false;
      form.querySelector('button[type=submit]').disabled=false;
      form.elements.fromTime.disabled=true;form.elements.untilTime.disabled=true;
      await load();}
    catch(e){$('notice').textContent=e instanceof TypeError?'ارتباط قطع شد.':e.message;controls();}})();
})();
