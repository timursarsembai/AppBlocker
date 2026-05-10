using System.Windows;

namespace AppBlocker.UI.Views
{
    public partial class LowFrictionUnlockView : Window
    {
        public LowFrictionUnlockView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Обработчик события попытки вставки текста в TextBox из буфера обмена.
        /// </summary>
        private void TextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            // Жестко блокируем вставку текста. 
            // Это сработает, даже если пользователь попробует обойти горячие клавиши через сторонний софт.
            e.CancelCommand();
            e.Handled = true;
        }
    }
}
