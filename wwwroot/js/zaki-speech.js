// إملاء صوتي حي لزكي — Web Speech API (Chrome/Edge)، من غير أي تسجيل أو رفع صوت للسيرفر.
// بيكتب النص المُعرَّف (مؤقت ونهائي) في صندوق السؤال عبر كول باك .NET،
// ولو المتصفح مابيدعمش الـ API الزرار بيختفي واللوحة بترجع لمسار التسجيل والتحويل الخادمي.
window.zakiSpeech = (function () {
    var rec = null;

    function engine() {
        return window.SpeechRecognition || window.webkitSpeechRecognition || null;
    }

    function notify(ref, method, arg) {
        try {
            var p = arg === undefined ? ref.invokeMethodAsync(method) : ref.invokeMethodAsync(method, arg);
            if (p && p.catch) p.catch(function () { });
        } catch (e) { /* الدائرة اتقفلت — نتجاهل */ }
    }

    return {
        supported: function () {
            return !!engine() && !!window.isSecureContext;
        },

        // بيرجع true لو الإملاء بدأ فعلاً
        start: function (dotNetRef) {
            var SR = engine();
            if (!SR || !window.isSecureContext) return false;
            if (rec) { try { rec.abort(); } catch (e) { } rec = null; }

            try {
                rec = new SR();
            } catch (e) {
                return false;
            }

            rec.lang = 'ar-EG';
            rec.interimResults = true;
            rec.continuous = true;

            var finalText = '';

            rec.onresult = function (ev) {
                var interim = '';
                for (var i = ev.resultIndex; i < ev.results.length; i++) {
                    var r = ev.results[i];
                    if (r.isFinal) finalText += r[0].transcript;
                    else interim += r[0].transcript;
                }
                notify(dotNetRef, 'OnSpeechText', (finalText + interim).trim());
            };

            rec.onerror = function (ev) {
                notify(dotNetRef, 'OnSpeechError', ev.error || 'other');
            };

            rec.onend = function () {
                rec = null;
                notify(dotNetRef, 'OnSpeechEnd');
            };

            try {
                rec.start();
                return true;
            } catch (e) {
                rec = null;
                return false;
            }
        },

        stop: function () {
            if (rec) {
                var r = rec;
                rec = null;
                try { r.stop(); } catch (e) { }
            }
        }
    };
})();
