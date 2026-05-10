using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using AppBlocker.Core.Configuration;
using AppBlocker.Core.Models;
using AppBlocker.Service.Engine;
using AppBlocker.Service.IPC;
using AppBlocker.Shared.IPC;

namespace AppBlocker.Service
{
    /// <summary>
    /// Основная фоновая служба блокировщика. Связывает все движки (Hosts, Process) и IPC вместе.
    /// Запускается как Windows Service с правами SYSTEM.
    /// </summary>
    public class BlockerWorker : BackgroundService
    {
        private readonly ConfigManager _configManager;
        private readonly ProcessBlocker _processBlocker;
        private readonly HostsFileBlocker _hostsBlocker;
        private readonly IpcServer _ipcServer;

        private AppConfig _currentConfig;

        public BlockerWorker()
        {
            _configManager = new ConfigManager();
            _processBlocker = new ProcessBlocker();
            _hostsBlocker = new HostsFileBlocker();
            
            // Пробрасываем метод обработки команд в IPC-сервер
            _ipcServer = new IpcServer(HandleIpcRequest);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // 1. При старте сервиса читаем конфиг (вдруг сервис упал и перезапустился во время сессии)
            _currentConfig = _configManager.LoadConfig();
            ApplyConfig(_currentConfig);

            // 2. Запускаем сервер прослушивания команд от UI в отдельном фоновом потоке
            _ = Task.Run(() => _ipcServer.StartListeningAsync(stoppingToken), stoppingToken);

            // 3. Основной цикл: проверяем, не закончилось ли время текущей блокировки
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (_currentConfig.CurrentMode != BlockingMode.None && 
                    _currentConfig.BlockEndTime.HasValue && 
                    DateTime.UtcNow >= _currentConfig.BlockEndTime.Value)
                {
                    // Время истекло, снимаем блокировки
                    StopSession();
                }
            }
        }

        /// <summary>
        /// Применяет конфигурацию: запускает или останавливает движки.
        /// </summary>
        private void ApplyConfig(AppConfig config)
        {
            if (config.CurrentMode != BlockingMode.None)
            {
                // Включаем мониторинг процессов
                _processBlocker.UpdateBlacklist(config.BlockedProcesses);
                _processBlocker.StartMonitoring();
                
                // Включаем блокировку сайтов
                _hostsBlocker.BlockDomains(config.BlockedWebsites);
            }
            else
            {
                // Выключаем всё
                _processBlocker.StopMonitoringAsync().Wait();
                _hostsBlocker.ClearAllBlocks();
            }
        }

        private void StopSession()
        {
            _currentConfig.CurrentMode = BlockingMode.None;
            _currentConfig.BlockEndTime = null;
            _configManager.SaveConfig(_currentConfig);
            
            ApplyConfig(_currentConfig);
        }

        /// <summary>
        /// Центральный обработчик команд от UI.
        /// </summary>
        private IpcResponse HandleIpcRequest(IpcRequest request)
        {
            try
            {
                switch (request.Action)
                {
                    case "GetStatus":
                        return new IpcResponse 
                        { 
                            IsSuccess = true, 
                            Payload = JsonSerializer.Serialize(new { 
                                Mode = _currentConfig.CurrentMode, 
                                EndTime = _currentConfig.BlockEndTime 
                            }) 
                        };

                    case "StartSession":
                        // Парсим параметры: { "Mode": 3, "DurationMinutes": 60 }
                        var options = JsonSerializer.Deserialize<JsonElement>(request.Payload);
                        var mode = (BlockingMode)options.GetProperty("Mode").GetInt32();
                        var minutes = options.GetProperty("DurationMinutes").GetInt32();
                        
                        _currentConfig.CurrentMode = mode;
                        _currentConfig.BlockEndTime = DateTime.UtcNow.AddMinutes(minutes);
                        
                        _configManager.SaveConfig(_currentConfig);
                        ApplyConfig(_currentConfig);
                        
                        return new IpcResponse { IsSuccess = true };

                    case "AddWebsite":
                        // Добавление сайта "на лету"
                        var site = JsonSerializer.Deserialize<string>(request.Payload);
                        if (!_currentConfig.BlockedWebsites.Contains(site))
                        {
                            _currentConfig.BlockedWebsites.Add(site);
                            _configManager.SaveConfig(_currentConfig);
                            
                            // Если сессия активна, сразу блокируем новый сайт
                            if (_currentConfig.CurrentMode != BlockingMode.None)
                            {
                                _hostsBlocker.BlockDomains(new[] { site });
                            }
                        }
                        return new IpcResponse { IsSuccess = true };

                    case "StopSession":
                        StopSession();
                        return new IpcResponse { IsSuccess = true };

                    default:
                        return new IpcResponse { IsSuccess = false, ErrorMessage = "Неизвестная команда." };
                }
            }
            catch (Exception ex)
            {
                return new IpcResponse { IsSuccess = false, ErrorMessage = $"Ошибка сервера: {ex.Message}" };
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            // При остановке/обновлении самой Windows Service важно корректно всё очистить
            await _processBlocker.StopMonitoringAsync();
            _processBlocker.Dispose();
            _hostsBlocker.ClearAllBlocks();
            
            await base.StopAsync(cancellationToken);
        }
    }
}
