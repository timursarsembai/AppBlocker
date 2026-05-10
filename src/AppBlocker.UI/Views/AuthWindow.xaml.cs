using System.Windows;
using System.Windows.Input;
using AppBlocker.UI.ViewModels;

namespace AppBlocker.UI.Views
{
    public partial class AuthWindow : Window
    {
        private AuthViewModel _viewModel;

        public AuthWindow(AuthViewModel viewModel)
        {
            InitializeComponent();
            _viewModel = viewModel;
            DataContext = _viewModel;

            _viewModel.OnSuccess = () =>
            {
                DialogResult = true;
                Close();
            };

            _viewModel.OnCancel = () =>
            {
                DialogResult = false;
                Close();
            };

            Loaded += (s, e) => PasswordInput.Focus();
        }

        private void Input_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                Submit_Click(sender, null);
            }
        }

        private void Submit_Click(object sender, RoutedEventArgs e)
        {
            if (_viewModel.SubmitCommand.CanExecute(PasswordInput.Password))
            {
                _viewModel.SubmitCommand.Execute(PasswordInput.Password);
                
                // Если перешли на стадию математики, очищаем пароль (на всякий случай)
                if (_viewModel.IsMathStage)
                {
                    PasswordInput.Password = string.Empty;
                }
            }
        }
    }
}
