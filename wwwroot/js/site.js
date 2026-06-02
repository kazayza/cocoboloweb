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
// ⭐ Notification Sound - تشغيل صوت الإشعارات
// ═══════════════════════════════════════════

// الحل: نعمل pre-load للأوديو بعد أول تفاعل من المستخدم
(function () {
    let _audio = null;
    let _unlocked = false;

    // نجهز الأوديو مرة واحدة ونحتفظ بيه
    function initAudio() {
        if (_audio) return;
        _audio = new Audio('/sounds/notification.mp3');
        _audio.preload = 'auto';
        _audio.volume = 0.7;
    }

    // callback يناديه Blazor لما يعوز يعرف الـ Audio اتفتح
    let _onUnlockedCallback = null;

    window.setOnAudioUnlocked = function (dotNetRef) {
        _onUnlockedCallback = dotNetRef;
        // لو الـ audio اتفتح بالفعل قبل ما Blazor يسجل → نناديه فوراً
        if (_unlocked && _onUnlockedCallback) {
            _onUnlockedCallback.invokeMethodAsync('OnAudioUnlocked').catch(() => {});
        }
    };

    // بعد أول تفاعل (click/keydown/touchstart) → نعمل unlock
    function unlockAudio() {
        if (_unlocked) return;
        initAudio();
        // نشغل الصوت بصوت = 0 عشان نفتح قناة الأوديو
        _audio.volume = 0;
        _audio.play()
            .then(() => {
                _audio.pause();
                _audio.currentTime = 0;
                _audio.volume = 0.7;
                _unlocked = true;
                console.log('✅ Audio unlocked successfully');
                // نبلغ Blazor إن الـ Audio اتفتح
                if (_onUnlockedCallback) {
                    _onUnlockedCallback.invokeMethodAsync('OnAudioUnlocked').catch(() => {});
                }
            })
            .catch(() => {
                // ممكن يفشل أول مرة - مش مشكلة
            });
    }

    // نستمع لأي تفاعل من المستخدم
    ['click', 'keydown', 'touchstart', 'mousedown'].forEach(event => {
        document.addEventListener(event, unlockAudio, { once: false, passive: true });
    });

    // الدالة اللي بيناديها Blazor
    window.playNotificationSound = function () {
        try {
            initAudio();

            if (_unlocked) {
                // الطريق العادي — الأوديو مفتوح
                _audio.currentTime = 0;
                _audio.volume = 0.7;
                _audio.play().catch(err => {
                    console.warn('⚠️ Sound play failed:', err);
                });
            } else {
                // fallback — نحاول نشغله على أمل
                const fallback = new Audio('/sounds/notification.mp3');
                fallback.volume = 0.7;
                fallback.play().catch(() => {
                    console.warn('⚠️ Audio still locked - user must interact with page first');
                });
            }
        } catch (error) {
            console.error('❌ Error playing notification sound:', error);
        }
    };
})();


// ═══════════════════════════════════════════
// ⭐ Idle Logout - تسجيل خروج تلقائي
// ═══════════════════════════════════════════
window.idleLogout = {
    timer: null,
    minutes: 30,  // ⏰ مدة الـ Idle (غيرها لو حابب)
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