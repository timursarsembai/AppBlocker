using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace AppBlocker.Service.Engine
{
    /// <summary>
    /// Движок блокировки веб-сайтов через модификацию системного файла hosts Windows.
    /// </summary>
    public class HostsFileBlocker
    {
        // Путь к системному файлу hosts: C:\Windows\System32\drivers\etc\hosts
        private static readonly string HostsFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System), 
            @"drivers\etc\hosts");
            
        private const string RedirectIp = "127.0.0.1";
        
        // Уникальный маркер для поиска и удаления именно наших записей, 
        // чтобы не трогать пользовательские настройки файла hosts.
        private const string MarkerComment = "# AppBlocker";

        /// <summary>
        /// Полная синхронизация: удаляет старые записи AppBlocker и записывает актуальный список.
        /// Гарантирует идемпотентность — можно вызывать повторно без побочных эффектов.
        /// </summary>
        public void SyncDomains(IEnumerable<string> domains)
        {
            if (domains == null) return;
            var domainList = domains.ToList();

            try
            {
                var hostsContent = File.ReadAllLines(HostsFilePath).ToList();
                
                // Шаг 1: Убираем все старые записи AppBlocker
                int removedCount = hostsContent.RemoveAll(line => line.Contains(MarkerComment));

                // Шаг 2: Добавляем актуальные записи
                bool hasNewEntries = false;
                foreach (var domain in domainList)
                {
                    string cleanDomain = domain.Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(cleanDomain)) continue;

                    hostsContent.Add($"{RedirectIp} {cleanDomain} {MarkerComment}");
                    hasNewEntries = true;

                    // Блокируем версию с www
                    if (!cleanDomain.StartsWith("www."))
                    {
                        hostsContent.Add($"{RedirectIp} www.{cleanDomain} {MarkerComment}");
                    }
                }

                // Шаг 3: Записываем только если что-то изменилось
                if (removedCount > 0 || hasNewEntries)
                {
                    File.WriteAllLines(HostsFilePath, hostsContent);
                    
                    if (hasNewEntries)
                    {
                        // Агрессивный сброс DNS при ДОБАВЛЕНИИ блокировок
                        FlushAllDnsCaches();
                    }
                    // При УДАЛЕНИИ записей НЕ сбрасываем DNS — 
                    // пусть 127.0.0.1 остаётся в кэше браузера как можно дольше
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException("Нет прав Администратора для изменения файла hosts.", ex);
            }
            catch (IOException ex)
            {
                throw new InvalidOperationException($"Ошибка доступа к файлу hosts: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Блокирует указанный список доменов, добавляя их в файл hosts.
        /// </summary>
        public void BlockDomains(IEnumerable<string> domains)
        {
            // Используем SyncDomains для надежности
            SyncDomains(domains);
        }

        /// <summary>
        /// Разблокирует указанный список доменов, удаляя их из файла hosts.
        /// </summary>
        public void UnblockDomains(IEnumerable<string> domains)
        {
            if (domains == null || !domains.Any()) return;

            try
            {
                var hostsContent = File.ReadAllLines(HostsFilePath).ToList();
                bool isModified = false;

                foreach (var domain in domains)
                {
                    string cleanDomain = domain.Trim().ToLowerInvariant();
                    string target = $" {cleanDomain} {MarkerComment}";
                    string targetWww = $" www.{cleanDomain} {MarkerComment}";

                    int removedCount = hostsContent.RemoveAll(line => 
                        line.Contains(target) || line.Contains(targetWww));

                    if (removedCount > 0) isModified = true;
                }

                if (isModified)
                {
                    File.WriteAllLines(HostsFilePath, hostsContent);
                    // НЕ сбрасываем DNS — пусть 127.0.0.1 остаётся в кэше
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException("Нет прав Администратора для изменения файла hosts.", ex);
            }
        }

        /// <summary>
        /// Полностью очищает все блокировки AppBlocker из файла hosts.
        /// </summary>
        /// <param name="flushDns">Если true, сбрасывает DNS-кэш после очистки (для финального разблокирования)</param>
        public void ClearAllBlocks(bool flushDns = false)
        {
            try
            {
                var hostsContent = File.ReadAllLines(HostsFilePath).ToList();
                int removedCount = hostsContent.RemoveAll(line => line.Contains(MarkerComment));

                if (removedCount > 0)
                {
                    File.WriteAllLines(HostsFilePath, hostsContent);
                    
                    if (flushDns)
                    {
                        FlushAllDnsCaches();
                    }
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                throw new InvalidOperationException("Нет прав Администратора для изменения файла hosts.", ex);
            }
        }

        /// <summary>
        /// Агрессивный сброс всех DNS-кэшей: Windows + браузеры.
        /// </summary>
        private void FlushAllDnsCaches()
        {
            try
            {
                // 1. Стандартный сброс Windows DNS
                RunHidden("ipconfig", "/flushdns");

                // 2. Перезапуск службы DNS Client — принудительно очищает весь кэш на уровне ОС
                RunHidden("net", "stop dnscache");
                RunHidden("net", "start dnscache");

                // 3. Сброс кэша Winsock (помогает с некоторыми браузерами)
                RunHidden("netsh", "winsock reset catalog");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Ошибка при сбросе DNS: {ex.Message}");
            }
        }

        private void RunHidden(string fileName, string arguments)
        {
            try
            {
                var processInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using (var process = Process.Start(processInfo))
                {
                    process?.WaitForExit(5000);
                }
            }
            catch { }
        }
    }
}
