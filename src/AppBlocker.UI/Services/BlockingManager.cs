using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AppBlocker.Core.Configuration;
using AppBlocker.Core.Models;
using AppBlocker.Service.Engine;
using AppBlocker.UI.Views;

namespace AppBlocker.UI.Services
{
    /// <summary>
    /// Менеджер блокировки, работающий внутри UI-процесса.
    /// Координирует все движки: hosts-блокировка, процессы, HTTP-сервер страницы блокировки, тосты.
    /// </summary>
    public class BlockingManager : IDisposable
    {
        private readonly ConfigManager _configManager;
        private readonly HostsFileBlocker _hostsBlocker;
        private readonly ProcessBlocker _processBlocker;
        private readonly BlockPageServer _blockPageServer;
        private readonly Random _random = new Random();

        private CancellationTokenSource _cts;
        private Task _pollingTask;
        private bool _isBlocking;

        // Антиспам: не показывать тост для одного и того же процесса чаще, чем раз в 30 сек
        private readonly Dictionary<string, DateTime> _lastToastTime = new(StringComparer.OrdinalIgnoreCase);

        public BlockingManager()
        {
            _configManager = new ConfigManager();
            _hostsBlocker = new HostsFileBlocker();
            _processBlocker = new ProcessBlocker();
            _blockPageServer = new BlockPageServer();

            // Подписываемся на убийство процессов для показа тостов
            _processBlocker.OnProcessKilled = OnProcessKilled;
        }

        public void Start()
        {
            if (_pollingTask != null && !_pollingTask.IsCompleted) return;
            _cts = new CancellationTokenSource();
            _pollingTask = Task.Run(() => PollingLoopAsync(_cts.Token));
        }

        private async Task PollingLoopAsync(CancellationToken token)
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    try
                    {
                        var config = _configManager.LoadConfig();

                        if (config.CurrentMode != BlockingMode.None && config.BlockEndTime.HasValue)
                        {
                            var remaining = config.BlockEndTime.Value.ToUniversalTime() - DateTime.UtcNow;

                            if (remaining.TotalSeconds > 0)
                            {
                                ApplyBlocks(config);
                            }
                            else
                            {
                                RemoveBlocks();
                                config.CurrentMode = BlockingMode.None;
                                config.BlockEndTime = null;
                                _configManager.SaveConfig(config);
                            }
                        }
                        else if (_isBlocking)
                        {
                            RemoveBlocks();
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[BlockingManager] Ошибка: {ex.Message}");
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private void ApplyBlocks(AppConfig config)
        {
            // Блокируем сайты через hosts
            if (config.BlockedWebsites != null && config.BlockedWebsites.Count > 0)
            {
                try
                {
                    _hostsBlocker.SyncDomains(config.BlockedWebsites);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[HostsBlocker] {ex.Message}");
                }

                // Запускаем HTTP-сервер страницы блокировки
                _blockPageServer.Start();
            }

            // Блокируем процессы
            if (config.BlockedProcesses != null && config.BlockedProcesses.Count > 0)
            {
                _processBlocker.UpdateBlacklist(config.BlockedProcesses);
                _processBlocker.StartMonitoring();
            }

            _isBlocking = true;
        }

        private void RemoveBlocks()
        {
            try { _hostsBlocker.ClearAllBlocks(flushDns: false); }
            catch (Exception ex) { Debug.WriteLine($"[HostsBlocker] Ошибка при снятии: {ex.Message}"); }

            _blockPageServer.Stop();
            _processBlocker.StopMonitoringAsync().GetAwaiter().GetResult();
            _isBlocking = false;
        }

        /// <summary>
        /// Вызывается из ProcessBlocker (из фонового потока) при убийстве процесса.
        /// Показывает тост-уведомление на UI-потоке с рандомной привычкой.
        /// </summary>
        private void OnProcessKilled(string processName)
        {
            // Антиспам: не больше 1 тоста на процесс за 30 секунд
            lock (_lastToastTime)
            {
                if (_lastToastTime.TryGetValue(processName, out var lastTime) &&
                    (DateTime.UtcNow - lastTime).TotalSeconds < 5)
                {
                    return;
                }
                _lastToastTime[processName] = DateTime.UtcNow;
            }

            // Показываем тост на UI-потоке
            Application.Current?.Dispatcher?.BeginInvoke(() =>
            {
                try
                {
                    var toast = new ToastWindow(processName);
                    toast.Show();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Toast] Ошибка: {ex.Message}");
                }
            });
        }

        public void Dispose()
        {
            _cts?.Cancel();
            try { _pollingTask?.GetAwaiter().GetResult(); } catch { }

            // Снимаем все блокировки при выходе
            try { _hostsBlocker.ClearAllBlocks(flushDns: true); } catch { }
            _blockPageServer?.Dispose();
            _processBlocker?.Dispose();
            _cts?.Dispose();
        }
    }
}
