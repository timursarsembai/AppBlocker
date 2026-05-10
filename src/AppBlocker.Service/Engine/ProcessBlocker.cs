using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AppBlocker.Service.Engine
{
    /// <summary>
    /// Движок мониторинга и блокировки десктопных приложений (.exe).
    /// </summary>
    public class ProcessBlocker : IDisposable
    {
        // Храним имена процессов. Используем HashSet с игнорированием регистра для мгновенного поиска (O(1)).
        private readonly HashSet<string> _blacklistedProcesses;
        
        // ReaderWriterLockSlim эффективнее обычного lock (Monitor), так как позволяет
        // читать список одновременно из нескольких потоков (если потребуется) и блокирует только при записи.
        private readonly ReaderWriterLockSlim _lock;
        
        /// <summary>
        /// Вызывается при убийстве процесса. Параметр — имя процесса.
        /// </summary>
        public Action<string> OnProcessKilled { get; set; }

        private CancellationTokenSource _cts;
        private Task _monitoringTask;

        public ProcessBlocker()
        {
            _blacklistedProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            _lock = new ReaderWriterLockSlim();
        }

        /// <summary>
        /// Устанавливает новый список запрещенных процессов. Можно обновлять "на лету" без остановки мониторинга.
        /// </summary>
        /// <param name="processNames">Список процессов, например: "telegram.exe", "discord"</param>
        public void UpdateBlacklist(IEnumerable<string> processNames)
        {
            if (processNames == null) return;

            _lock.EnterWriteLock();
            try
            {
                _blacklistedProcesses.Clear();
                foreach (var name in processNames)
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    // Оптимизация: .NET Process.GetProcessesByName ожидает имя без расширения .exe
                    string cleanName = name.Trim();
                    if (cleanName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        cleanName = cleanName.Substring(0, cleanName.Length - 4);
                    }
                    
                    _blacklistedProcesses.Add(cleanName);
                }
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        /// <summary>
        /// Запускает фоновый воркер для проверки процессов раз в секунду.
        /// </summary>
        public void StartMonitoring()
        {
            // Защита от повторного запуска
            if (_monitoringTask != null && !_monitoringTask.IsCompleted)
            {
                return; 
            }

            _cts = new CancellationTokenSource();
            
            // Запускаем в фоновом пуле потоков
            _monitoringTask = Task.Run(() => MonitorLoopAsync(_cts.Token));
        }

        /// <summary>
        /// Корректно останавливает фоновый мониторинг.
        /// </summary>
        public async Task StopMonitoringAsync()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                
                if (_monitoringTask != null)
                {
                    try
                    {
                        await _monitoringTask;
                    }
                    catch (OperationCanceledException) { /* Ожидаемое исключение при отмене */ }
                }
                
                _cts.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// Основной цикл мониторинга.
        /// </summary>
        private async Task MonitorLoopAsync(CancellationToken token)
        {
            // ОПТИМИЗАЦИЯ CPU/RAM: PeriodicTimer (появился в .NET 6) работает эффективнее, 
            // чем Task.Delay. Он не создает новый объект Task на каждой итерации цикла, 
            // что снижает нагрузку на сборщик мусора (GC).
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));

            try
            {
                // Ждем следующего тика (1 сек). Если сработает токен отмены, вылетит OperationCanceledException
                while (await timer.WaitForNextTickAsync(token))
                {
                    KillBlacklistedProcesses();
                }
            }
            catch (OperationCanceledException)
            {
                // Цикл прерван пользователем/системой, выходим молча
            }
        }

        private void KillBlacklistedProcesses()
        {
            _lock.EnterReadLock();
            try
            {
                if (_blacklistedProcesses.Count == 0) return;

                // ОПТИМИЗАЦИЯ CPU:
                // Вызывать Process.GetProcesses() раз в секунду — это "тяжелая" операция,
                // так как Windows возвращает массив всех сотен процессов, аллоцируя много памяти.
                // Вместо этого мы делаем точечные запросы Process.GetProcessesByName() только
                // для тех приложений, которые есть в черном списке.
                foreach (var processName in _blacklistedProcesses)
                {
                    var activeInstances = Process.GetProcessesByName(processName);
                    
                    foreach (var process in activeInstances)
                    {
                        try
                        {
                            if (!process.HasExited)
                            {
                                string name = process.ProcessName;
                                process.Kill();
                                Debug.WriteLine($"[{DateTime.Now}] Убит процесс: {name}");
                                
                                try { OnProcessKilled?.Invoke(name); }
                                catch { }
                            }
                        }
                        catch (Exception)
                        {
                            // Игнорируем исключения (Access Denied).
                            // Возникает, если пытаемся убить системный процесс или антивирус,
                            // к которому даже у System/Admin нет прямого доступа.
                        }
                        finally
                        {
                            // ОПТИМИЗАЦИЯ RAM (КРИТИЧНО):
                            // Класс Process работает с неуправляемыми ресурсами (дескрипторами ОС).
                            // Если не вызывать Dispose(), в сервисе быстро начнется утечка дескрипторов (Handle Leak).
                            process.Dispose();
                        }
                    }
                }
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }

        public void Dispose()
        {
            // Синхронный вызов очистки при удалении объекта
            StopMonitoringAsync().GetAwaiter().GetResult();
            _lock?.Dispose();
        }
    }
}
