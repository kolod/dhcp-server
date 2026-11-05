using System.Windows;
using DhcpServer.App.Localization;
using DhcpServer.App.ViewModels;

namespace DhcpServer.App.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var version = (DataContext as MainViewModel)?.Version ?? string.Empty;
            MessageBox.Show(
                this,
                LanguageService.Format("About_Text", version),
                LanguageService.Get("Menu_About"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }
}
