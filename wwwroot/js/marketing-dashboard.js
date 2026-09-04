// ═══════════════════════════════════════════════════════════
// 📊 Marketing Dashboard — Funnel + Semi-Gauge (ApexCharts)
// ═══════════════════════════════════════════════════════════
(function () {
    'use strict';

    var charts = {};

    function bidi(text) {
        return '\u2067' + text + '\u2069';
    }

    window.cocoboloMarketing = {

        // ═══════════════════════════════════════════
        // 🪜 الفانل (ApexCharts Funnel)
        // ═══════════════════════════════════════════
        renderFunnel: function (elementId, labels, values, colors, drops) {
            var el = document.getElementById(elementId);
            if (!el) return;

            if (charts[elementId]) {
                charts[elementId].destroy();
            }

            var options = {
                series: [
                    {
                        name: 'Funnel Series',
                        data: values,
                    },
                ],
                chart: {
                    type: 'funnel',
                    height: 500,
                    dropShadow: {
                        enabled: true,
                        top: 2,
                        left: 2,
                        blur: 4,
                        opacity: 0.15,
                    },
                },
                colors: colors || ['#1769d5', '#d84635', '#7447c6', '#efa21b', '#15965e'],
                plotOptions: {
                    bar: {
                        borderRadius: 0,
                        barHeight: '80%',
                        distributed: true,
                    },
                },
                dataLabels: {
                    enabled: true,
                    formatter: function (val, opt) {
                        var idx = opt.dataPointIndex;
                        var label = opt.w.globals.labels[idx];
                        var drop = drops && drops[idx] != null ? drops[idx] : null;
                        var txt = bidi(label) + ':  ' + val;
                        if (drop != null && drop > 0) {
                            txt += '  (تسرب ' + drop.toFixed(1) + '%)';
                        }
                        return txt;
                    },
                    style: {
                        fontSize: '13px',
                        fontFamily: 'Cairo, sans-serif',
                        fontWeight: 700,
                        colors: ['#ffffff'],
                    },
                    dropShadow: {
                        enabled: true,
                    },
                },
                xaxis: {
                    categories: labels.map(bidi),
                },
                legend: {
                    show: false,
                },
                tooltip: {
                    style: {
                        fontSize: '12px',
                        fontFamily: 'Cairo, sans-serif',
                    },
                },
            };

            charts[elementId] = new ApexCharts(el, options);
            charts[elementId].render();
        },

        updateFunnel: function (elementId, labels, values, drops) {
            var chart = charts[elementId];
            if (!chart) {
                window.cocoboloMarketing.renderFunnel(elementId, labels, values, null, drops);
                return;
            }
            chart.updateOptions({
                xaxis: {
                    categories: labels.map(bidi),
                },
                dataLabels: {
                    formatter: function (val, opt) {
                        var idx = opt.dataPointIndex;
                        var label = opt.w.globals.labels[idx];
                        var drop = drops && drops[idx] != null ? drops[idx] : null;
                        var txt = bidi(label) + ':  ' + val;
                        if (drop != null && drop > 0) {
                            txt += '  (تسرب ' + drop.toFixed(1) + '%)';
                        }
                        return txt;
                    },
                },
            });
            chart.updateSeries([
                {
                    data: values,
                },
            ]);
        },

        // ═══════════════════════════════════════════
        // 🎯 Semi-Circle Gauge
        // ═══════════════════════════════════════════
        renderSemiGauge: function (elementId, value, color) {
            var el = document.getElementById(elementId);
            if (!el) return;

            if (charts[elementId]) {
                charts[elementId].destroy();
            }

            var clamped = Math.max(0, Math.min(100, value));

            var options = {
                series: [clamped],
                chart: {
                    height: 190,
                    type: 'gauge',
                },
                plotOptions: {
                    radialBar: {
                        startAngle: -90,
                        endAngle: 90,
                        track: {
                            background: '#e7e7e7',
                            strokeWidth: '97%',
                            margin: 5,
                        },
                        dataLabels: {
                            name: { show: false },
                            value: {
                                offsetY: -2,
                                fontSize: '26px',
                                fontFamily: 'Cairo, sans-serif',
                                fontWeight: 800,
                                color: '#17233a',
                                formatter: function (val) {
                                    return val.toFixed(1) + '%';
                                },
                            },
                        },
                    },
                },
                fill: {
                    type: 'gradient',
                    gradient: {
                        shade: 'light',
                        shadeIntensity: 0.4,
                        inverseColors: false,
                        opacityFrom: 1,
                        opacityTo: 1,
                        stops: [0, 50, 53, 91],
                    },
                },
                colors: [color || '#15965e'],
                labels: ['Score'],
            };

            charts[elementId] = new ApexCharts(el, options);
            charts[elementId].render();
        },

        // 🏆 Score gauge (0-10) — نسخة أكبر من semi-gauge مخصصة لمؤشر الأداء التسويقي
        renderScoreGauge: function (elementId, score, color) {
            var el = document.getElementById(elementId);
            if (!el) return;

            if (charts[elementId]) {
                charts[elementId].destroy();
            }

            var clampedScore = Math.max(0, Math.min(10, score || 0));
            var pct = clampedScore * 10;

            var options = {
                series: [pct],
                chart: {
                    height: 250,
                    type: 'gauge',
                },
                plotOptions: {
                    radialBar: {
                        startAngle: -90,
                        endAngle: 90,
                        track: {
                            background: '#e7e7e7',
                            strokeWidth: '95%',
                            margin: 6,
                        },
                        dataLabels: {
                            name: { show: false },
                            value: {
                                offsetY: -6,
                                fontSize: '40px',
                                fontFamily: 'Cairo, sans-serif',
                                fontWeight: 900,
                                color: '#17233a',
                                formatter: function (val) {
                                    return (val / 10).toFixed(1);
                                },
                            },
                        },
                    },
                },
                fill: {
                    type: 'gradient',
                    gradient: {
                        shade: 'light',
                        shadeIntensity: 0.35,
                        inverseColors: false,
                        opacityFrom: 1,
                        opacityTo: 1,
                        stops: [0, 50, 53, 91],
                    },
                },
                colors: [color || '#15965e'],
                labels: ['Score'],
            };

            charts[elementId] = new ApexCharts(el, options);
            charts[elementId].render();
        },

        updateScoreGauge: function (elementId, score, color) {
            var chart = charts[elementId];
            if (!chart) {
                window.cocoboloMarketing.renderScoreGauge(elementId, score, color);
                return;
            }
            var clampedScore = Math.max(0, Math.min(10, score || 0));
            chart.updateOptions({
                colors: [color || '#15965e'],
            });
            chart.updateSeries([clampedScore * 10]);
        },

        updateSemiGauge: function (elementId, value, color) {
            var chart = charts[elementId];
            if (!chart) {
                window.cocoboloMarketing.renderSemiGauge(elementId, value, color);
                return;
            }
            chart.updateOptions({
                colors: [color || '#15965e'],
            });
            chart.updateSeries([Math.max(0, Math.min(100, value))]);
        },
    };
})();
