using System;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AppBlocker.Shared.IPC;

namespace AppBlocker.Service.IPC
{
    /// <summary>
    /// Сервер, слушающий команды от WPF-интерфейса через Named Pipes.
    /// </summary>
    public class IpcServer
    {
        private const string PipeName = "AppBlockerIPC";
        private readonly Func<IpcRequest, IpcResponse> _requestHandler;

        public IpcServer(Func<IpcRequest, IpcResponse> requestHandler)
        {
            _requestHandler = requestHandler;
        }

        /// <summary>
        /// Бесконечный цикл прослушивания входящих подключений.
        /// </summary>
        public async Task StartListeningAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    // Создаем серверный пайп. 
                    // В реальном production-коде здесь нужно добавить PipeSecurity,
                    // чтобы разрешить подключение только локальным Администраторам или текущему юзеру.
                    using var pipeServer = new NamedPipeServerStream(
                        PipeName, 
                        PipeDirection.InOut, 
                        1, 
                        PipeTransmissionMode.Message, 
                        PipeOptions.Asynchronous);

                    // Ждем подключения клиента (UI)
                    await pipeServer.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(pipeServer);
                    using var writer = new StreamWriter(pipeServer) { AutoFlush = true };

                    string jsonRequest = await reader.ReadLineAsync();
                    
                    if (!string.IsNullOrEmpty(jsonRequest))
                    {
                        var request = JsonSerializer.Deserialize<IpcRequest>(jsonRequest);
                        
                        // Передаем запрос обработчику в основном Worker'е
                        var response = _requestHandler(request);
                        
                        string jsonResponse = JsonSerializer.Serialize(response);
                        await writer.WriteLineAsync(jsonResponse);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Ожидаемое завершение работы сервиса
                    break; 
                }
                catch (Exception ex)
                {
                    // Логируем и продолжаем слушать следующий запрос
                    System.Diagnostics.Debug.WriteLine($"IPC Server Error: {ex.Message}");
                }
            }
        }
    }
}
