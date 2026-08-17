(function () {
    'use strict';

    const charts = {};

    function totalFormatter(w) {
        const total = (w?.globals?.seriesTotals || []).reduce((a, b) => a + b, 0);
        return new Intl.NumberFormat('ar-EG').format(total);
    }

    window.cocoboloLeadsDashboard = {
        renderRoundedQuotationDonut: function (elementId, labels, values, colors, totalLabel) {
            const el = document.getElementById(elementId);
            if (!el || typeof ApexCharts === 'undefined') return;

            if (charts[elementId]) {
                charts[elementId].destroy();
            }

            const options = {
                series: values || [],
                chart: {
                    type: 'donut',
                    width: 420
                },
                labels: labels || [],
                colors: ['#0EA5E9', '#14B8A6', '#F59E0B', '#F43F5E'],
                plotOptions: {
                    pie: {
                        borderRadius: 12,
                        spacing: 5,
                        donut: {
                            size: '68%',
                            labels: {
                                show: true,
                                total: {
                                    show: true,
                                    label: totalLabel || 'إجمالي العروض',
                                    formatter: totalFormatter
                                }
                            }
                        }
                    }
                },
                stroke: {
                    width: 0
                },
                dataLabels: {
                    enabled: false
                },
                legend: {
                    position: 'bottom'
                },
                tooltip: {
                    theme: 'dark',
                    y: {
                        formatter: function (val) {
                            return new Intl.NumberFormat('ar-EG').format(val);
                        },
                        title: {
                            formatter: function () { return ''; }
                        }
                    }
                },
                responsive: [
                    {
                        breakpoint: 480,
                        options: {
                            chart: {
                                width: 320
                            }
                        }
                    }
                ]
            };

            charts[elementId] = new ApexCharts(el, options);
            charts[elementId].render();
        },

        destroyChart: function (elementId) {
            if (charts[elementId]) {
                charts[elementId].destroy();
                delete charts[elementId];
            }
        }
    };
})();
