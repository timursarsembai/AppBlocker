using System;
using System.Diagnostics;
using System.Threading;

namespace AppBlocker.Watchdog
{
    /// <summary>
    /// Консольное приложение-сторож. 
    /// Оно должно быть скомпилировано как отдельный .exe файл и запускаться основной службой.
    /// Архитектура: Служба A запускает Сторожа B. Сторож B запускает Службу A, если та падает. 
    /// Это создает цикличную зависимость, которую крайне сложно разорвать без перезагрузки ПК.
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            // Watchdog принимает два аргумента при запуске из основной службы:
            // 1. PID (Process ID) основной службы
            // 2. Полный путь к exe-файлу основной службы (для перезапуска)
            if (args.Length < 2)
            {
                Console.WriteLine("Неверные аргументы. Требуется PID и путь к процессу.");
                return;
            }
            
            if (!int.TryParse(args[0], out int targetPid)) return;
            string targetPath = args[1];

            // Бесконечный цикл на случай, если пользователь попытается убить оба процесса скриптом
            while (true)
            {
                try
                {
                    // Пытаемся "прицепиться" к процессу основной службы
                    var targetProcess = Process.GetProcessById(targetPid);
                    
                    // Блокируем поток и ждем, пока основная служба не завершится (или ее не убьет юзер)
                    targetProcess.WaitForExit();
                    
                    // Если код дошел сюда — служба убита!
                    // Немедленно запускаем ее заново
                    targetPid = RestartService(targetPath);
                }
                catch (ArgumentException)
                {
                    // Исключение означает, что процесс с таким PID уже не существует (был убит до запуска Watchdog)
                    targetPid = RestartService(targetPath);
                }
                catch (Exception ex)
                {
                    // Логируем ошибку и ждем пару секунд во избежание спама
                    Thread.Sleep(2000);
                }
            }
        }

        private static int RestartService(string path)
        {
            try
            {
                // Запускаем основную службу
                var newProcess = Process.Start(path);
                return newProcess.Id; // Возвращаем новый PID для дальнейшего мониторинга
            }
            catch
            {
                // Если не удалось (например, файл удален), засыпаем на 1 секунду и пробуем снова
                Thread.Sleep(1000);
                return -1;
            }
        }
    }
}
