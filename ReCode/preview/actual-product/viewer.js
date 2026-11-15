'use strict';
(() => {
 const $=id=>document.getElementById(id);
 const groupNames={common:'مشترک',employee:'کارمند',manager:'مدیر واحد',admin:'مدیریت سامانه',operator:'دستگاه‌های تردد',executive:'مدیر ارشد',workflow:'بازگشت تصمیم',technical:'فنی'};
 const groupOrder=['common','employee','manager','admin','operator','executive','workflow','technical'];
 const digits=n=>new Intl.NumberFormat('fa-IR').format(n);
 let inventory=null,selected=null,mode='layout';
 const frame=$('product-frame');
 function frameUrl(route){return route+'?__preview_mode='+mode;}
 function renderNavigation(){
  const term=$('search').value.trim().toLocaleLowerCase('fa');
  const pages=inventory.pages.filter(p=>(p.title+' '+p.route+' '+p.resource+' '+groupNames[p.group]).toLocaleLowerCase('fa').includes(term));
  $('visible-count').textContent=digits(pages.length)+' مسیر از '+digits(inventory.routes)+' مسیر موجود';
  const nav=$('pages');nav.replaceChildren();
  for(const group of groupOrder){
   const rows=pages.filter(p=>p.group===group);if(!rows.length)continue;
   const section=document.createElement('section');section.className='nav-group';
   const heading=document.createElement('h2'),count=document.createElement('span');heading.textContent=groupNames[group];count.textContent=digits(rows.length);heading.append(count);section.append(heading);
   for(const row of rows){const a=document.createElement('a');a.className='nav-item';a.href='#'+encodeURIComponent(row.route);a.textContent=row.title;a.title=row.route+' · '+row.resource;if(selected?.route===row.route)a.setAttribute('aria-current','page');section.append(a);}
   nav.append(section);
  }
  if(!pages.length){const p=document.createElement('p');p.className='empty-search';p.textContent='صفحهٔ موجودی با این عبارت پیدا نشد.';nav.append(p);}
 }
 function showHelp(row){
  const box=$('help-content');box.replaceChildren();if(!row.article){box.textContent='برای این مسیر مقاله‌ای در فایل راهنمای محصول ثبت نشده است.';return;}
  const p=document.createElement('p');p.textContent=row.article.Overview;box.append(p);
  for(const [title,key] of [['روال ثبت‌شده','Steps'],['محدودیت‌های ثبت‌شده','Limitations']]){const h=document.createElement('h3');h.textContent=title;box.append(h);const ul=document.createElement('ul');for(const text of row.article[key]||[]){const li=document.createElement('li');li.textContent=text;ul.append(li);}box.append(ul);}
 }
 function refreshMode(){
  $('layout-mode').setAttribute('aria-pressed',String(mode==='layout'));$('runtime-mode').setAttribute('aria-pressed',String(mode==='runtime'));
  $('mode-note').textContent=mode==='layout'?'HTML و CSS اصلی نمایش داده می‌شوند؛ JavaScript و ارسال فرم عمداً اجرا نمی‌شوند. متن‌هایی مثل «در حال دریافت» متن اولیهٔ همان قالب‌اند؛ جدول‌ها با دادهٔ نمونه پر نشده‌اند.':'JavaScript اصلی همین محصول اجرا می‌شود. APIهای وابسته به ورود/SQL پاسخ ۵۰۳ می‌گیرند؛ ممکن است بخش‌ها طبق منطق واقعی مجوز مخفی یا پیام خطا نمایش داده شود. هیچ نشست، مجوز یا نتیجهٔ موفقی جعل نشده است.';
  if(selected)$('open-page').href=frameUrl(selected.route);
 }
 function selectRoute(route,load=true){
  const row=inventory.pages.find(p=>p.route===route);if(!row)return false;
  selected=row;$('page-title').textContent=row.title;$('page-group').textContent=groupNames[row.group];$('page-route').textContent=row.route;$('source-path').textContent=row.source;$('source-hash').textContent=row.sha256.slice(0,16)+'…';$('source-hash').title=row.sha256;
  $('open-source').href='/__preview/source?route='+encodeURIComponent(row.route);frame.title='صفحهٔ واقعی محصول: '+row.title;
  showHelp(row);refreshMode();renderNavigation();if(load)frame.src=frameUrl(row.route);return true;
 }
 function routeFromHash(){try{return decodeURIComponent(location.hash.slice(1));}catch{return '';}}
 $('search').addEventListener('input',()=>{if(inventory)renderNavigation();});
 window.addEventListener('hashchange',()=>{if(inventory&&!selectRoute(routeFromHash()))selectRoute('/workspace/employees');});
 for(const value of ['layout','runtime'])$(value+'-mode').addEventListener('click',()=>{mode=value;refreshMode();if(selected)frame.src=frameUrl(selected.route);});
 for(const value of ['desktop','mobile'])$(value).addEventListener('click',()=>{$('frame-stage').classList.toggle('mobile',value==='mobile');for(const id of ['desktop','mobile'])$(id).setAttribute('aria-pressed',String(id===value));});
 $('reload').addEventListener('click',()=>{if(selected)frame.src=frameUrl(selected.route);});
 frame.addEventListener('load',()=>{
  try{
   const url=new URL(frame.contentWindow.location.href);if(url.origin!==location.origin)return;
   const row=inventory?.pages.find(p=>p.route===url.pathname);if(!row)return;
   if(url.searchParams.get('__preview_mode')!==mode){frame.src=frameUrl(row.route);return;}
   if(selected?.route!==row.route){selectRoute(row.route,false);history.replaceState(null,'','#'+encodeURIComponent(row.route));}
  }catch{/* The sandbox may block external navigation; no auth or origin bypass is attempted. */}
 });
 fetch('/__preview/inventory',{cache:'no-store'}).then(r=>{if(!r.ok)throw new Error();return r.json();}).then(data=>{
  inventory=data;$('route-count').textContent=digits(data.routes);$('file-count').textContent=digits(data.htmlFiles);$('revision').textContent=data.revision.slice(0,7);
  const route=routeFromHash()||'/workspace/employees';if(!selectRoute(route))selectRoute('/workspace/employees');
 }).catch(()=>{$('page-title').textContent='خواندن موجودی واقعی ناموفق بود؛ فهرست فرضی جایگزین نشده است.';});
})();
