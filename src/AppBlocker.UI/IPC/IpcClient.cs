using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading.Tasks;
using AppBlocker.Shared.IPC;

namespace AppBlocker.UI.IPC
{
    /// <summary>
    /// Клиент для общения WPF-приложения с фоновым сервисом через Named Pipes.
    /// </summary>
    public class IpcClient
    {
        private const string PipeName = "AppBlockerIPC";
        private const int TimeoutMs = 3000; // Ждем ответа сервиса максимум 3 секунды

        /// <summary>
        /// Отправляет команду сервису и возвращает результат.
        /// </summary>
        /// <param name="action">Имя команды (например, "StartSession").</param>
        /// <param name="payload">Любой объект данных (будет сериализован в JSON).</param>
        public async Task<IpcResponse> SendRequestAsync(string action, object payload = null)
        {
            var request = new IpcRequest
            {
                Action = action,
                Payload = payload != null ? JsonSerializer.Serialize(payload) : null
            };

            // Подключаемся к пайпу на локальной машине (".")
            using var pipeClient = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            
            try
            {
                await pipeClient.ConnectAsync(TimeoutMs);

                using var writer = new StreamWriter(pipeClient) { AutoFlush = true };
                using var reader = new StreamReader(pipeClient);

                // Отправляем JSON-запрос (с \n на конце)
                string jsonRequest = JsonSerializer.Serialize(request);
                await writer.WriteLineAsync(jsonRequest);

                // Ждем JSON-ответ
                string jsonResponse = await reader.ReadLineAsync();
                
                if (string.IsNullOrEmpty(jsonResponse))
                {
                    return new IpcResponse { IsSuccess = false, ErrorMessage = "Получен пустой ответ от сервиса." };
                }

                return JsonSerializer.Deserialize<IpcResponse>(jsonResponse);
            }
            catch (TimeoutException)
            {
                return new IpcResponse { IsSuccess = false, ErrorMessage = "Служба AppBlocker не запущена или не отвечает." };
            }
            catch (Exception ex)
            {
                return new IpcResponse { IsSuccess = false, ErrorMessage = $"Ошибка IPC: {ex.Message}" };
            }
        }
    }
}
