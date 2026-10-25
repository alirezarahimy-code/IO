/* ==========================================================================
   TWF Enterprise Core UI Script — 100% Offline
   ========================================================================== */
(function (window, document) {
    'use strict';

    function setCookie(name, value, days) {
        var expires = '';
        if (days) {
            var date = new Date();
            date.setTime(date.getTime() + (days * 24 * 60 * 60 * 1000));
            expires = '; expires=' + date.toUTCString();
        }
        document.cookie = name + '=' + (value || '') + expires + '; path=/; SameSite=Strict';
    }

    function getCookie(name) {
        var nameEQ = name + '=';
        var ca = document.cookie.split(';');
        for (var i = 0; i < ca.length; i++) {
            var c = ca[i];
            while (c.charAt(0) === ' ') c = c.substring(1, c.length);
            if (c.indexOf(nameEQ) === 0) return c.substring(nameEQ.length, c.length);
        }
        return null;
    }

    window.TWFUI = {
        toggleSidebar: function () {
            var sidebar = document.getElementById('twf-sidebar');
            if (!sidebar) return;
            var isCollapsed = sidebar.classList.toggle('collapsed');
            setCookie('sidemenu', (!isCollapsed).toString(), 30);
        },
        toggleSubmenu: function (submenuId) {
            var el = document.getElementById(submenuId);
            if (!el) return;
            el.classList.toggle('open');
        },
        setCookie: setCookie,
        getCookie: getCookie
    };

    document.addEventListener('DOMContentLoaded', function () {
        var toggles = document.querySelectorAll('[data-twf-submenu]');
        for (var i = 0; i < toggles.length; i++) {
            toggles[i].addEventListener('click', function (e) {
                e.preventDefault();
                var targetId = this.getAttribute('data-twf-submenu');
                window.TWFUI.toggleSubmenu(targetId);
            });
        }
    });
})(window, document);

/* ==========================================================================
   ARIA Core — data access, dialog engine and unified request form
   Vanilla ES5 only: no jQuery, no Kendo, no CDN. Works fully offline.
   ========================================================================== */
