window.openPdfBase64 = function(b64) {
    try {
        var byteCharacters = atob(b64);
        var byteNumbers = new Array(byteCharacters.length);
        for (var i = 0; i < byteCharacters.length; i++) {
            byteNumbers[i] = byteCharacters.charCodeAt(i);
        }
        var byteArray = new Uint8Array(byteNumbers);
        var blob = new Blob([byteArray], { type: 'application/pdf' });
        var url = URL.createObjectURL(blob);
        window.open(url, '_blank');
    } catch(e) {
        alert('حدث خطأ أثناء فتح الملف: ' + e.message);
    }
};
var _notificationAudio = new Audio('/sounds/notification.mp3');
_notificationAudio.volume = 1.0;

window.playNotificationSound = function() {
    try {
        _notificationAudio.currentTime = 0; // يرجع من الأول
        _notificationAudio.play().catch(function(e) {
            console.log('Sound blocked: ' + e.message);
        });
    } catch(e) {
        console.log('Sound error: ' + e.message);
    }
};