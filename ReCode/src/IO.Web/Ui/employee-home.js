'use strict';
(() => {
  const $=id=>document.getElementById(id);let busy=false;
  function clear(){$('account-id').textContent='—';$('body').replaceChildren();$('requests-message').textContent='';}
  async function load(){if(busy)return;busy=true;$('refresh').disabled=true;clear();$('notice').textContent='';try{
    const session=await fetch('/auth/me',{credentials:'same-origin',cache:'no-store'});
    if(!session.ok){$('notice').textContent=session.status===401?'نشست پایان یافته؛ دوباره وارد شوید.':'حساب در دسترس نیست.';return;}
    const me=await session.json();$('account-id').textContent=me.id;
    const r=await fetch('/corrections/own?offset=0',{credentials:'same-origin',cache:'no-store'});
    if(!r.ok){if(r.status===401){clear();$('notice').textContent='نشست پایان یافته؛ دوباره وارد شوید.';}else $('requests-message').textContent=r.status===403?'مجوز مشاهدهٔ درخواست‌های شخصی ندارید.':'دریافت درخواست‌ها ناموفق بود.';return;}
    const data=await r.json();for(const item of data.items.slice(0,5)){const tr=document.createElement('tr');for(const value of [item.workDate,item.punchKind===0?'ورود':item.punchKind===1?'خروج':'نامشخص',['در انتظار','تأییدشده','ردشده','لغوشده'][item.state]??'نامشخص',item.id]){const td=document.createElement('td');td.textContent=value??'—';tr.append(td);}$('body').append(tr);}if(!data.items.length)$('requests-message').textContent='درخواستی ثبت نشده است.';
  }catch{clear();$('notice').textContent='ارتباط برقرار نشد؛ دوباره تلاش کنید.';}finally{busy=false;$('refresh').disabled=false;}}
  $('refresh').addEventListener('click',load);load();
})();
