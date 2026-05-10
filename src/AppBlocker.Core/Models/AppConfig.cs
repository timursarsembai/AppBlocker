using System;
using System.Collections.Generic;

namespace AppBlocker.Core.Models
{
    /// <summary>
    /// Модель данных для хранения конфигурации приложения.
    /// </summary>
    public class AppConfig
    {
        /// <summary>
        /// Список заблокированных доменов (например, "youtube.com").
        /// </summary>
        public List<string> BlockedWebsites { get; set; } = new List<string>();

        /// <summary>
        /// Список заблокированных процессов (например, "telegram.exe").
        /// </summary>
        public List<string> BlockedProcesses { get; set; } = new List<string>();

        /// <summary>
        /// Текущий активный режим работы.
        /// </summary>
        public BlockingMode CurrentMode { get; set; } = BlockingMode.None;

        /// <summary>
        /// Время (в UTC), до которого действует блокировка. 
        /// Если null, значит сессия бессрочная или не активна.
        /// </summary>
        public DateTime? BlockEndTime { get; set; }


        /// <summary>
        /// Запускать UI вместе с Windows.
        /// </summary>
        public bool AutoStartUI { get; set; } = true;

        /// <summary>
        /// Сворачивать приложение в трей вместо закрытия.
        /// </summary>
        public bool MinimizeToTray { get; set; } = true;

        /// <summary>
        /// Длительность сессии "Окно ясности" в минутах.
        /// </summary>
        public int ClarityWindowMinutes { get; set; } = 120;

        /// <summary>
        /// Длительность сессии "Снижение барьера" в минутах.
        /// </summary>
        public int LowerBarrierMinutes { get; set; } = 60;

        /// <summary>
        /// Длительность сессии "Паника" в минутах.
        /// </summary>
        public int PanicMinutes { get; set; } = 60;

        /// <summary>
        /// Тип защиты (None, Pin, Password) от досрочного отключения блокировки.
        /// </summary>
        public ProtectionType CurrentProtectionType { get; set; } = ProtectionType.None;

        /// <summary>
        /// SHA256 хэш PIN-кода или пароля.
        /// </summary>
        public string ProtectionHash { get; set; } = string.Empty;

        /// <summary>
        /// Требовать решение математической задачи после верного ввода пароля.
        /// </summary>
        public bool RequireMathChallenge { get; set; } = false;
    }
}
