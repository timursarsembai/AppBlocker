using System;
using System.IO;
using System.Text;
using System.Text.Json;
using AppBlocker.Core.Configuration;

namespace AppBlocker.Bridge;

/// <summary>
/// Native Messaging Host для Chrome.
/// Chrome запускает этот процесс и общается через stdin/stdout.
/// 
/// Протокол Chrome Native Messaging:
///   - Каждое сообщение = 4 байта длины (uint32, little-endian) + JSON тело
///   - Chrome пишет в stdin, читает из stdout
/// </summary>
class Program
{
    static void Main()
    {
        var configManager = new ConfigManager();

        // Читаем сообщения из stdin в цикле (long-lived connection)
        using var stdin = Console.OpenStandardInput();
        using var stdout = Console.OpenStandardOutput();

        while (true)
        {
            try
            {
                // 1. Читаем 4 байта длины сообщения
                var lengthBytes = new byte[4];
                int bytesRead = stdin.Read(lengthBytes, 0, 4);
                if (bytesRead == 0) break; // Chrome закрыл соединение

                int messageLength = BitConverter.ToInt32(lengthBytes, 0);
                if (messageLength <= 0 || messageLength > 1024 * 1024) break; // Защита от мусора

                // 2. Читаем JSON тело
                var messageBytes = new byte[messageLength];
                int totalRead = 0;
                while (totalRead < messageLength)
                {
                    int read = stdin.Read(messageBytes, totalRead, messageLength - totalRead);
                    if (read == 0) break;
                    totalRead += read;
                }

                string json = Encoding.UTF8.GetString(messageBytes, 0, totalRead);
                var request = JsonSerializer.Deserialize<JsonElement>(json);

                // 3. Обрабатываем запрос
                string action = request.TryGetProperty("action", out var actionProp) 
                    ? actionProp.GetString() 
                    : "unknown";

                string responseJson;

                if (action == "getConfig")
                {
                    var config = configManager.LoadConfig();

                    // Определяем активна ли сессия
                    bool isActive = config.CurrentMode.ToString() != "None" && config.BlockEndTime.HasValue;
                    string sessionEnd = null;
                    
                    if (isActive && config.BlockEndTime.HasValue)
                    {
                        // Проверяем не истекла ли
                        if (DateTime.UtcNow >= config.BlockEndTime.Value.ToUniversalTime())
                        {
                            isActive = false;
                        }
                        else
                        {
                            sessionEnd = config.BlockEndTime.Value.ToUniversalTime().ToString("o");
                        }
                    }

                    var response = new
                    {
                        blocked = isActive ? config.BlockedWebsites : new System.Collections.Generic.List<string>(),
                        sessionEnd = sessionEnd,
                        mode = isActive ? config.CurrentMode.ToString() : "None"
                    };

                    responseJson = JsonSerializer.Serialize(response);
                }
                else if (action == "ping")
                {
                    responseJson = "{\"status\":\"ok\"}";
                }
                else
                {
                    responseJson = "{\"error\":\"unknown action\"}";
                }

                // 4. Отправляем ответ: 4 байта длины + JSON
                byte[] responseBytes = Encoding.UTF8.GetBytes(responseJson);
                byte[] responseLengthBytes = BitConverter.GetBytes(responseBytes.Length);
                
                stdout.Write(responseLengthBytes, 0, 4);
                stdout.Write(responseBytes, 0, responseBytes.Length);
                stdout.Flush();
            }
            catch (Exception)
            {
                // Если stdin закрыт или любая ошибка — выходим
                break;
            }
        }
    }
}
