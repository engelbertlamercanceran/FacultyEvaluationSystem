// Table pagination for every table in the system.
//
// Any <table class="table"> with more rows than one page gets a pager below it:
// "Showing 1–10 of 57", a rows-per-page choice and page buttons. It works on the
// rows already in the page, so no controller has to change. Opt out with
// data-no-paginate (tables paged on the server, or forms such as the evaluation
// form whose rows must all stay visible). data-page-size="25" sets the default size.
//
// <input data-table-filter="<css selector>"> filters the rows of the matching
// tables as the user types, and the pager follows the filtered rows.
(function () {
    'use strict';

    var SIZES = [10, 25, 50, 100];

    function paginate(table) {
        var body = table.tBodies[0];
        if (!body) return;

        var size = parseInt(table.dataset.pageSize, 10) || SIZES[0];
        var page = 1;

        var pager = document.createElement('div');
        pager.className = 'table-pager d-flex justify-content-between align-items-center flex-wrap gap-2 px-3 py-2 border-top small';
        table.insertAdjacentElement('afterend', pager);

        function rows() {
            // Rows hidden by a search filter are not counted
            return Array.from(body.rows).filter(function (r) { return !r.classList.contains('filtered-out'); });
        }

        function render() {
            var all = Array.from(body.rows);
            var visible = rows();
            var pages = Math.max(1, Math.ceil(visible.length / size));
            page = Math.min(Math.max(1, page), pages);

            all.forEach(function (r) { r.style.display = 'none'; });
            visible.slice((page - 1) * size, page * size).forEach(function (r) { r.style.display = ''; });

            // A table that fits on the smallest page needs no pager
            if (visible.length <= SIZES[0]) { pager.style.display = 'none'; visible.forEach(function (r) { r.style.display = ''; }); return; }
            pager.style.display = '';

            var from = (page - 1) * size + 1, to = Math.min(page * size, visible.length);
            var html = '<div class="text-muted">Showing ' + from + '&ndash;' + to + ' of ' + visible.length +
                ' &middot; <label class="mb-0">Rows <select class="form-select form-select-sm d-inline-block w-auto py-0" data-size>';
            SIZES.forEach(function (s) { html += '<option' + (s === size ? ' selected' : '') + '>' + s + '</option>'; });
            html += '</select></label></div><ul class="pagination pagination-sm mb-0 flex-wrap">';

            function item(label, target, disabled, active) {
                return '<li class="page-item' + (disabled ? ' disabled' : '') + (active ? ' active' : '') + '">' +
                    '<button type="button" class="page-link" data-page="' + target + '">' + label + '</button></li>';
            }

            html += item('Previous', page - 1, page === 1, false);
            for (var p = Math.max(1, page - 3); p <= Math.min(pages, page + 3); p++) html += item(p, p, false, p === page);
            html += item('Next', page + 1, page === pages, false);
            pager.innerHTML = html + '</ul>';
        }

        pager.addEventListener('click', function (e) {
            var btn = e.target.closest('[data-page]');
            if (!btn || btn.parentElement.classList.contains('disabled')) return;
            page = parseInt(btn.dataset.page, 10);
            render();
        });
        pager.addEventListener('change', function (e) {
            if (!e.target.matches('[data-size]')) return;
            size = parseInt(e.target.value, 10);
            page = 1;
            render();
        });

        table.refreshPager = function () { page = 1; render(); };
        render();
    }

    function filter(input) {
        var tables = document.querySelectorAll(input.dataset.tableFilter);
        input.addEventListener('input', function () {
            var words = input.value.toLowerCase().split(/\s+/).filter(Boolean);
            tables.forEach(function (table) {
                Array.from(table.tBodies[0] ? table.tBodies[0].rows : []).forEach(function (r) {
                    var text = r.textContent.toLowerCase();
                    var match = words.every(function (w) { return text.indexOf(w) >= 0; });
                    r.classList.toggle('filtered-out', !match);
                    if (!table.refreshPager) r.style.display = match ? '' : 'none';
                });
                if (table.refreshPager) table.refreshPager();
            });
        });
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('table.table:not([data-no-paginate])').forEach(paginate);
        document.querySelectorAll('[data-table-filter]').forEach(filter);
    });
})();
