using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Windows.Input;

namespace AppBlocker.UI.ViewModels
{
    /// <summary>
    /// ViewModel для окна досрочного снятия блокировки (Режим "Снижение барьера").
    /// </summary>
    public class LowFrictionUnlockViewModel : INotifyPropertyChanged
    {
        private string _challengeString;
        private string _userInput;
        private bool _isUnlockSuccessful;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Строка, которую пользователь должен перепечатать.
        /// </summary>
        public string ChallengeString
        {
            get => _challengeString;
            private set
            {
                _challengeString = value;
                OnPropertyChanged();
            }
        }

        /// <summary>
        /// Текст, вводимый пользователем.
        /// </summary>
        public string UserInput
        {
            get => _userInput;
            set
            {
                _userInput = value;
                OnPropertyChanged();
                
                // Проверяем совпадение при каждом вводе символа
                CheckMatch();
            }
        }

        /// <summary>
        /// Флаг успешного прохождения проверки.
        /// </summary>
        public bool IsUnlockSuccessful
        {
            get => _isUnlockSuccessful;
            private set
            {
                _isUnlockSuccessful = value;
                OnPropertyChanged();
            }
        }

        // Команда, которая вызывается, когда блокировка снята
        public ICommand UnlockCommand { get; }

        public LowFrictionUnlockViewModel()
        {
            // Генерируем строку при инициализации ViewModel
            ChallengeString = GenerateRandomString(40);
            
            // Здесь должна быть ваша реализация RelayCommand/DelegateCommand
            // Для примера используем простую заглушку
            UnlockCommand = new RelayCommand(ExecuteUnlock, CanExecuteUnlock);
        }

        /// <summary>
        /// Генерирует случайную строку заданной длины из букв, цифр и спецсимволов.
        /// Использует криптографически стойкий генератор.
        /// </summary>
        private string GenerateRandomString(int length)
        {
            // Исключены похожие символы (например l, 1, I, O, 0) для удобства ручного ввода
            const string validChars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789!@#$%^&*()-_=+";
            
            var result = new char[length];
            for (int i = 0; i < length; i++)
            {
                // RandomNumberGenerator.GetInt32 доступен с .NET Core 3.0 / .NET 5+
                result[i] = validChars[RandomNumberGenerator.GetInt32(validChars.Length)];
            }
            return new string(result);
        }

        private void CheckMatch()
        {
            // Точное совпадение с учетом регистра
            IsUnlockSuccessful = string.Equals(ChallengeString, UserInput, StringComparison.Ordinal);
        }

        private bool CanExecuteUnlock(object parameter)
        {
            return IsUnlockSuccessful;
        }

        private void ExecuteUnlock(object parameter)
        {
            // Вызов IPC клиента для отправки команды сервису на снятие блокировок
            // _ipcClient.SendUnlockCommand();
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // Простейшая реализация ICommand для примера (обычно используется из MVVM-фреймворка)
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute;
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object parameter) => _execute(parameter);
    }
}
