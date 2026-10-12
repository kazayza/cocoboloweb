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

// ═══════════════ Zaki FAB — سحب وحفظ المكان (P4) ═══════════════

window.zakiFabDragged = false;

window.zakiInitFab = function () {
    const btn = document.getElementById('zakiFab');
    if (!btn || btn.dataset.zakiInit === '1') return;
    btn.dataset.zakiInit = '1';

    const MARGIN = 12;

    // حمّل المكان المحفوظ
    try {
        const saved = localStorage.getItem('zakiFabPos');
        if (saved) {
            const p = saved.split('|');
            const right = parseFloat(p[0]);
            const bottom = parseFloat(p[1]);
            if (!isNaN(right) && !isNaN(bottom)) {
                btn.style.right = Math.max(MARGIN, Math.min(right, window.innerWidth - btn.offsetWidth - MARGIN)) + 'px';
                btn.style.bottom = Math.max(MARGIN, Math.min(bottom, window.innerHeight - btn.offsetHeight - MARGIN)) + 'px';
            }
        }
    } catch (e) { }

    let dragging = false, moved = false;
    let startX = 0, startY = 0, startRight = 0, startBottom = 0;

    btn.addEventListener('pointerdown', function (e) {
        dragging = true;
        moved = false;
        startX = e.clientX;
        startY = e.clientY;
        const rect = btn.getBoundingClientRect();
        startRight = window.innerWidth - rect.right;
        startBottom = window.innerHeight - rect.bottom;
        try { btn.setPointerCapture(e.pointerId); } catch (ex) { }
        e.preventDefault();
    });

    btn.addEventListener('pointermove', function (e) {
        if (!dragging) return;
        const dx = e.clientX - startX;
        const dy = e.clientY - startY;
        if (!moved && Math.abs(dx) + Math.abs(dy) < 8) return;
        moved = true;
        btn.classList.add('zk-dragging');

        const maxRight = window.innerWidth - btn.offsetWidth - MARGIN;
        const maxBottom = window.innerHeight - btn.offsetHeight - MARGIN;
        btn.style.right = Math.max(MARGIN, Math.min(startRight - dx, maxRight)) + 'px';
        btn.style.bottom = Math.max(MARGIN, Math.min(startBottom - dy, maxBottom)) + 'px';
    });

    function endDrag() {
        if (!dragging) return;
        dragging = false;
        btn.classList.remove('zk-dragging');
        if (moved) {
            window.zakiFabDragged = true;
            setTimeout(function () { window.zakiFabDragged = false; }, 300);
            try {
                const rect = btn.getBoundingClientRect();
                localStorage.setItem('zakiFabPos',
                    (window.innerWidth - rect.right) + '|' + (window.innerHeight - rect.bottom));
            } catch (e) { }
        }
    }

    // الصور جوّه الزرار ممنوع المتصفح يسحبها سحب أصلي
    btn.addEventListener('dragstart', function (e) { e.preventDefault(); });

    btn.addEventListener('pointerup', endDrag);
    btn.addEventListener('pointercancel', endDrag);

    // لما المقاس يتغير نرجع الزرار جوه الشاشة
    window.addEventListener('resize', function () {
        const rect = btn.getBoundingClientRect();
        const right = window.innerWidth - rect.right;
        const bottom = window.innerHeight - rect.bottom;
        const maxRight = window.innerWidth - btn.offsetWidth - MARGIN;
        const maxBottom = window.innerHeight - btn.offsetHeight - MARGIN;
        btn.style.right = Math.max(MARGIN, Math.min(right, maxRight)) + 'px';
        btn.style.bottom = Math.max(MARGIN, Math.min(bottom, maxBottom)) + 'px';
        window.zakiAnchorPanel();
    });
};

window.zakiFabWasDragged = function () {
    return window.zakiFabDragged === true;
};

// يثبت النافذة الصغيرة جنب الزرار (بتتنادى بعد كل رسم)
window.zakiAnchorPanel = function () {
    const btn = document.getElementById('zakiFab');
    const panel = document.getElementById('zakiMiniPanel');
    if (!btn || !panel) return;

    const MARGIN = 12;
    const rect = btn.getBoundingClientRect();
    const right = window.innerWidth - rect.right;
    const bottom = window.innerHeight - rect.bottom;

    const maxPanelRight = Math.max(MARGIN, window.innerWidth - panel.offsetWidth - MARGIN);
    panel.style.right = Math.max(MARGIN, Math.min(right, maxPanelRight)) + 'px';
    panel.style.bottom = (bottom + rect.height + 12) + 'px';
};


// احتياطي: لو Blazor مناداش zakiInitFab (كاش قديم مثلًا) — نشغله لوحده أول ما الزرار يظهر
(function zakiFabAutoInit() {
    let tries = 0;
    const timer = setInterval(function () {
        tries++;
        if (document.getElementById('zakiFab')) {
            clearInterval(timer);
            if (window.zakiInitFab) window.zakiInitFab();
        } else if (tries > 40) {
            clearInterval(timer);
        }
    }, 500);
})();
