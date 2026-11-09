'use strict';
(() => {
  const nav = document.getElementById('navigation');
  const content = document.getElementById('page-content');
  const search = document.getElementById('page-search');
  const sidebar = document.querySelector('.sidebar');
  const toggle = document.getElementById('menu-toggle');
  let pages = [];
  const activeSchemas = Object.create(null);
  const esc = value => String(value).replace(/[&<>"']/g, c => ({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const groupIcon = {'کارمند':'◉','مدیر':'◇','سازمان':'▦','برنامه‌ریزی':'▤','تردد و دستگاه':'◷','کارکرد':'◫','درخواست‌ها':'▧','گزارش‌ها':'▥','مدیریت سامانه':'⚙'};
  function selected() {
    let id = '';
    try { id = decodeURIComponent(location.hash.slice(1)); } catch { /* Unknown or malformed URL falls back safely. */ }
    return pages.find(p => p.id === id) || pages[0];
  }
  function drawNav() {
    const filter = search.value.trim().toLocaleLowerCase('fa');
    const visible = pages.filter(p => (p.title + ' ' + p.group).toLocaleLowerCase('fa').includes(filter));
    const current = selected();
    nav.innerHTML = [...new Set(visible.map(p => p.group))].map(group => `<div class="nav-group"><h2>${esc(group)}</h2>${visible.filter(p => p.group === group).map(p => `<a class="nav-link${p.id === current.id ? ' active' : ''}" ${p.id === current.id ? 'aria-current="page"' : ''} href="#${esc(p.id)}"><span class="nav-symbol" aria-hidden="true">${esc(groupIcon[group] || '•')}</span><span>${esc(p.title)}</span></a>`).join('')}</div>`).join('');
    document.getElementById('no-pages').hidden = visible.length !== 0;
  }
  function schemaField([name, label, type, max, required]) {
    const attrs = `name="${esc(name)}"${required ? ' required' : ''}${max ? ` maxlength="${max}"` : ''}`;
    let control;
    if (type === 'select') control = `<select ${attrs}><option value="">انتخاب نمونه</option><option value="example">گزینهٔ نمونه</option></select>`;
    else if (type === 'kind') control = `<select ${attrs}><option value="0">کاری</option><option value="1">تعطیل</option><option value="2">استراحت</option></select>`;
    else if (type === 'dayOffset') control = `<select ${attrs}><option value="0">همان روز</option><option value="1">روز بعد</option></select>`;
    else if (type === 'minutes') control = `<input type="number" min="0" max="720" step="1" ${attrs}>`;
    else control = `<input type="${esc(type)}" ${attrs} ${type === 'date' ? 'aria-describedby="date-help"' : ''}>`;
    return `<label>${esc(label)}${required ? ' <span aria-label="الزامی">*</span>' : ''}${control}<small>${esc(name)}</small></label>`;
  }
  function drawSchemaPage(page) {
    const specs = window.IOSchemaPages[page.id];
    const active = specs.find(x => x.key === activeSchemas[page.id]) || specs[0];
    activeSchemas[page.id] = active.key;
    const tabs = specs.map(spec => `<button type="button" data-schema="${esc(spec.key)}" aria-pressed="${spec.key === active.key}">${esc(spec.title)}</button>`).join('');
    const headings = active.columns.map(c => `<th scope="col">${esc(c)}</th>`).join('');
    const rows = active.rows.map(row => `<tr>${row.map(cell => `<td>${esc(cell)}</td>`).join('')}</tr>`).join('');
    content.innerHTML = `<div class="heading"><div><span class="eyebrow">${esc(page.group)} / طراحی بر اساس مدل</span><h1>${esc(page.title)}</h1><p>${esc(page.description)}</p></div><span class="badge">پیش‌نمایش · بدون اتصال</span></div>
      <section class="panel"><div class="panel-head"><div><h2>نمای اطلاعات</h2><p>فهرست نمونه؛ انتخاب بخش و جست‌وجو فقط روی دادهٔ ساختگی انجام می‌شود.</p></div><span class="badge">دادهٔ ساختگی</span></div>
      <div class="schema-tabs" aria-label="انتخاب نوع اطلاعات">${tabs}</div><div class="schema-search"><label for="schema-search">جست‌وجو در نمونه‌ها</label><input id="schema-search" type="search" placeholder="جست‌وجو در این فهرست"></div>
      <div class="table-wrap"><table><thead><tr>${headings}</tr></thead><tbody>${rows}</tbody></table></div><div class="table-note" id="schema-empty" hidden>موردی در دادهٔ ساختگی یافت نشد.</div><div class="table-note">نمایش نمونه است و اطلاعات واقعی را نشان نمی‌دهد.</div></section>
      <section class="panel"><div class="panel-head"><div><h2>فرم نمونهٔ ${esc(active.title)}</h2><p>فیلدها مطابق ورودی ایجاد در قرارداد فعلی هستند.</p></div><span class="badge">بدون ثبت</span></div><div class="panel-body">
      <div class="schema-fields">${active.fields.map(schemaField).join('')}</div>${active.fields.some(field => field[2] === 'date') ? '<p id="date-help" class="schema-help">تاریخ انتخابگر مرورگر میلادی است؛ پایان بازه انحصاری است. نمایش و ورود جلالی کامل بعداً افزوده می‌شود.</p>' : ''}
      <p class="schema-help">شناسه و نسخه توسط سامانه مدیریت می‌شوند. انتخاب‌ها فقط نمونه‌اند و تغییری ذخیره نمی‌شود.</p><div class="actions"><button type="button" disabled>ثبت غیرفعال تا اتصال</button></div></div></section>
      <p class="source-note">مدل: <code class="schema-contract">${esc(active.record)}</code> · قرارداد ورودی: <code class="schema-contract">${esc(active.contract)}</code> · ارجاع قدیم: <code class="schema-contract">${esc(page.source)}</code></p>`;
  }
  function drawPage() {
    if (!pages.length) return;
    const page = selected();
    document.title = `${page.title} | پیش‌نمایش IO`;
    document.getElementById('breadcrumb').textContent = page.title;
    if (window.IOSchemaPages[page.id]) { drawSchemaPage(page); drawNav(); return; }
    const headings = page.columns.map(c => `<th scope="col">${esc(c)}</th>`).join('');
    const rows = page.rows.map(row => `<tr>${row.map(cell => `<td>${esc(cell)}</td>`).join('')}</tr>`).join('');
    content.innerHTML = `<div class="heading"><div><span class="eyebrow">${esc(page.group)} / صفحهٔ نمونه</span><h1>${esc(page.title)}</h1><p>${esc(page.description)}</p></div><span class="badge">مرحلهٔ طراحی · بدون اتصال</span></div>
      <div class="info-grid"><div class="info-card"><span class="label">بخش سامانه</span><strong class="blue">${esc(page.group)}</strong><small>مسیر نمایشی این صفحه</small></div><div class="info-card"><span class="label">نمونه‌های قابل مشاهده</span><strong>${page.rows.length.toLocaleString('fa-IR')} مورد</strong><small>فقط دادهٔ ساختگی</small></div><div class="info-card"><span class="label">وضعیت اتصال</span><strong>در انتظار اتصال</strong><small>هیچ داده‌ای ثبت نمی‌شود</small></div></div>
      <section class="panel" aria-labelledby="fields-title"><div class="panel-head"><div><h2 id="fields-title">اطلاعات و فیلترها</h2><p>جایگاه ورودی‌های مربوط به این صفحه</p></div><button type="button" disabled>عملیات صفحه</button></div><div class="panel-body"><div class="fields">${page.fields.map(f => `<label>${esc(f)}<input type="text" placeholder="${esc(f)}" disabled></label>`).join('')}</div><div class="actions"><button disabled type="button">اعمال / ثبت</button><button class="secondary" disabled type="button">پاک کردن</button></div></div></section>
      <section class="panel" aria-labelledby="list-title"><div class="panel-head"><div><h2 id="list-title">نمای اطلاعات</h2><p>ساختار نمونه برای ${esc(page.title)}</p></div><span class="badge">دادهٔ ساختگی</span></div><div class="table-wrap"><table><thead><tr>${headings}</tr></thead><tbody>${rows}</tbody></table></div><div class="table-note">این اطلاعات واقعی نیستند و هیچ عملیاتی انجام نمی‌شود.</div></section>
      <p class="source-note">مرجع بررسی صفحهٔ قدیمی: <code>${esc(page.source)}</code> · تطبیق کامل نیازمند بازبینی است.</p>`;
    drawNav();
  }
  toggle.addEventListener('click', () => { const open = sidebar.classList.toggle('open'); toggle.setAttribute('aria-expanded', String(open)); });
  nav.addEventListener('click', e => { if (e.target.closest('a') && window.matchMedia('(max-width: 700px)').matches) { sidebar.classList.remove('open'); toggle.setAttribute('aria-expanded', 'false'); } });
  content.addEventListener('click', event => {
    const tab = event.target.closest('[data-schema]');
    if (!tab) return;
    const page = selected();
    activeSchemas[page.id] = tab.dataset.schema;
    drawSchemaPage(page);
    content.querySelector('[data-schema][aria-pressed="true"]').focus();
  });
  content.addEventListener('input', event => {
    if (event.target.id !== 'schema-search') return;
    const term = event.target.value.trim().toLocaleLowerCase('fa');
    let found = 0;
    for (const row of content.querySelectorAll('tbody tr')) {
      row.hidden = !row.textContent.toLocaleLowerCase('fa').includes(term);
      if (!row.hidden) found++;
    }
    content.querySelector('#schema-empty').hidden = found !== 0;
  });
  search.addEventListener('input', drawNav);
  window.addEventListener('hashchange', () => { drawPage(); document.getElementById('content').focus({preventScroll:true}); window.scrollTo(0,0); });
  fetch('pages.json').then(response => { if (!response.ok) throw Error('catalog unavailable'); return response.json(); }).then(data => { pages = data; if (!pages.length) throw Error('empty catalog'); drawPage(); }).catch(() => { content.textContent = 'بارگذاری فهرست صفحات انجام نشد. صفحه را از طریق وب‌سرور محلی باز کنید و دوباره تلاش کنید.'; });
})();
