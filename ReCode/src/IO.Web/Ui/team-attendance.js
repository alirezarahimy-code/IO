'use strict';
(() => {
  const $=id=>document.getElementById(id),form=$('filter');let busy=false,offset=0,count=0,loaded=false;
  function controls(){for(const e of form.querySelectorAll('input,button'))e.disabled=busy;$('previous').disabled=busy||!loaded||offset===0;$('next').disabled=busy||!loaded||count<50||offset>=100000;}
  function clear(){loaded=false;count=0;$('results').hidden=true;$('body').replaceChildren();$('page-label').textContent='';$('empty').hidden=true;controls();}
  function show(s){$('notice').textContent=s;}
  for(const input of form.querySelectorAll('input'))input.addEventListener('input',()=>{clear();show('');});
  async function run(nextOffset){if(busy)return;busy=true;controls();clear();show('');try{
    const params=new URLSearchParams({fromUtc:form.elements.from.value+'T00:00:00Z',untilUtcExclusive:form.elements.until.value+'T00:00:00Z',offset:String(nextOffset)});const r=await fetch('/attendance/team?'+params,{credentials:'same-origin',cache:'no-store'});
    if(!r.ok){show(r.status===401?'نشست پایان یافته؛ دوباره وارد شوید.':r.status===403?'مجوز مشاهدهٔ تردد تیم وجود ندارد.':r.status===400?'بازهٔ UTC معتبر نیست یا بیش از ۶۲ روز است.':r.status===409?'تعارض هم‌زمان رخ داد؛ دوباره تلاش کنید.':'دریافت اطلاعات ناموفق بود.');return;}
    const data=await r.json();offset=nextOffset;count=data.items.length;loaded=true;for(const item of data.items){const tr=document.createElement('tr');for(const value of [item.personnelNumber,item.employeeName,item.occurredAtUtc,item.reportedEventType,item.source,item.sourceTimeZoneId]){const td=document.createElement('td');td.textContent=value??'—';tr.append(td);}$('body').append(tr);}$('empty').hidden=count!==0;$('page-label').textContent='از ردیف '+(offset+1).toLocaleString('fa-IR');$('results').hidden=false;
  }catch{show('ارتباط برقرار نشد؛ دوباره تلاش کنید.');}finally{busy=false;controls();}}
  form.addEventListener('submit',e=>{e.preventDefault();run(0);});$('previous').addEventListener('click',()=>run(offset-50));$('next').addEventListener('click',()=>run(offset+50));controls();
})();
