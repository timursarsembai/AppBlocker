using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using AppBlocker.Core.Configuration;
using AppBlocker.Core.Models;
using AppBlocker.UI.ViewModels;

namespace AppBlocker.UI
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer _idleTimer;
        private bool _isLocked = false;
        private readonly ConfigManager _configManager;

        public MainWindow()
        {
            InitializeComponent();
            _configManager = new ConfigManager();
            
            _idleTimer = new DispatcherTimer();
            _idleTimer.Interval = TimeSpan.FromSeconds(30);
            _idleTimer.Tick += IdleTimer_Tick;
            
            // Запускаем таймер, если защита включена
            CheckProtectionAndStartTimer();
        }

        private void CheckProtectionAndStartTimer()
        {
            var config = _configManager.LoadConfig();
            if (config.CurrentProtectionType != ProtectionType.None && !string.IsNullOrEmpty(config.ProtectionHash))
            {
                _idleTimer.Start();
            }
            else
            {
                _idleTimer.Stop();
                
                // Разблокируем интерфейс, если он был заблокирован, но без вызова рекурсивного UnlockApp
                if (_isLocked)
                {
                    _isLocked = false;
                    LockOverlay.Visibility = Visibility.Collapsed;
                    MainContent.Visibility = Visibility.Visible;
                }
            }
        }

        private void IdleTimer_Tick(object sender, EventArgs e)
        {
            // Таймер истек — блокируем окно
            LockApp();
        }

        private void Window_InputActivity(object sender, InputEventArgs e)
        {
            if (!_isLocked)
            {
                // Сбрасываем таймер при активности, если не заблокированы
                var config = _configManager.LoadConfig();
                if (config.CurrentProtectionType != ProtectionType.None)
                {
                    _idleTimer.Stop();
                    _idleTimer.Start();
                }
            }
        }

        private void Window_Activated(object sender, EventArgs e)
        {
            // При разворачивании окна из трея, если таймер истек, мы будем в состоянии _isLocked.
            // Также проверяем конфиг (вдруг защиту только что включили)
            CheckProtectionAndStartTimer();
        }

        private void LockApp()
        {
            var config = _configManager.LoadConfig();
            if (config.CurrentProtectionType != ProtectionType.None && !string.IsNullOrEmpty(config.ProtectionHash))
            {
                _isLocked = true;
                LockOverlay.Visibility = Visibility.Visible;
                MainContent.Visibility = Visibility.Hidden;
                _idleTimer.Stop();
            }
        }

        private void UnlockApp()
        {
            _isLocked = false;
            LockOverlay.Visibility = Visibility.Collapsed;
            MainContent.Visibility = Visibility.Visible;
            CheckProtectionAndStartTimer();
        }

        private void UnlockBtn_Click(object sender, RoutedEventArgs e)
        {
            var config = _configManager.LoadConfig();
            if (config.CurrentProtectionType != ProtectionType.None && !string.IsNullOrEmpty(config.ProtectionHash))
            {
                var authVm = new AuthViewModel(config.CurrentProtectionType, config.ProtectionHash, config.RequireMathChallenge);
                var authWin = new AppBlocker.UI.Views.AuthWindow(authVm);
                authWin.Owner = this;
                
                if (authWin.ShowDialog() == true)
                {
                    UnlockApp();
                }
            }
            else
            {
                UnlockApp();
            }
        }
    }
}
