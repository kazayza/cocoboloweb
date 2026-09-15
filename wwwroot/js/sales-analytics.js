// ═══════════════════════════════════════════════════════════
// 📈 Sales Analytics Dashboard — ApexCharts renderers
// ═══════════════════════════════════════════════════════════
(function () {
    'use strict';

    var charts = {};

    function bidi(text) {
        return '\u2067' + text + '\u2069';
    }

    function fmt(n) {
        var neg = n < 0 ? '-' : '';
        n = Math.abs(Math.round(n));
        return neg + n.toString().replace(/\B(?=(\d{3})+(?!\d))/g, ',');
    }

    function money(v) {
        return fmt(v) + ' ج';
    }

    // ضمانة وجود ApexCharts عالمياً (حتى لو لم تُحمَّل بعد من أي صفحة أخرى)
    function ensureApex(callback) {
        if (window.ApexCharts) { callback(); return; }
        var s = document.createElement('script');
        s.src = 'https://cdn.jsdelivr.net/npm/apexcharts@3.45.1/dist/apexcharts.min.js';
        s.onload = callback;
        s.onerror = function () { /* نترك الصفحة بدون رسم */ };
        document.head.appendChild(s);
    }

    function renderChart(elementId, options) {
        var el = document.getElementById(elementId);
        if (!el) return;
        if (charts[elementId]) { charts[elementId].destroy(); delete charts[elementId]; }
        charts[elementId] = new ApexCharts(el, options);
        charts[elementId].render();
    }

    window.cocoboloSales = {

        renderTrend: function (elementId, labels, current, previous) {
            var lb = (labels || []).map(bidi);
            ensureApex(function () {
                renderChart(elementId, {
                    chart: { type: 'area', height: 280, fontFamily: 'Tajawal, Segoe UI, Tahoma', toolbar: { show: false }, zoom: { enabled: false } },
                    series: [
                        { name: bidi('هذه الفترة'), data: current || [] },
                        { name: bidi('الفترة السابقة'), data: previous || [] }
                    ],
                    colors: ['#4f46e5', '#a5b4fc'],
                    stroke: { width: 3, curve: 'smooth' },
                    fill: { type: 'gradient', gradient: { opacityFrom: 0.35, opacityTo: 0.04 } },
                    dataLabels: { enabled: false },
                    grid: { borderColor: '#eef2f7' },
                    xaxis: { categories: lb },
                    yaxis: { labels: { formatter: function (v) { return fmt(v); } } },
                    tooltip: { y: { formatter: function (v) { return money(v); } } },
                    legend: { position: 'top', horizontalAlign: 'left', markers: { size: 6 } }
                });
            });
        },

        renderBranches: function (elementId, labels, values) {
            ensureApex(function () {
                renderChart(elementId, {
                    chart: { type: 'bar', height: 280, fontFamily: 'Tajawal, Segoe UI, Tahoma', toolbar: { show: false } },
                    series: [{ name: bidi('المبيعات'), data: values || [] }],
                    colors: ['#0ea5e9'],
                    plotOptions: { bar: { borderRadius: 6, columnWidth: '50%', horizontal: false } },
                    dataLabels: { enabled: false },
                    grid: { borderColor: '#eef2f7' },
                    xaxis: { categories: (labels || []).map(bidi) },
                    yaxis: { labels: { formatter: function (v) { return fmt(v); } } },
                    tooltip: { y: { formatter: function (v) { return money(v); } } }
                });
            });
        },

        renderShare: function (elementId, labels, values) {
            var palette = ['#7c3aed', '#0ea5e9', '#10b981', '#f59e0b', '#ef4444', '#6366f1', '#14b8a6', '#f472b6'];
            ensureApex(function () {
                renderChart(elementId, {
                    chart: { type: 'donut', height: 280, fontFamily: 'Tajawal, Segoe UI, Tahoma' },
                    series: values || [],
                    labels: (labels || []).map(bidi),
                    colors: palette,
                    legend: { position: 'bottom', fontSize: '12px' },
                    dataLabels: { enabled: true, formatter: function (val) { return val.toFixed(1) + '%'; } },
                    tooltip: { y: { formatter: function (v) { return money(v); } } },
                    plotOptions: { pie: { donut: { size: '68%' } } }
                });
            });
        },

        renderPayments: function (elementId, labels, values) {
            var palette = ['#10b981', '#0ea5e9', '#f59e0b', '#8b5cf6', '#ef4444', '#64748b', '#14b8a6', '#f472b6'];
            ensureApex(function () {
                renderChart(elementId, {
                    chart: { type: 'donut', height: 280, fontFamily: 'Tajawal, Segoe UI, Tahoma' },
                    series: values || [],
                    labels: (labels || []).map(bidi),
                    colors: palette,
                    legend: { position: 'bottom', fontSize: '12px' },
                    dataLabels: { enabled: true, formatter: function (val) { return val.toFixed(1) + '%'; } },
                    tooltip: { y: { formatter: function (v) { return money(v); } } },
                    plotOptions: { pie: { donut: { size: '70%', labels: { show: false } } } }
                });
            });
        },

        destroyAll: function () {
            Object.keys(charts).forEach(function (k) {
                try { charts[k].destroy(); } catch (e) { }
                delete charts[k];
            });
        },

        // ═══════════════════════════════════════════
        // Tooltips عائمة احترافية (نسخة من .sa-tip-pop داخل .sa-tip-wrap)
        // ═══════════════════════════════════════════
        initTips: function () { /* مسجّلة مرة واحدة عند التحميل — انظر بالأسفل */ }
    };

    var floatTip = null;
    var attached = false;

    function hideTip() {
        if (floatTip) floatTip.style.display = 'none';
    }

    function showTip(trigger, clientX, clientY) {
        var pop = trigger.querySelector('.sa-tip-pop');
        if (!pop || !pop.innerHTML.trim()) { hideTip(); return; }
        if (!floatTip) {
            floatTip = document.createElement('div');
            floatTip.className = 'sa-float-tip';
            document.body.appendChild(floatTip);
        }
        floatTip.innerHTML = pop.innerHTML;
        floatTip.style.display = 'block';
        floatTip.style.visibility = 'hidden';
        var pad = 14;
        var left = clientX + pad;
        var top = clientY + pad;
        var r = floatTip.getBoundingClientRect();
        if (left + r.width > window.innerWidth - 8) left = clientX - r.width - pad;
        if (top + r.height > window.innerHeight - 8) top = clientY - r.height - pad;
        if (top < 8) top = 8;
        if (left < 8) left = 8;
        floatTip.style.left = left + 'px';
        floatTip.style.top = top + 'px';
        floatTip.style.visibility = 'visible';
    }

    function attachTips() {
        if (attached) return;
        attached = true;
        document.addEventListener('mouseover', function (e) {
            var t = e.target && e.target.closest ? e.target.closest('.sa-tip-wrap') : null;
            if (t) showTip(t, e.clientX, e.clientY);
        });
        document.addEventListener('mouseout', function (e) {
            var t = e.target && e.target.closest ? e.target.closest('.sa-tip-wrap') : null;
            if (!t) hideTip();
        });
        ['scroll', 'resize'].forEach(function (ev) {
            document.addEventListener(ev, hideTip, true);
        });
        window.cocoboloSales.initTips = function () { }; // no-op after attach
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', attachTips);
    } else {
        attachTips();
    }
})();
