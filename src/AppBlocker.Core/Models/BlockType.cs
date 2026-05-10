using System;

namespace AppBlocker.Core.Models
{
    /// <summary>
    /// Тип блокировки ресурса.
    /// </summary>
    public enum BlockType
    {
        /// <summary>
        /// Блокировка во время активной сессии (по таймеру).
        /// </summary>
        Timer,

        /// <summary>
        /// Блокировка всегда (пока не отключит пользователь).
        /// </summary>
        Always,

        /// <summary>
        /// Блокировка по расписанию (диапазон времени).
        /// </summary>
        Schedule
    }
}
