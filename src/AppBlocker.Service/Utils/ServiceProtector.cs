using System;
using System.Diagnostics;

namespace AppBlocker.Service.Utils
{
    /// <summary>
    /// Утилита для защиты Windows-службы от остановки пользователем или Администратором.
    /// </summary>
    public static class ServiceProtector
    {
        /* 
           ВАЖНОЕ ЗАМЕЧАНИЕ ПО АРХИТЕКТУРЕ:
           В старом .NET Framework была возможность использовать классы из System.Security.AccessControl 
           вместе с System.ServiceProcess для изменения ACL. 
           Однако в современных .NET (Core / 5 / 8) пространство имен System.ServiceProcess устарело, 
           и прямого API для изменения прав служб нет (требуются сложные P/Invoke вызовы к advapi32.dll 
           через функции QueryServiceObjectSecurity и SetServiceObjectSecurity).

           Гораздо более надежный, чистый и современный способ изменить Access Control List службы — 
           использовать системную утилиту 'sc.exe' с передачей SDDL (Security Descriptor Definition Language) строки.
        */

        /// <summary>
        /// Изменяет права доступа (ACL) к службе, запрещая Администраторам её останавливать.
        /// </summary>
        /// <param name="serviceName">Системное имя службы (например, "AppBlockerSvc").</param>
        public static void ProtectService(string serviceName)
        {
            try
            {
                // Разбор SDDL строки:
                // D: - это DACL (Discretionary Access Control List)
                // (A;;CCLCSWRPWPDTLOCRRC;;;SY) - Разрешаем (A) системе (SY) всё (Start, Stop, Write, Delete).
                // (A;;CCLCSWLOCRRC;;;BA)       - Разрешаем (A) Администраторам (BA) ТОЛЬКО чтение статуса. 
                //                                Мы убрали у BA флаги RP (Start), WP (Stop), DT (Pause).
                
                string protectedSddl = "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCLCSWLOCRRC;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)";

                RunScCommand($"sdset \"{serviceName}\" \"{protectedSddl}\"");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при защите службы: {ex.Message}");
            }
        }
        
        /// <summary>
        /// Возвращает стандартные права Администраторам (нужно вызывать программно перед удалением/обновлением приложения).
        /// </summary>
        public static void UnprotectService(string serviceName)
        {
            try
            {
                // Стандартный SDDL Windows-служб. Здесь у BA (Built-in Administrators) есть полные права:
                // (A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)
                
                string defaultSddl = "D:(A;;CCLCSWRPWPDTLOCRRC;;;SY)(A;;CCDCLCSWRPWPDTLOCRSDRCWDWO;;;BA)(A;;CCLCSWLOCRRC;;;IU)(A;;CCLCSWLOCRRC;;;SU)";

                RunScCommand($"sdset \"{serviceName}\" \"{defaultSddl}\"");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Ошибка при снятии защиты со службы: {ex.Message}");
            }
        }

        private static void RunScCommand(string arguments)
        {
            var processInfo = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(processInfo);
            process?.WaitForExit(5000);
        }
    }
}
