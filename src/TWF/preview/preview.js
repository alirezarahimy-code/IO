/* ==========================================================================
   ARIA preview harness — offline demo wiring for the rewritten views.
   No external calls: every interaction is simulated in the browser.
   ========================================================================== */
(function (window, document) {
    'use strict';

    /* ---------- Section routing ---------- */

    window.showSection = function (id) {
        var sections = document.querySelectorAll('.twf-section');
        for (var i = 0; i < sections.length; i++) {
            sections[i].classList.toggle('active', sections[i].id === id);
        }
        var links = document.querySelectorAll('.twf-nav-link');
        for (var j = 0; j < links.length; j++) {
            links[j].classList.toggle('active', links[j].getAttribute('data-section') === id);
        }
        window.scrollTo(0, 0);
        if (id === 'selectArea') { areaRedraw(); }
        if (id === 'locationReport') { locationRedraw(); }
        if (window.location.hash !== '#' + id) {
            history.replaceState(null, '', '#' + id);
        }
        return false;
    };

    document.addEventListener('click', function (e) {
        var link = e.target.closest ? e.target.closest('[data-section]') : null;
        if (!link) return;
        e.preventDefault();
        window.showSection(link.getAttribute('data-section'));
    });

    /* ---------- Toast (industrial, no library) ---------- */

    window.previewToast = function (message, isError) {
        var host = document.getElementById('twfToastHost');
        if (!host) {
            host = document.createElement('div');
            host.id = 'twfToastHost';
            host.style.cssText = 'position:fixed;bottom:20px;left:20px;z-index:3000;display:flex;flex-direction:column;gap:8px;';
            document.body.appendChild(host);
        }

        var toast = document.createElement('div');
        toast.className = 'twf-alert ' + (isError ? 'twf-alert-danger' : 'twf-alert-success');
        toast.style.cssText = 'margin:0;min-width:280px;box-shadow:0 10px 20px rgba(15,23,42,.18);';
        toast.textContent = message;
        host.appendChild(toast);

        window.setTimeout(function () {
            toast.style.opacity = '0';
            toast.style.transition = 'opacity .3s ease';
            window.setTimeout(function () { host.removeChild(toast); }, 320);
        }, 2600);
    };

    /* ---------- Request dialog (uses the real modal engine) ---------- */

    var requestTypeLabels = {
        0: 'مرخصی روزانه', 1: 'مرخصی ساعتی', 2: 'ماموریت روزانه', 3: 'ماموریت ساعتی',
        4: 'ثبت تردد فراموش‌شده', 5: 'مجوز تاخیر', 6: 'مجوز تعجیل', 7: 'مجوز اضافه‌کار'
    };

    window.openRequestDialog = function (requestType, leaveTypeId, fromDate, toDate, forDate, forTime) {
        requestType = parseInt(requestType || 0, 10);
        var label = requestTypeLabels[requestType] || 'ثبت درخواست';
        var isDaily = requestType === 0 || requestType === 2;

        var body =
            '<div class="twf-alert twf-alert-info">درخواست برای کاربر جاری <strong>رضا کاظمی (۲۰۰)</strong> ثبت می‌شود.</div>' +
            '<div class="twf-grid-2">' +
            '  <div class="twf-field"><label class="twf-label">نوع درخواست</label>' +
            '    <select class="twf-select" id="pvReqType">' + options(requestType) + '</select></div>' +
            '  <div class="twf-field"><label class="twf-label">نوع مرخصی / ماموریت</label>' +
            '    <select class="twf-select"><option>استحقاقی</option><option>استعلاجی</option><option>ماموریت اداری</option></select></div>' +
            '  <div class="twf-field"><label class="twf-label">' + (isDaily ? 'از تاریخ / تا تاریخ' : 'تاریخ') + '</label>' +
            '    <input type="text" class="twf-input twf-num" dir="ltr" id="pvFromDate" value="' + (fromDate || '1404/08/03') + '" /></div>' +
            (isDaily
                ? '  <div class="twf-field"><label class="twf-label">تا تاریخ</label>' +
                  '    <input type="text" class="twf-input twf-num" dir="ltr" id="pvToDate" value="' + (toDate || fromDate || '1404/08/03') + '" /></div>'
                : '  <div class="twf-field"><label class="twf-label">ساعت</label>' +
                  '    <input type="text" class="twf-input twf-num" dir="ltr" value="' + (forTime || '08:00') + '" /></div>') +
            '  <div class="twf-field" style="grid-column:1 / -1;"><label class="twf-label">توضیحات</label>' +
            '    <input type="text" class="twf-input" id="pvDesc" placeholder="توضیح درخواست (الزامی در برخی قوانین)" /></div>' +
            '</div>';

        var footer =
            '<button type="button" class="twf-btn twf-btn-secondary" data-twf-close="1">انصراف</button>' +
            '<button type="button" class="twf-btn twf-btn-primary" id="pvSubmitRequest">ثبت درخواست</button>';

        window.TWF.openModal(label, body, footer);

        document.getElementById('pvSubmitRequest').onclick = function () {
            var desc = document.getElementById('pvDesc').value.trim();
            if (desc.length > 0 && desc.length < 3) {
                window.previewToast('توضیحات معتبر نمی‌باشد.', true);
                return;
            }
            window.TWF.closeModal();
            window.previewToast('درخواست ثبت شد و به کارتابل مدیر فوری ارسال گردید.');
        };
    };

    function options(selected) {
        var html = '';
        for (var key in requestTypeLabels) {
            if (!Object.prototype.hasOwnProperty.call(requestTypeLabels, key)) continue;
            html += '<option value="' + key + '"' + (parseInt(key, 10) === selected ? ' selected' : '') + '>' +
                requestTypeLabels[key] + '</option>';
        }
        return html;
    }

    window.previewToggleFields = function () { /* field visibility handled server-side in the real view */ };

    window.previewSubmitRequest = function () {
        window.previewToast('درخواست ثبت شد و پس از تایید مسئول مستقیم در T5 اعمال می‌شود.');
    };

    /* ---------- Cartable decisions ---------- */

    window.previewDecide = function (button, accepted) {
        var row = button.closest('tr');
        var actionsCell = row.cells[row.cells.length - 1];
        actionsCell.innerHTML = accepted
            ? '<span class="twf-badge twf-badge-success">تایید شد — مرحله بعد ارسال شد</span>'
            : '<span class="twf-badge twf-badge-danger">رد شد — به درخواست‌دهنده اعلام شد</span>';
        row.cells[0].innerHTML = '';
        window.previewToast(accepted ? 'درخواست تایید شد.' : 'درخواست رد شد.', !accepted);
    };

    window.previewBulk = function (accepted) {
        var rows = document.querySelectorAll('#cartableBody tr');
        var touched = 0;
        for (var i = 0; i < rows.length; i++) {
            var check = rows[i].querySelector('.twf-row-chk');
            if (!check || !check.checked) continue;
            var buttons = rows[i].querySelectorAll('button');
            if (buttons.length === 0) continue;
            window.previewDecide(buttons[0], accepted);
            touched++;
        }
        window.previewToast(touched > 0
            ? touched + ' درخواست انتخاب‌شده پردازش شد.'
            : 'هیچ درخواستی انتخاب نشده است.', touched === 0);
    };

    /* ---------- Personnel status filter tabs ---------- */

    document.addEventListener('click', function (e) {
        var tab = e.target.closest ? e.target.closest('.twf-tab') : null;
        if (!tab) return;
        var tabs = tab.parentNode.querySelectorAll('.twf-tab');
        for (var i = 0; i < tabs.length; i++) tabs[i].classList.remove('active');
        tab.classList.add('active');
        window.previewToast('فیلتر «' + tab.textContent.trim() + '» اعمال شد.');
    });

    /* ---------- Offline vector canvases ---------- */

    function fitCanvas(canvas) {
        var ratio = window.devicePixelRatio || 1;
        var rect = canvas.getBoundingClientRect();
        if (rect.width === 0) return;
        canvas.width = Math.round(rect.width * ratio);
        canvas.height = Math.round(rect.height * ratio);
        var ctx = canvas.getContext('2d');
        ctx.setTransform(ratio, 0, 0, ratio, 0, 0);
        canvas._cssWidth = rect.width;
        canvas._cssHeight = rect.height;
    }

    function drawGrid(ctx, w, h) {
        ctx.clearRect(0, 0, w, h);
        ctx.strokeStyle = 'rgba(51,65,85,0.55)';
        ctx.lineWidth = 1;
        for (var x = 0; x < w; x += 28) {
            ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, h); ctx.stroke();
        }
        for (var y = 0; y < h; y += 28) {
            ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(w, y); ctx.stroke();
        }
    }

    /* Location report: fixed offline sample points + trail */
    var locationPoints = [
        { x: 0.18, y: 0.62, label: '۳۱۲' }, { x: 0.22, y: 0.58, label: '۳۱۲' },
        { x: 0.27, y: 0.54, label: '۳۱۲' }, { x: 0.34, y: 0.51, label: '۳۱۸' },
        { x: 0.41, y: 0.47, label: '۳۱۸' }, { x: 0.48, y: 0.44, label: '۳۳۰' },
        { x: 0.55, y: 0.40, label: '۳۳۰' }, { x: 0.63, y: 0.35, label: '۳۴۱' },
        { x: 0.71, y: 0.31, label: '۳۴۱' }, { x: 0.78, y: 0.27, label: '۳۴۱' }
    ];

    window.locationRedraw = function () {
        var canvas = document.getElementById('locationCanvas');
        if (!canvas) return;
        fitCanvas(canvas);
        var ctx = canvas.getContext('2d');
        var w = canvas._cssWidth, h = canvas._cssHeight;
        drawGrid(ctx, w, h);

        // allowed area polygon
        ctx.beginPath();
        ctx.moveTo(w * 0.12, h * 0.78);
        ctx.lineTo(w * 0.34, h * 0.20);
        ctx.lineTo(w * 0.86, h * 0.24);
        ctx.lineTo(w * 0.90, h * 0.80);
        ctx.closePath();
        ctx.fillStyle = 'rgba(29,78,216,0.16)';
        ctx.fill();
        ctx.strokeStyle = 'rgba(147,197,253,0.85)';
        ctx.lineWidth = 1.5;
        ctx.stroke();

        // track
        ctx.beginPath();
        for (var i = 0; i < locationPoints.length; i++) {
            var p = locationPoints[i];
            if (i === 0) ctx.moveTo(p.x * w, p.y * h);
            else ctx.lineTo(p.x * w, p.y * h);
        }
        ctx.strokeStyle = '#f59e0b';
        ctx.lineWidth = 2;
        ctx.stroke();

        // points
        for (var j = 0; j < locationPoints.length; j++) {
            var pt = locationPoints[j];
            ctx.beginPath();
            ctx.arc(pt.x * w, pt.y * h, 4.5, 0, Math.PI * 2);
            ctx.fillStyle = '#38bdf8';
            ctx.fill();
            ctx.strokeStyle = '#0f172a';
            ctx.lineWidth = 1.5;
            ctx.stroke();

            ctx.fillStyle = '#cbd5e1';
            ctx.font = '11px Tahoma';
            ctx.fillText(pt.label, pt.x * w + 8, pt.y * h - 6);
        }

        ctx.fillStyle = '#94a3b8';
        ctx.font = '11px Tahoma';
        ctx.fillText('محدوده مجاز: محوطه اصلی کارخانه', 12, 20);
    };

    /* Area editor */
    var areaPoints = [];

    window.areaRedraw = function () {
        var canvas = document.getElementById('areaCanvas');
        if (!canvas) return;
        fitCanvas(canvas);
        var ctx = canvas.getContext('2d');
        var w = canvas._cssWidth, h = canvas._cssHeight;
        drawGrid(ctx, w, h);

        if (areaPoints.length > 1) {
            ctx.beginPath();
            ctx.moveTo(areaPoints[0].x, areaPoints[0].y);
            for (var i = 1; i < areaPoints.length; i++) ctx.lineTo(areaPoints[i].x, areaPoints[i].y);
            if (areaPoints.length > 2) ctx.closePath();
            ctx.fillStyle = 'rgba(34,197,94,0.18)';
            if (areaPoints.length > 2) ctx.fill();
            ctx.strokeStyle = '#4ade80';
            ctx.lineWidth = 2;
            ctx.stroke();
        }

        for (var j = 0; j < areaPoints.length; j++) {
            var p = areaPoints[j];
            ctx.beginPath();
            ctx.arc(p.x, p.y, 5, 0, Math.PI * 2);
            ctx.fillStyle = '#4ade80';
            ctx.fill();
            ctx.strokeStyle = '#0f172a';
            ctx.lineWidth = 1.5;
            ctx.stroke();
            ctx.fillStyle = '#e2e8f0';
            ctx.font = '11px Tahoma';
            ctx.fillText(String(j + 1), p.x + 8, p.y - 6);
        }

        ctx.fillStyle = '#94a3b8';
        ctx.font = '11px Tahoma';
        ctx.fillText(areaPoints.length === 0 ? 'برای افزودن راس روی بوم کلیک کنید' : 'رئوس محدوده جدید', 12, 20);
    };

    function toPersianDigits(value) {
        return String(value).replace(/\d/g, function (d) { return '۰۱۲۳۴۵۶۷۸۹'[parseInt(d, 10)]; });
    }

    window.areaSave = function () {
        if (areaPoints.length < 3) {
            window.previewToast('برای ذخیره محدوده حداقل ۳ راس لازم است.', true);
            return;
        }
        window.previewToast('محدوده با ' + areaPoints.length + ' راس در جدول AreaDA ذخیره شد.');
    };

    window.areaClear = function () {
        areaPoints = [];
        refreshAreaTable();
        window.areaRedraw();
    };

    window.areaUndo = function () {
        areaPoints.pop();
        refreshAreaTable();
        window.areaRedraw();
    };

    function refreshAreaTable() {
        var body = document.getElementById('areaPointsBody');
        var counter = document.getElementById('areaPointCount');
        if (counter) {
            counter.textContent = toPersianDigits(areaPoints.length);
        }
        if (!body) return;

        if (areaPoints.length === 0) {
            body.innerHTML = '<tr><td colspan="3" class="twf-muted" style="text-align:center;">هنوز راسی ثبت نشده است.</td></tr>';
            return;
        }

        var html = '';
        for (var i = 0; i < areaPoints.length; i++) {
            var p = areaPoints[i];
            html += '<tr><td class="twf-num">' + (i + 1) + '</td>' +
                '<td class="twf-num" dir="ltr">' + (35.700 + p.y / 100000).toFixed(6) + '</td>' +
                '<td class="twf-num" dir="ltr">' + (51.350 + p.x / 100000).toFixed(6) + '</td></tr>';
        }
        body.innerHTML = html;
    }

    document.addEventListener('DOMContentLoaded', function () {
        var areaCanvas = document.getElementById('areaCanvas');
        if (areaCanvas) {
            areaCanvas.addEventListener('click', function (e) {
                var rect = areaCanvas.getBoundingClientRect();
                areaPoints.push({ x: e.clientX - rect.left, y: e.clientY - rect.top });
                refreshAreaTable();
                window.areaRedraw();
            });
        }

        // clock
        var clock = document.getElementById('twfClock');
        function renderClock() {
            if (!clock) return;
            var now = new Date();
            var pad = function (n) { return n < 10 ? '0' + n : String(n); };
            clock.textContent = pad(now.getHours()) + ':' + pad(now.getMinutes()) + ':' + pad(now.getSeconds());
        }
        renderClock();
        window.setInterval(renderClock, 1000);

        // deep link
        var initial = (window.location.hash || '').replace('#', '');
        if (initial) window.showSection(initial);
        window.locationRedraw();
        window.areaRedraw();
    });

    window.addEventListener('resize', function () {
        window.locationRedraw();
        window.areaRedraw();
    });
})(window, document);
