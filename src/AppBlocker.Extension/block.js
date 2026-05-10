// block.js — логика страницы блокировки
// Показывает заблокированный URL, таймер, рандомную привычку

(function() {
    // Получаем URL из query-параметра
    const params = new URLSearchParams(window.location.search);
    const blockedUrl = params.get("url") || "неизвестный сайт";

    // Показываем заблокированный URL
    try {
        const parsed = new URL(blockedUrl);
        document.getElementById("blockedUrl").textContent = parsed.hostname + parsed.pathname;
    } catch {
        document.getElementById("blockedUrl").textContent = blockedUrl;
    }

    // Загружаем данные из chrome.storage.local
    chrome.storage.local.get(
        ["sessionEnd", "sessionMode"],
        (data) => {
            // Запускаем таймер
            if (data.sessionEnd) {
                const endTime = new Date(data.sessionEnd).getTime();
                const timerBlock = document.getElementById("timerBlock");
                const timerValue = document.getElementById("timerValue");
                
                timerBlock.style.display = "block";

                function updateTimer() {
                    const now = Date.now();
                    const remaining = endTime - now;

                    if (remaining <= 0) {
                        timerValue.textContent = "00:00:00";
                        timerBlock.querySelector(".timer-label").textContent = "Сессия завершена";
                        return;
                    }

                    const hours = Math.floor(remaining / 3600000);
                    const minutes = Math.floor((remaining % 3600000) / 60000);
                    const seconds = Math.floor((remaining % 60000) / 1000);

                    timerValue.textContent = 
                        String(hours).padStart(2, "0") + ":" +
                        String(minutes).padStart(2, "0") + ":" +
                        String(seconds).padStart(2, "0");
                }

                updateTimer();
                setInterval(updateTimer, 1000);
            }
        }
    );
})();
