window.sessionMonitor = {
    start: function (dotNetHelper, timeoutSeconds) {
        let timer;

        const resetTimer = () => {
            clearTimeout(timer);
            timer = setTimeout(() => {
                dotNetHelper.invokeMethodAsync("LogoutByInactivity");
            }, timeoutSeconds * 1000);
        };

        // Detectar actividad del usuario
        window.onmousemove = resetTimer;
        window.onkeydown = resetTimer;
        window.onclick = resetTimer;
        window.onscroll = resetTimer;

        // Iniciar temporizador
        resetTimer();
    }
};


