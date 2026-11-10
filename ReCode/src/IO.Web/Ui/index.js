'use strict';
(() => {
  const $=id=>document.getElementById(id);let signedOut=false;
  async function request(url){const r=await fetch(url,{credentials:'same-origin',cache:'no-store'});if(!r.ok)throw new Error(r.status===401?'نشست پایان یافته؛ دوباره وارد شوید.':'دریافت اطلاعات ناموفق بود.');return r.json();}
  // The logout listener attaches immediately: panel summary requests must never
  // delay or overwrite the end of the session.
  $('logout').addEventListener('click',async()=>{if($('logout').disabled)return;$('logout').disabled=true;signedOut=true;
    try{const csrf=await request('/auth/csrf');await fetch('/auth/logout',{method:'POST',credentials:'same-origin',headers:{'X-CSRF-TOKEN':csrf.token}});$('notice').textContent='از حساب خارج شدید؛ برای ورود دوباره به صفحهٔ ورود بروید.';}
    catch{$('logout').disabled=false;signedOut=false;$('notice').textContent='خروج انجام نشد؛ دوباره تلاش کنید.';}});
  (async()=>{try{
      const me=await request('/auth/me');if(signedOut)return;$('who').textContent='حساب جاری: '+me.id;$('logout').disabled=false;
      const caps=await request('/workspace/capabilities');if(signedOut)return;
      $('employee-panel').hidden=!caps.employee;$('manager-panel').hidden=!caps.manager;$('admin-panel').hidden=!caps.admin;
      $('empty-panel').hidden=caps.employee||caps.manager||caps.admin;
      if(caps.employee){
        const r=await fetch('/corrections/own?offset=0&state=0',{credentials:'same-origin',cache:'no-store'});
        if(signedOut)return;
        if(r.ok){const data=await r.json();$('employee-summary').textContent='درخواست در انتظار تصمیم: '+data.items.length+(data.items.length===50?' یا بیشتر':'');}
        else $('employee-summary').textContent='وضعیت درخواست‌ها در دسترس نیست.';
      }
      if(caps.manager){
        const r=await fetch('/corrections/inbox?offset=0',{credentials:'same-origin',cache:'no-store'});
        if(signedOut)return;
        if(r.ok){const data=await r.json();$('manager-summary').textContent='درخواست در انتظار بررسی در کارتابل شما: '+data.items.length+(data.items.length===50?' یا بیشتر':'');}
        else $('manager-summary').textContent='کارتابل در دسترس نیست.';
      }
    }catch(e){if(signedOut)return;$('notice').textContent=e instanceof TypeError?'ارتباط برقرار نشد؛ دوباره تلاش کنید.':e.message;}
  })();
})();
