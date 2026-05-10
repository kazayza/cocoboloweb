window.cocoboloAuth = {
    login: async function (username, password) {
        try {
            const response = await fetch("/auth/login", {
                method: "POST",
                headers: {
                    "Content-Type": "application/json"
                },
                credentials: "same-origin",
                body: JSON.stringify({
                    username: username,
                    password: password
                })
            });

            const text = await response.text();

            if (!response.ok) {
                let message = text;

                try {
                    const json = JSON.parse(text);
                    message = json.message || json.title || message;
                } catch { }

                return {
                    Ok: false,
                    Message: message || "تعذر تسجيل الدخول."
                };
            }

            return {
                Ok: true,
                Message: null
            };
        } catch {
            return {
                Ok: false,
                Message: "تعذر الاتصال بالخادم. تحقق من الشبكة ثم حاول مرة أخرى."
            };
        }
    },

    logout: async function () {
        await fetch("/auth/logout", {
            method: "POST",
            credentials: "same-origin"
        });
    }
};