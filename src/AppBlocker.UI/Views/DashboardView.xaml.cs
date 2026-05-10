using System.Windows.Controls;
using System.Windows.Input;

namespace AppBlocker.UI.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();
        }

        private void HoursUp_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is AppBlocker.UI.ViewModels.DashboardViewModel vm)
            {
                vm.InputHours = System.Math.Min(23, vm.InputHours + 1);
            }
        }

        private void HoursDown_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is AppBlocker.UI.ViewModels.DashboardViewModel vm)
            {
                vm.InputHours = System.Math.Max(0, vm.InputHours - 1);
            }
        }

        private void MinutesUp_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is AppBlocker.UI.ViewModels.DashboardViewModel vm)
            {
                vm.InputMinutes = System.Math.Min(59, vm.InputMinutes + 1);
            }
        }

        private void MinutesDown_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is AppBlocker.UI.ViewModels.DashboardViewModel vm)
            {
                vm.InputMinutes = System.Math.Max(0, vm.InputMinutes - 1);
            }
        }

        private void Hours_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0) HoursUp_Click(null, null);
            else HoursDown_Click(null, null);
            e.Handled = true;
        }

        private void Minutes_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0) MinutesUp_Click(null, null);
            else MinutesDown_Click(null, null);
            e.Handled = true;
        }
    }
}
