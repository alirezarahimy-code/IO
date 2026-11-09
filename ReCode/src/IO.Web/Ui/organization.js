'use strict';
(() => {
  const specs = {
    organizations:{label:'سازمان‌ها', single:'سازمان', columns:[['name','نام'],['id','شناسه']], fields:[['name','نام سازمان',200,true]]},
    companies:{label:'شرکت‌ها', single:'شرکت', columns:[['name','نام'],['organizationId','شناسهٔ سازمان'],['id','شناسه']], fields:[['organizationId','شناسهٔ سازمان',36,true],['name','نام شرکت',200,true]]},
    sites:{label:'سایت‌ها', single:'سایت', columns:[['name','نام'],['companyId','شناسهٔ شرکت'],['timeZoneId','منطقهٔ زمانی'],['id','شناسه']], fields:[['companyId','شناسهٔ شرکت',36,true],['name','نام سایت',200,true],['timeZoneId','شناسهٔ منطقهٔ زمانی، مثل Asia/Tehran',100,true]]},
    units:{label:'واحدها', single:'واحد', columns:[['name','نام'],['companyId','شناسهٔ شرکت'],['parentId','شناسهٔ واحد بالادست'],['id','شناسه']], fields:[['companyId','شناسهٔ شرکت',36,true],['parentId','شناسهٔ واحد بالادست (اختیاری)',36,false],['name','نام واحد',200,true]]}
  };
  const notice = document.getElementById('notice'), head = document.getElementById('head'), body = document.getElementById('body');
  const form = document.getElementById('create-form'), fields = document.getElementById('create-fields');
  const previous = document.getElementById('previous'), next = document.getElementById('next'), refresh = document.getElementById('refresh');
  const tabs = [...document.querySelectorAll('[data-kind]')];
  let kind = 'organizations', offset = 0, count = 0, busy = false, authorized = false;
  function updateControls() {
    for (const button of tabs) { button.disabled = busy; button.setAttribute('aria-pressed', String(button.dataset.kind === kind)); }
    refresh.disabled = busy; previous.disabled = busy || !authorized || offset === 0; next.disabled = busy || !authorized || count < 100 || offset >= 1000000;
    form.querySelector('button').disabled = busy || !authorized; for (const input of fields.querySelectorAll('input')) input.disabled = busy || !authorized;
  }
  function resetData() { authorized = false; count = 0; body.replaceChildren(); document.getElementById('empty').hidden = true; form.reset(); }
  function setNotice(text) { notice.textContent = text; }
  async function request(url, options = {}) {
    const response = await fetch(url, {...options, credentials:'same-origin', cache:'no-store'});
    if (!response.ok) {
      if (response.status === 401 || response.status === 403) resetData();
      const message = response.status === 401 ? 'نشست شما پایان یافته است؛ دوباره از پنل وارد شوید.' : response.status === 403 ? 'دسترسی به مدیریت سازمان ندارید یا باید ابتدا گذرواژه را تغییر دهید.' : response.status === 409 ? 'تعارض رخ داد؛ اطلاعات را به‌روزرسانی و دوباره تلاش کنید.' : response.status === 400 ? 'اطلاعات معتبر نیست؛ شناسه‌ها و فیلدها را بررسی کنید.' : 'دریافت یا ثبت اطلاعات انجام نشد.';
      throw new Error(message);
    }
    return response.json();
  }
  async function perform(action) {
    if (busy) return; busy = true; updateControls();
    try { await action(); } catch (e) { setNotice(e instanceof Error ? e.message : 'خطای ارتباط با سرور.'); }
    finally { busy = false; updateControls(); }
  }
  function drawFields() {
    const spec = specs[kind]; fields.replaceChildren();
    for (const [name,label,max,required] of spec.fields) {
      const wrapper = document.createElement('label'); wrapper.textContent = label;
      const input = document.createElement('input'); input.name = name; input.maxLength = max; input.required = required;
      wrapper.append(input); fields.append(wrapper);
    }
    document.getElementById('list-title').textContent = 'فهرست ' + spec.label;
    document.getElementById('create-title').textContent = 'ایجاد ' + spec.single;
    head.replaceChildren(); const tr = document.createElement('tr');
    for (const [, title] of spec.columns) { const th = document.createElement('th'); th.scope = 'col'; th.textContent = title; tr.append(th); } head.append(tr);
  }
  async function load() {
    const result = await request('/org/catalog/' + kind + '?offset=' + offset); authorized = true; count = result.items.length; body.replaceChildren();
    document.getElementById('page-label').textContent = 'از ردیف ' + (offset + 1).toLocaleString('fa-IR');
    document.getElementById('empty').hidden = count !== 0;
    for (const item of result.items) { const tr = document.createElement('tr');
      for (const [key] of specs[kind].columns) { const td = document.createElement('td'); td.textContent = item[key] ?? '—'; tr.append(td); } body.append(tr); }
  }
  for (const tab of tabs) tab.addEventListener('click', () => perform(async () => { kind = tab.dataset.kind; offset = 0; resetData(); drawFields(); await load(); setNotice(''); }));
  refresh.addEventListener('click', () => perform(async () => { await load(); setNotice('اطلاعات به‌روزرسانی شد.'); }));
  previous.addEventListener('click', () => perform(async () => { offset -= 100; try { await load(); setNotice(''); } catch (e) { offset += 100; throw e; } }));
  next.addEventListener('click', () => perform(async () => { offset += 100; try { await load(); setNotice(''); } catch (e) { offset -= 100; throw e; } }));
  form.addEventListener('submit', event => { event.preventDefault(); perform(async () => {
    const payload = {}; for (const [name,,,required] of specs[kind].fields) payload[name] = form.elements[name].value.trim() || (required ? '' : null);
    const csrf = await request('/auth/csrf');
    await request('/org/' + kind, {method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':csrf.token},body:JSON.stringify(payload)});
    form.reset(); setNotice('ثبت انجام شد. در حال به‌روزرسانی فهرست…');
    try { await load(); setNotice('ثبت انجام شد؛ ممکن است رکورد در صفحهٔ دیگری باشد.'); }
    catch { setNotice('ثبت انجام شد، اما دریافت مجدد فهرست ناموفق بود.'); }
  }); });
  drawFields(); perform(async () => { await load(); setNotice(''); });
})();
