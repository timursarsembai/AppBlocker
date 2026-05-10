using System;
using System.ComponentModel;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AppBlocker.Core.Models;
using AppBlocker.UI.IPC;

namespace AppBlocker.UI.Views
{
    public partial class LockScreenWindow : Window
    {
        private bool _canClose = false;
        private BlockingMode _currentMode;
        private string _challengeString;

        // Список привычек (в реальном проекте должен тянуться из конфигурации/базы данных)
        private readonly string[] _randomHabits = new[]
        {
            "Выпейте стакан чистой воды",
            "Сделайте 10 глубоких вдохов",
            "Потянитесь и разомните шею",
            "Посмотрите в окно вдаль на 20 секунд",
            "Встаньте и пройдитесь по комнате"
        };

        public LockScreenWindow(BlockingMode mode)
        {
            InitializeComponent();
            _currentMode = mode;
            SetupView();
        }

        private void SetupView()
        {
            if (_currentMode == BlockingMode.LowerBarrier)
            {
                StrictBlockPanel.Visibility = Visibility.Collapsed;
                LowerBarrierPanel.Visibility = Visibility.Visible;
                
                // Генерируем 40-символьную строку
                _challengeString = GenerateRandomString(40);
                ChallengeTextBlock.Text = _challengeString;
                
                // Фокус на поле ввода
                InputTextBox.Focus();
            }
            else
            {
                // Режимы ClarityWindow или Hyperfocus
                LowerBarrierPanel.Visibility = Visibility.Collapsed;
                StrictBlockPanel.Visibility = Visibility.Visible;
                
                // Выбираем случайную привычку из Дофаминового меню
                var random = new Random();
                DopamineHabitTextBlock.Text = _randomHabits[random.Next(_randomHabits.Length)];
            }
        }

        /// <summary>
        /// Генератор сложной строки для перепечатывания
        /// </summary>
        private string GenerateRandomString(int length)
        {
            const string validChars = "ABCDEFGHJKLMNOPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%^&*()-_=+";
            var result = new char[length];
            for (int i = 0; i < length; i++)
            {
                result[i] = validChars[RandomNumberGenerator.GetInt32(validChars.Length)];
            }
            return new string(result);
        }

        /// <summary>
        /// Обработчик ввода текста. Кнопка разблокировки недоступна до 100% совпадения.
        /// </summary>
        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_currentMode == BlockingMode.LowerBarrier)
            {
                bool isMatch = string.Equals(InputTextBox.Text, _challengeString, StringComparison.Ordinal);
                UnlockButton.IsEnabled = isMatch;
            }
        }

        /// <summary>
        /// Полностью блокирует возможность закрытия окна через Alt+F4 или крестик
        /// </summary>
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (!_canClose)
            {
                e.Cancel = true;
            }
        }

        /// <summary>
        /// Жесткая блокировка вставки из буфера обмена (Ctrl+V)
        /// </summary>
        private void InputTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            e.CancelCommand();
            e.Handled = true;
        }

        /// <summary>
        /// Кнопка "Разблокировать" (появляется только после правильного ввода текста)
        /// </summary>
        private async void UnlockButton_Click(object sender, RoutedEventArgs e)
        {
            // Отправляем команду сервису на досрочную остановку
            var ipcClient = new IpcClient();
            await ipcClient.SendRequestAsync("StopSession");

            // Только после этого разрешаем окну закрыться
            _canClose = true;
            this.Close();
        }

        /// <summary>
        /// Кнопка "Вернуться к работе" (передумал сдаваться)
        /// </summary>
        private void KeepWorkingButton_Click(object sender, RoutedEventArgs e)
        {
            // Не снимаем блокировку, просто убираем окно
            _canClose = true;
            this.Close();
        }

        /// <summary>
        /// Кнопка "Понятно" для строгих режимов
        /// </summary>
        private void OkayButton_Click(object sender, RoutedEventArgs e)
        {
            // Закрываем окно-уведомление. 
            // Процесс остался заблокированным/убитым фоновой службой.
            _canClose = true;
            this.Close();
        }
    }
}
