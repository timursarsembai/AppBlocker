using System;
using System.IO;
using System.Text.Json;
using AppBlocker.Core.Models;

namespace AppBlocker.Core.Configuration
{
    /// <summary>
    /// Менеджер конфигурации, отвечающий за сохранение и загрузку настроек блокировщика.
    /// </summary>
    public class ConfigManager
    {
        // Используем ProgramData (CommonApplicationData), чтобы:
        // 1. Файл был доступен фоновому сервису (который работает под системным аккаунтом).
        // 2. Файл был скрыт от обычного пользователя (папка ProgramData по умолчанию скрыта в Windows).
        private static readonly string ConfigDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), 
            "AppBlocker");
            
        private static readonly string ConfigFilePath = Path.Combine(ConfigDirectory, "settings.json");
        
        // Объект для синхронизации потоков (чтобы UI и Service не попытались писать в файл одновременно,
        // хотя в идеале записью должен заниматься только сервис через IPC, но на всякий случай лочим).
        private readonly object _fileLock = new object();

        public ConfigManager()
        {
            EnsureDirectoryExists();
        }

        private void EnsureDirectoryExists()
        {
            if (!Directory.Exists(ConfigDirectory))
            {
                Directory.CreateDirectory(ConfigDirectory);
            }
        }

        /// <summary>
        /// Загружает конфигурацию из JSON-файла.
        /// </summary>
        public AppConfig LoadConfig()
        {
            lock (_fileLock)
            {
                // Если файла нет, создаем дефолтный
                if (!File.Exists(ConfigFilePath))
                {
                    return CreateDefaultConfig();
                }

                try
                {
                    string json = File.ReadAllText(ConfigFilePath);
                    var config = JsonSerializer.Deserialize<AppConfig>(json);
                    
                    // Если десериализовался как null (например, файл был пуст), то возвращаем дефолт
                    if (config == null)
                    {
                        return CreateDefaultConfig();
                    }

                    return config;
                }
                catch (JsonException)
                {
                    // Файл поврежден (сломан синтаксис JSON) - перезаписываем дефолтным
                    return CreateDefaultConfig();
                }
                catch (Exception ex)
                {
                    // Проблемы с доступом к файлу (например, эксклюзивная блокировка другим процессом)
                    // Возвращаем пустой конфиг в памяти, чтобы приложение не упало
                    System.Diagnostics.Debug.WriteLine($"Ошибка при загрузке конфига: {ex.Message}");
                    return new AppConfig(); 
                }
            }
        }

        /// <summary>
        /// Сохраняет конфигурацию в JSON-файл.
        /// </summary>
        public void SaveConfig(AppConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            lock (_fileLock)
            {
                EnsureDirectoryExists();
                
                try
                {
                    // Используем WriteIndented = true для красивого форматирования (чтобы было проще отлаживать)
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    string json = JsonSerializer.Serialize(config, options);
                    
                    File.WriteAllText(ConfigFilePath, json);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Ошибка при сохранении конфига: {ex.Message}");
                    // В реальном приложении здесь стоит пробросить ошибку выше или залогировать
                    throw;
                }
            }
        }

        /// <summary>
        /// Создает, сохраняет и возвращает настройки по умолчанию.
        /// </summary>
        private AppConfig CreateDefaultConfig()
        {
            var config = new AppConfig
            {
                BlockedWebsites = new System.Collections.Generic.List<string>(),
                BlockedProcesses = new System.Collections.Generic.List<string>(),
                CurrentMode = BlockingMode.None,
                BlockEndTime = null
            };
            
            // Сразу сохраняем на диск, чтобы файл появился
            SaveConfig(config);
            
            return config;
        }
    }
}
