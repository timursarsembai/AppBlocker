namespace AppBlocker.Shared.IPC
{
    /// <summary>
    /// Запрос, отправляемый от UI к фоновой службе.
    /// </summary>
    public class IpcRequest
    {
        /// <summary>
        /// Имя команды (например, "StartSession", "GetStatus", "AddWebsite").
        /// </summary>
        public string Action { get; set; }

        /// <summary>
        /// Данные команды в формате JSON.
        /// </summary>
        public string Payload { get; set; }
    }

    /// <summary>
    /// Ответ фоновой службы пользовательскому интерфейсу.
    /// </summary>
    public class IpcResponse
    {
        /// <summary>
        /// Успешно ли выполнена команда.
        /// </summary>
        public bool IsSuccess { get; set; }

        /// <summary>
        /// Сообщение об ошибке (если IsSuccess == false).
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Полезная нагрузка ответа (статус, данные) в формате JSON.
        /// </summary>
        public string Payload { get; set; }
    }
}
