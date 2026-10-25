/* ===========================================================================
   T5Web Admin — the only script in the portal.
   Loaded as a same-origin file so the Content-Security-Policy can stay
   "script-src 'self'" (no inline handlers, no external file).
   =========================================================================== */
(function () {
    'use strict';

    // Selects marked data-autosubmit submit their form on change.
    document.addEventListener('change', function (event) {
        var target = event.target;
        if (!target || target.tagName !== 'SELECT' || target.getAttribute('data-autosubmit') !== 'true') {
            return;
        }
        if (target.form) {
            target.form.submit();
        }
    });

    // Buttons marked data-print open the browser print dialog. Done here rather than as
    // an inline onclick so the CSP can keep "script-src 'self'" (photo sheet, docs/10).
    document.addEventListener('click', function (event) {
        var target = event.target;
        if (!target || target.getAttribute('data-print') !== 'true') {
            return;
        }
        event.preventDefault();
        window.print();
    });

    // Destructive-looking actions ask once, in the operator's language.
    document.addEventListener('submit', function (event) {
        var form = event.target;
        if (!form || form.getAttribute('data-confirm') !== 'true') {
            return;
        }
        if (!window.confirm('از ثبت این تغییر مطمئن هستید؟')) {
            event.preventDefault();
        }
    });
}());