(function (window, document) {
    'use strict';

    var TWF = window.TWF || {};

    /* ---------- Low-level HTTP helpers ---------- */

    function encodeForm(data) {
        var parts = [];
        for (var key in data) {
            if (!Object.prototype.hasOwnProperty.call(data, key)) continue;
            var value = data[key];
            if (value === null || typeof value === 'undefined') value = '';
            parts.push(encodeURIComponent(key) + '=' + encodeURIComponent(value));
        }
        return parts.join('&');
    }

    function parseJson(text) {
        try {
            return JSON.parse(text);
        } catch (e) {
            return null;
        }
    }

    /**
     * POST a JSON payload and invoke callback(err, parsedResponse).
     * Callback shape matches the legacy XHR call sites in the views.
     */
    TWF.ajaxJson = function (url, data, callback) {
        var xhr = new XMLHttpRequest();
        xhr.open('POST', url, true);
        xhr.setRequestHeader('Content-Type', 'application/json; charset=utf-8');
        xhr.setRequestHeader('X-Requested-With', 'XMLHttpRequest');
        xhr.onreadystatechange = function () {
            if (xhr.readyState !== 4) return;
            if (xhr.status >= 200 && xhr.status < 300) {
                var resp = parseJson(xhr.responseText);
                if (callback) callback(resp === null ? new Error('BAD_JSON') : null, resp);
            } else if (callback) {
                callback(new Error('HTTP_' + xhr.status), null);
            }
        };
        xhr.send(JSON.stringify(data || {}));
    };

    /** POST classic form-encoded data (used by MVC actions binding simple models). */
    TWF.postForm = function (url, data, callback) {
        var xhr = new XMLHttpRequest();
        xhr.open('POST', url, true);
        xhr.setRequestHeader('Content-Type', 'application/x-www-form-urlencoded; charset=UTF-8');
        xhr.setRequestHeader('X-Requested-With', 'XMLHttpRequest');
        xhr.onreadystatechange = function () {
            if (xhr.readyState !== 4) return;
            if (xhr.status >= 200 && xhr.status < 300) {
                if (callback) callback(null, parseJson(xhr.responseText));
            } else if (callback) {
                callback(new Error('HTTP_' + xhr.status), null);
            }
        };
        xhr.send(encodeForm(data || {}));
    };

    /** GET a URL and return raw HTML text to the callback(err, html). */
    TWF.getHtml = function (url, callback) {
        var xhr = new XMLHttpRequest();
        xhr.open('GET', url, true);
        xhr.setRequestHeader('X-Requested-With', 'XMLHttpRequest');
        xhr.onreadystatechange = function () {
            if (xhr.readyState !== 4) return;
            if (xhr.status >= 200 && xhr.status < 300) {
                if (callback) callback(null, xhr.responseText);
            } else if (callback) {
                callback(new Error('HTTP_' + xhr.status), null);
            }
        };
        xhr.send();
    };

    TWF.escapeHtml = function (value) {
        var div = document.createElement('div');
        div.appendChild(document.createTextNode(value === null || typeof value === 'undefined' ? '' : String(value)));
        return div.innerHTML;
    };

    /** Select/deselect every checkbox matching a selector (header row checkbox). */
    TWF.toggleSelectAll = function (source, selector) {
        var boxes = document.querySelectorAll(selector);
        for (var i = 0; i < boxes.length; i++) {
            boxes[i].checked = !!source.checked;
        }
    };

    /* ---------- Modal dialog engine ---------- */

    var dialogHost = null;

    function ensureDialogHost() {
        if (dialogHost && document.body.contains(dialogHost)) return dialogHost;

        dialogHost = document.createElement('div');
        dialogHost.className = 'twf-modal-host';
        dialogHost.setAttribute('role', 'presentation');
        dialogHost.innerHTML =
            '<div class="twf-modal-backdrop" data-twf-close="1"></div>' +
            '<div class="twf-modal" role="dialog" aria-modal="true" aria-labelledby="twfModalTitle">' +
            '  <div class="twf-modal-header">' +
            '    <h3 class="twf-modal-title" id="twfModalTitle"></h3>' +
            '    <button type="button" class="twf-modal-close" aria-label="بستن" data-twf-close="1">&times;</button>' +
            '  </div>' +
            '  <div class="twf-modal-body"></div>' +
            '  <div class="twf-modal-footer"></div>' +
            '</div>';
        document.body.appendChild(dialogHost);

        dialogHost.addEventListener('click', function (e) {
            var target = e.target;
            if (target && target.getAttribute && target.getAttribute('data-twf-close')) {
                TWF.closeModal();
            }
        });

        document.addEventListener('keydown', function (e) {
            if (e.keyCode === 27) TWF.closeModal();
        });

        return dialogHost;
    }

    TWF.openModal = function (title, bodyHtml, footerHtml) {
        var host = ensureDialogHost();
        host.querySelector('.twf-modal-title').textContent = title || '';
        host.querySelector('.twf-modal-body').innerHTML = bodyHtml || '';
        host.querySelector('.twf-modal-footer').innerHTML = footerHtml || '';
        host.classList.add('open');
        document.body.classList.add('twf-modal-open');
        return host;
    };

    TWF.closeModal = function () {
        if (!dialogHost) return;
        dialogHost.classList.remove('open');
        document.body.classList.remove('twf-modal-open');
        var body = dialogHost.querySelector('.twf-modal-body');
        if (body) body.innerHTML = '';
    };

    /* ---------- Unified request form (dialog) ---------- */

    function setValue(id, value) {
        var el = document.getElementById(id);
        if (el) el.value = value === null || typeof value === 'undefined' ? '' : value;
    }

    function setInputByName(name, value) {
        var el = document.querySelector('#twfRequestFormHost [name="' + name + '"]');
        if (el) el.value = value === null || typeof value === 'undefined' ? '' : value;
    }

    /** Filter the leave/duty type list to the types valid for the chosen request type. */
    window.showValidTypes = function (requestType, selectedTypeId, requestTitle) {
        requestType = parseInt(requestType, 10);
        var select = document.getElementById('LeaveTypeName');
        var typeGroup = document.getElementById('typeGroup');
        if (!select) return;

        var visible = 0;
        for (var i = 0; i < select.options.length; i++) {
            var opt = select.options[i];
            var parent = parseInt(opt.getAttribute('data-parent'), 10);
            var matches = (parent === requestType) ||
                (requestType === 7 && parent === 7) ||
                (requestType === 5 || requestType === 6 || requestType === 4);
            opt.hidden = !matches;
            opt.disabled = !matches;
            if (matches) {
                visible++;
                if (selectedTypeId && parseInt(opt.value, 10) === parseInt(selectedTypeId, 10)) {
                    select.value = opt.value;
                }
            }
        }

        if (typeGroup) {
            typeGroup.style.display = visible > 0 ? '' : 'none';
        }
        if (requestTitle) {
            var title = document.getElementById('twfModalTitle');
            if (title) title.textContent = requestTitle;
        }
    };

    /** Kept global because the shared partial calls it inline: onchange="onRequestTypeSelect(this)". */
    window.onRequestTypeSelect = function (select) {
        setValue('requestTypeID', select.value);
        window.showValidTypes(select.value, null, null);
    };

    window.showRequest = function (RequestType, LeaveTypeID, FromDateHijri, ToDateHijri, ForDateHijri,
                                   FromTime, ToTime, ForTime, ReqName) {
        TWF.openModal(ReqName || 'ثبت درخواست', '<div id="twfRequestFormHost" class="twf-modal-loading">در حال بارگذاری فرم درخواست...</div>',
            '<button type="button" class="twf-btn twf-btn-secondary" data-twf-close="1">انصراف</button>' +
            '<button type="button" class="twf-btn twf-btn-primary" id="twfSendRequestBtn">ثبت درخواست</button>');

        TWF.getHtml('/Request/Form', function (err, html) {
            var host = document.getElementById('twfRequestFormHost');
            if (!host) return;
            if (err || !html) {
                host.innerHTML = '<div class="twf-alert twf-alert-danger">بارگذاری فرم درخواست انجام نشد. دوباره تلاش کنید.</div>';
                return;
            }

            host.className = '';
            host.innerHTML = html;

            setValue('requestTypeID', RequestType);
            setValue('selectedName', RequestType);
            setValue('LeaveTypeID', LeaveTypeID);

            if (ForDateHijri) {
                setInputByName('ToDateHijri', ToDateHijri || ForDateHijri);
                setInputByName('FromDateHijri', FromDateHijri || ForDateHijri);
                setInputByName('ForDateHijri', ForDateHijri);
            } else {
                setInputByName('ForDateHijri', FromDateHijri || '');
                setInputByName('FromDateHijri', FromDateHijri || '');
                setInputByName('ToDateHijri', ToDateHijri || '');
            }

            setInputByName('FromTime', FromTime || '');
            setInputByName('ToTime', ToTime || '');
            setInputByName('ForTime', ForTime || '');

            window.showValidTypes(RequestType, LeaveTypeID, ReqName);

            var sendBtn = document.getElementById('twfSendRequestBtn');
            if (sendBtn) sendBtn.onclick = window.sendRequest;
        });
    };

    function currentValue(id) {
        var el = document.getElementById(id);
        return el ? el.value : '';
    }

    function currentInput(name) {
        var el = document.querySelector('#twfRequestFormHost [name="' + name + '"]');
        return el ? el.value : '';
    }

    window.sendRequest = function () {
        var button = document.getElementById('twfSendRequestBtn');
        if (button) {
            button.disabled = true;
            button.textContent = 'در حال ثبت...';
        }

        var payload = {
            IsNew: true,
            RequestType: currentValue('requestTypeID'),
            LeaveTypeID: currentValue('LeaveTypeID'),
            LeaveDescriptionsID: currentValue('LeaveDescID') || -1,
            Descriptions: currentInput('Descriptions'),
            FromDateHijri: currentInput('FromDateHijri'),
            ToDateHijri: currentInput('ToDateHijri'),
            ForDateHijri: currentInput('ForDateHijri'),
            FromTime: currentInput('FromTime'),
            ToTime: currentInput('ToTime'),
            ForTime: currentInput('ForTime'),
            OvertimeValue: currentInput('OvertimeValue')
        };

        TWF.ajaxJson('/Request/GeneralRequestWeb', payload, function (err, resp) {
            var host = document.getElementById('twfRequestFormHost');
            var message = resp && resp.Message ? resp.Message : '';
            var okMessage = 'اطلاعات با موفقیت ثبت شد';

            if (button) {
                button.disabled = false;
                button.textContent = 'ثبت درخواست';
            }

            if (err) {
                if (host) {
                    host.insertAdjacentHTML('afterbegin',
                        '<div class="twf-alert twf-alert-danger">ارتباط با سرور برقرار نشد.</div>');
                }
                return;
            }

            if (host) {
                host.insertAdjacentHTML('afterbegin',
                    '<div class="twf-alert ' + (message === okMessage ? 'twf-alert-success' : 'twf-alert-danger') + '">' +
                    TWF.escapeHtml(message) + '</div>');
            }

            if (message === okMessage) {
                window.setTimeout(function () {
                    TWF.closeModal();
                    var monthOffset = 0;
                    if (typeof window.twfLoadDailyDetailsReport === 'function') {
                        window.twfLoadDailyDetailsReport(monthOffset);
                    }
                }, 1400);
            }
        });
    };

    window.TWF = TWF;
})(window, document);
