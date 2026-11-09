'use strict';
(() => {
  const $=id=>document.getElementById(id),form=$('filter');let busy=false;
  function clear(){$('result').hidden=true;$('body').replaceChildren();}
  function show(s){$('notice').textContent=s;}
  form.elements.workDate.addEventListener('input',clear);
  form.addEventListener('submit',async event=>{event.preventDefault();if(busy)return;busy=true;for(const e of form.querySelectorAll('input,button'))e.disabled=true;clear();show('');
    try{const r=await fetch('/schedules/own/'+encodeURIComponent(form.elements.workDate.value),{credentials:'same-origin',cache:'no-store'});if(!r.ok){show(r.status===401?'نشست پایان یافته؛ دوباره وارد شوید.':r.status===403?'ابتدا گذرواژهٔ موقت را تغییر دهید یا دسترسی لازم را دریافت کنید.':r.status===404?'برای این روز برنامه‌ای در دسترس نیست یا مجوز مشاهده ندارید.':r.status===409?'برنامه یا مرز زمانی مبهم است؛ به مسئول سامانه اطلاع دهید.':'تاریخ یا درخواست معتبر نیست.');return;}
      const data=await r.json();const kind=data.dayKind===null?'ثبت نشده':(['کاری','تعطیل','استراحت'][data.dayKind]??'نامشخص');const values=[['روز میلادی',data.workDate],['روز شمسی',[data.jalaliYear,data.jalaliMonth,data.jalaliDay].join('/')],['تقویم',data.calendar],['نوع روز',kind],['شیفت',data.shift],['منطقهٔ زمانی سایت',data.timeZoneId],['آغاز UTC',data.startUtc],['پایان UTC',data.endUtc]];
      for(const [label,value] of values){const tr=document.createElement('tr'),th=document.createElement('th'),td=document.createElement('td');th.scope='row';th.textContent=label;td.textContent=value??'—';tr.append(th,td);$('body').append(tr);}$('result').hidden=false;
    }catch{show('ارتباط برقرار نشد؛ دوباره تلاش کنید.');}finally{busy=false;for(const e of form.querySelectorAll('input,button'))e.disabled=false;}
  });
})();
