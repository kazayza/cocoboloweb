// ═══════════════ Download File Functions ═══════════════

window.downloadFileFromBase64 = function (base64, fileName) {
    const link = document.createElement('a');
    link.href = 'data:application/vnd.openxmlformats-officedocument.spreadsheetml.sheet;base64,' + base64;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};

window.downloadFile = function (fileName, contentType, base64) {
    const link = document.createElement('a');
    link.href = `data:${contentType};base64,${base64}`;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
};
window.printPage = function () { window.print(); };


// ═══════════════════════════════════════════
// ⭐ Idle Logout - تسجيل خروج تلقائي
// ═══════════════════════════════════════════
window.idleLogout = {
    timer: null,
    minutes: 5,  // ⏰ مدة الـ Idle (غيرها لو حابب)
    dotNetRef: null,

    start: function (dotNetReference) {
        this.dotNetRef = dotNetReference;
        const self = this;

        const resetTimer = () => {
            clearTimeout(self.timer);
            self.timer = setTimeout(() => {
                if (self.dotNetRef) {
                    self.dotNetRef.invokeMethodAsync('OnIdleTimeout');
                }
            }, self.minutes * 60 * 1000);
        };

        // الأحداث اللي تعتبر نشاط
        ['mousemove', 'keydown', 'mousedown', 'touchstart', 'scroll', 'click']
            .forEach(event => {
                document.addEventListener(event, resetTimer, { passive: true });
            });

        // ابدأ المؤقت
        resetTimer();
        console.log('✅ Idle Logout activated - ' + this.minutes + ' minutes');
    },

    stop: function () {
        clearTimeout(this.timer);
        this.dotNetRef = null;
    }
};