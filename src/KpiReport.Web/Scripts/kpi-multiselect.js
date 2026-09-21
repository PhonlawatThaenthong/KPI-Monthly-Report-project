/* ============================================================
   HR KPI System — multi-select dropdown
   ------------------------------------------------------------
   Turns every <select multiple> into a dropdown with checkboxes.
   The native <select> is kept in the DOM (visually hidden) and is
   still the single source of truth for the posted value, so no
   controller / model binding changes are needed.

   Opt out on a single control with data-kpi-ms="off".
   Custom empty-state text with data-kpi-ms-placeholder="...".
   Styles: ~/Content/kpi-multiselect.css
   ============================================================ */
(function () {
    'use strict';

    var TEXT = {
        placeholder: '— เลือก —',
        search: 'ค้นหา…',
        selectAll: 'ทั้งหมด',
        clear: 'ล้าง',
        noMatch: 'ไม่พบตัวเลือกที่ตรงกับคำค้น',
        countSuffix: ' รายการ'
    };

    var SEARCH_THRESHOLD = 8;  // show the search box from this many options up
    var seq = 0;

    function summarize(select, labelEl, countEl) {
        var chosen = [];
        for (var i = 0; i < select.options.length; i++) {
            if (select.options[i].selected) {
                chosen.push(select.options[i].text.trim());
            }
        }

        if (chosen.length === 0) {
            labelEl.textContent = select.getAttribute('data-kpi-ms-placeholder') || TEXT.placeholder;
            labelEl.classList.add('is-placeholder');
            countEl.hidden = true;
            return;
        }

        labelEl.classList.remove('is-placeholder');
        labelEl.textContent = chosen.length <= 2
            ? chosen.join(', ')
            : chosen.length + TEXT.countSuffix;
        labelEl.title = chosen.join(', ');
        countEl.hidden = chosen.length <= 2;
        countEl.textContent = chosen.length;
    }

    function enhance(select) {
        if (select.getAttribute('data-kpi-ms') === 'off' || select.dataset.kpiMsReady) {
            return;
        }
        select.dataset.kpiMsReady = '1';

        var id = 'kpiMs' + (++seq);
        var options = Array.prototype.slice.call(select.options);

        var wrap = document.createElement('div');
        wrap.className = 'kpi-ms';

        var toggle = document.createElement('button');
        toggle.type = 'button';
        toggle.className = 'kpi-ms-toggle';
        toggle.id = id + 'Toggle';
        toggle.setAttribute('aria-haspopup', 'listbox');
        toggle.setAttribute('aria-expanded', 'false');
        if (select.disabled) { toggle.disabled = true; }

        var labelEl = document.createElement('span');
        labelEl.className = 'kpi-ms-label';
        var countEl = document.createElement('span');
        countEl.className = 'kpi-ms-count';
        countEl.hidden = true;
        var caret = document.createElement('span');
        caret.className = 'kpi-ms-caret';
        caret.setAttribute('aria-hidden', 'true');
        toggle.appendChild(labelEl);
        toggle.appendChild(countEl);
        toggle.appendChild(caret);

        var panel = document.createElement('div');
        panel.className = 'kpi-ms-panel';
        panel.id = id + 'Panel';

        var tools = document.createElement('div');
        tools.className = 'kpi-ms-tools';

        var search = null;
        if (options.length >= SEARCH_THRESHOLD) {
            search = document.createElement('input');
            search.type = 'search';
            search.className = 'kpi-ms-search';
            search.placeholder = TEXT.search;
            search.setAttribute('aria-label', TEXT.search);
            tools.appendChild(search);
        }

        var allBtn = document.createElement('button');
        allBtn.type = 'button';
        allBtn.className = 'kpi-ms-action';
        allBtn.textContent = TEXT.selectAll;

        var clearBtn = document.createElement('button');
        clearBtn.type = 'button';
        clearBtn.className = 'kpi-ms-action';
        clearBtn.textContent = TEXT.clear;

        tools.appendChild(allBtn);
        tools.appendChild(clearBtn);
        panel.appendChild(tools);

        var list = document.createElement('div');
        list.className = 'kpi-ms-list';
        list.setAttribute('role', 'group');
        panel.appendChild(list);

        var empty = document.createElement('p');
        empty.className = 'kpi-ms-empty';
        empty.textContent = TEXT.noMatch;
        empty.hidden = true;
        panel.appendChild(empty);

        var rows = options.map(function (opt, index) {
            var row = document.createElement('label');
            row.className = 'kpi-ms-option';

            var box = document.createElement('input');
            box.type = 'checkbox';
            box.checked = opt.selected;
            box.disabled = opt.disabled;
            box.value = opt.value;

            var text = document.createElement('span');
            text.className = 'kpi-ms-option-text';
            text.textContent = opt.text.trim();

            row.appendChild(box);
            row.appendChild(text);
            list.appendChild(row);

            box.addEventListener('change', function () {
                select.options[index].selected = box.checked;
                summarize(select, labelEl, countEl);
                select.dispatchEvent(new Event('change', { bubbles: true }));
            });

            return { row: row, box: box, index: index, text: text.textContent.toLowerCase() };
        });

        function setAll(state) {
            rows.forEach(function (r) {
                if (r.box.disabled || r.row.classList.contains('is-hidden')) { return; }
                r.box.checked = state;
                select.options[r.index].selected = state;
            });
            summarize(select, labelEl, countEl);
            select.dispatchEvent(new Event('change', { bubbles: true }));
        }

        allBtn.addEventListener('click', function () { setAll(true); });
        clearBtn.addEventListener('click', function () { setAll(false); });

        if (search) {
            search.addEventListener('input', function () {
                var q = search.value.trim().toLowerCase();
                var shown = 0;
                rows.forEach(function (r) {
                    var hit = !q || r.text.indexOf(q) !== -1;
                    r.row.classList.toggle('is-hidden', !hit);
                    if (hit) { shown++; }
                });
                empty.hidden = shown !== 0;
            });
        }

        // a panel inside a table wrapper (overflow:hidden) would be clipped,
        // so in that case it is positioned against the viewport instead.
        function isClipped() {
            var node = wrap.parentNode;
            while (node && node.nodeType === 1 && node !== document.body) {
                var style = window.getComputedStyle(node);
                if (style.overflow !== 'visible' || style.overflowX !== 'visible' || style.overflowY !== 'visible') {
                    return true;
                }
                node = node.parentNode;
            }
            return false;
        }

        function place() {
            var rect = toggle.getBoundingClientRect();
            var flipUp = window.innerHeight - rect.bottom < panel.offsetHeight + 12
                      && rect.top > panel.offsetHeight + 12;
            wrap.classList.toggle('is-flipped', flipUp);

            if (!wrap.classList.contains('is-fixed')) { return; }
            panel.style.minWidth = rect.width + 'px';
            panel.style.left = Math.min(rect.left, window.innerWidth - panel.offsetWidth - 8) + 'px';
            panel.style.top = flipUp ? (rect.top - panel.offsetHeight - 4) + 'px' : (rect.bottom + 4) + 'px';
        }

        function open() {
            wrap.classList.toggle('is-fixed', isClipped());
            wrap.classList.add('is-open');
            toggle.setAttribute('aria-expanded', 'true');
            place();
            window.addEventListener('scroll', place, true);
            window.addEventListener('resize', place);
            if (search) { search.focus(); }
        }

        function close() {
            wrap.classList.remove('is-open', 'is-flipped');
            toggle.setAttribute('aria-expanded', 'false');
            window.removeEventListener('scroll', place, true);
            window.removeEventListener('resize', place);
        }

        toggle.addEventListener('click', function () {
            if (wrap.classList.contains('is-open')) { close(); } else { open(); }
        });

        wrap.addEventListener('keydown', function (e) {
            if (e.key === 'Escape' && wrap.classList.contains('is-open')) {
                close();
                toggle.focus();
            }
        });

        document.addEventListener('click', function (e) {
            if (!wrap.contains(e.target)) { close(); }
        });

        // keep the widget in sync if server-side / other script changes the select
        select.addEventListener('change', function (e) {
            if (e.detail === 'kpi-ms') { return; }
            rows.forEach(function (r) { r.box.checked = select.options[r.index].selected; });
            summarize(select, labelEl, countEl);
        });

        select.parentNode.insertBefore(wrap, select);
        wrap.appendChild(toggle);
        wrap.appendChild(panel);
        wrap.appendChild(select);

        select.classList.add('kpi-ms-native');
        select.setAttribute('tabindex', '-1');
        select.setAttribute('aria-hidden', 'true');
        select.removeAttribute('size');

        // carry the original <label for="..."> over to the new trigger
        var srcLabel = select.id ? document.querySelector('label[for="' + select.id + '"]') : null;
        if (srcLabel) {
            toggle.setAttribute('aria-labelledby', srcLabel.id || (srcLabel.id = id + 'Label'));
        } else if (select.getAttribute('aria-label')) {
            toggle.setAttribute('aria-label', select.getAttribute('aria-label'));
        }

        summarize(select, labelEl, countEl);
    }

    function init(root) {
        var scope = root || document;
        Array.prototype.forEach.call(
            scope.querySelectorAll('select[multiple]'),
            enhance
        );
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () { init(); });
    } else {
        init();
    }

    window.kpiMultiSelect = { init: init, enhance: enhance };
}());
