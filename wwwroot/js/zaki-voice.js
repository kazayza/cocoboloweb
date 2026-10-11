// زكي — تسجيل الصوت من المتصفح (MediaRecorder)
// الميكروفون بيشتغل على https أو على localhost بس (المتصفح بيمنعه غير كده).
// start() بيرجّع "" لو نجح، أو كود يوضح سبب الفشل:
//   insecure | nosupport | denied | notfound | busy | other
// stop() بيرجّع الصوت Base64 عشان يتبعت للسيرفر.
window.zakiVoice = {
    _recorder: null,
    _chunks: [],
    _stream: null,

    start: async function () {
        if (this._recorder) return '';

        if (!window.isSecureContext) return 'insecure';
        if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia || !window.MediaRecorder) {
            return 'nosupport';
        }

        this._chunks = [];

        try {
            this._stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        } catch (e) {
            const name = e && e.name ? e.name : '';
            if (name === 'NotAllowedError' || name === 'SecurityError') return 'denied';
            if (name === 'NotFoundError' || name === 'DevicesNotFoundError') return 'notfound';
            if (name === 'NotReadableError' || name === 'TrackStartError') return 'busy';
            return 'other';
        }

        const mime = MediaRecorder.isTypeSupported('audio/webm') ? 'audio/webm' : '';

        this._recorder = mime
            ? new MediaRecorder(this._stream, { mimeType: mime })
            : new MediaRecorder(this._stream);

        this._recorder.ondataavailable = (e) => {
            if (e.data && e.data.size > 0) this._chunks.push(e.data);
        };

        this._recorder.start();
        return '';
    },

    stop: function () {
        return new Promise((resolve) => {
            const rec = this._recorder;
            if (!rec) { resolve({ mime: '', data: '' }); return; }

            rec.onstop = async () => {
                const type = rec.mimeType || 'audio/webm';
                const blob = new Blob(this._chunks, { type: type });

                if (this._stream) this._stream.getTracks().forEach((t) => t.stop());

                this._recorder = null;
                this._stream = null;
                this._chunks = [];

                resolve({ mime: type, data: await blobToBase64(blob) });
            };

            rec.stop();
        });
    },

    cancel: function () {
        if (this._recorder) {
            try { this._recorder.stop(); } catch (e) { /* تجاهل */ }
        }
        if (this._stream) this._stream.getTracks().forEach((t) => t.stop());
        this._recorder = null;
        this._stream = null;
        this._chunks = [];
    }
};

function blobToBase64(blob) {
    return new Promise((resolve, reject) => {
        const reader = new FileReader();
        reader.onload = () => resolve(String(reader.result).split(',')[1] || '');
        reader.onerror = reject;
        reader.readAsDataURL(blob);
    });
}
