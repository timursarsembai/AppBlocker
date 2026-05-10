using System.Windows.Controls;

namespace AppBlocker.UI.Views
{
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();
        }

        private void SetProtectionBtn_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (!string.IsNullOrEmpty(ProtectionInput.Password))
            {
                if (DataContext is AppBlocker.UI.ViewModels.SettingsViewModel vm)
                {
                    vm.SetProtectionHashCommand.Execute(ProtectionInput.Password);
                }

                // Показываем уведомление (можно через MessageBox или кастомное)
                System.Windows.MessageBox.Show("Защита успешно установлена!", "Успех", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                // ProtectionInput.Password = string.Empty; // Удалено по просьбе пользователя
            }
        }
    }
}
