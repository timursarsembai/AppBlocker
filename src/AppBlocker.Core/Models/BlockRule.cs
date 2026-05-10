using System;

namespace AppBlocker.Core.Models
{
    /// <summary>
    /// Правило блокировки для конкретного ресурса (сайта или процесса).
    /// </summary>
    public class BlockRule
    {
        /// <summary>
        /// Имя ресурса (домен или имя процесса).
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Тип блокировки.
        /// </summary>
        public BlockType Type { get; set; } = BlockType.Timer;

        /// <summary>
        /// Время начала блокировки (для типа Schedule).
        /// </summary>
        public TimeSpan? StartTime { get; set; }

        /// <summary>
        /// Время окончания блокировки (для типа Schedule).
        /// </summary>
        public TimeSpan? EndTime { get; set; }
    }
}
