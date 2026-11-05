using System.Windows;
using DhcpServer.App.ViewModels;

namespace DhcpServer.App.Views
{
    public partial class ConfirmChangesWindow : Window
    {
        public ConfirmChangesWindow(ConfirmChangesViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
        }

        private void Apply_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
