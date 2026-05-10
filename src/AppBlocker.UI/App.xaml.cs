using System.Windows;
using AppBlocker.UI.Services;

namespace AppBlocker.UI
{
    public partial class App : Application
    {
        private BlockingManager _blockingManager;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Запускаем менеджер блокировки при старте приложения.
            // Он будет каждые 2 секунды проверять конфиг и применять блокировки.
            _blockingManager = new BlockingManager();
            _blockingManager.Start();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Корректно снимаем все блокировки при выходе из приложения
            _blockingManager?.Dispose();
            base.OnExit(e);
        }
    }
}
