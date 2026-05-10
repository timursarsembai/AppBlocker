using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace AppBlocker.UI.Views
{
    public partial class ToastWindow : Window
    {
        private readonly DispatcherTimer _autoCloseTimer;

        public ToastWindow(string processName)
        {
            InitializeComponent();

            ProcessNameText.Text = processName;

            // Запускаем таймер для автозакрытияй нижний угол экрана
            var workArea = SystemParameters.WorkArea;
            Left = workArea.Right - Width - 10;
            Top = workArea.Bottom - 200;

            // Анимация появления (slide up + fade in)
            Opacity = 0;
            var slideFrom = Top + 30;
            Top = slideFrom;

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var slideUp = new DoubleAnimation(slideFrom, slideFrom - 30, TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            BeginAnimation(OpacityProperty, fadeIn);
            BeginAnimation(TopProperty, slideUp);

            // Автозакрытие через 6 секунд
            _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            _autoCloseTimer.Tick += (s, e) => FadeOutAndClose();
            _autoCloseTimer.Start();

            // Закрытие по клику
            MouseLeftButtonDown += (s, e) => FadeOutAndClose();
        }

        private void FadeOutAndClose()
        {
            _autoCloseTimer?.Stop();

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (s, e) => Close();
            BeginAnimation(OpacityProperty, fadeOut);
        }
    }
}
