namespace AppBlocker.Core.Models
{
    /// <summary>
    /// Режимы работы блокировщика.
    /// </summary>
    public enum BlockingMode
    {
        /// <summary>
        /// Блокировка отключена.
        /// </summary>
        None,

        /// <summary>
        /// Режим "Окно ясности" (жесткая блокировка всего отвлекающего).
        /// </summary>
        ClarityWindow,

        /// <summary>
        /// Режим "Снижение барьера" (мягкая блокировка с возможностью ввода текста для отмены).
        /// </summary>
        LowerBarrier,

        /// <summary>
        /// Режим "Гиперфокус" (блокировка всего, кроме разрешенного белого списка).
        /// </summary>
        Hyperfocus
    }
}
