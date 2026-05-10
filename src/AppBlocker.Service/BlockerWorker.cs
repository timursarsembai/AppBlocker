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
            // 1. При старте сервиса читаем конфиг
            _currentConfig = _configManager.LoadConfig();
            ApplyConfig(_currentConfig);

            // 2. Запускаем сервер прослушивания команд от UI
            _ = Task.Run(() => _ipcServer.StartListeningAsync(stoppingToken), stoppingToken);

            // 3. Основной цикл: проверяем расписание и таймер
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                // Перечитываем конфиг, чтобы подхватить изменения из UI и проверить расписание
                _currentConfig = _configManager.LoadConfig();
                ApplyConfig(_currentConfig);

                if (_currentConfig.CurrentMode != BlockingMode.None && 
                    _currentConfig.BlockEndTime.HasValue && 
                    DateTime.UtcNow >= _currentConfig.BlockEndTime.Value)
                {
                    StopSession();
                }
            }
        }

        /// <summary>
        /// Применяет конфигурацию: запускает или останавливает движки на основе АКТИВНЫХ правил.
        /// </summary>
        private void ApplyConfig(AppConfig config)
        {
            var activeWebsites = GetActiveBlockedWebsites(config);
            var activeProcesses = GetActiveBlockedProcesses(config);

            // Процессы
            _processBlocker.UpdateBlacklist(activeProcesses);
            if (activeProcesses.Count > 0)
            {
                _processBlocker.StartMonitoring();
            }
            else
            {
                _processBlocker.StopMonitoringAsync().Wait();
            }

            // Сайты (Hosts)
            if (activeWebsites.Count > 0)
            {
                _hostsBlocker.BlockDomains(activeWebsites);
            }
            else
            {
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

        private List<string> GetActiveBlockedWebsites(AppConfig config)
        {
            var list = new List<string>();
            foreach (var rule in config.WebsiteBlockRules ?? new List<BlockRule>())
            {
                if (IsRuleActive(rule, config)) list.Add(rule.Name);
            }
            return list;
        }

        private List<string> GetActiveBlockedProcesses(AppConfig config)
        {
            var list = new List<string>();
            foreach (var rule in config.ProcessBlockRules ?? new List<BlockRule>())
            {
                if (IsRuleActive(rule, config)) list.Add(rule.Name);
            }
            return list;
        }

        private bool IsRuleActive(BlockRule rule, AppConfig config)
        {
            switch (rule.Type)
            {
                case BlockType.Always:
                    return true;
                case BlockType.Timer:
                    return config.CurrentMode != BlockingMode.None;
                case BlockType.Schedule:
                    if (rule.StartTime.HasValue && rule.EndTime.HasValue)
                    {
                        var now = DateTime.Now.TimeOfDay;
                        if (rule.StartTime.Value <= rule.EndTime.Value)
                        {
                            return now >= rule.StartTime.Value && now <= rule.EndTime.Value;
                        }
                        else
                        {
                            return now >= rule.StartTime.Value || now <= rule.EndTime.Value;
                        }
                    }
                    return false;
                default:
                    return false;
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
