using System.Windows;
using DhcpServer.App.Localization;
using DhcpServer.App.ViewModels;
using DhcpServer.Core.Addressing;

namespace DhcpServer.App.Views
{
    /// <summary>The real consent dialogs (modal, owned by the main window).</summary>
    internal sealed class WpfUserDialogs : IUserDialogs
    {
        public bool ConfirmChanges(ChangePreview preview, string adapterName)
        {
            var window = new ConfirmChangesWindow(new ConfirmChangesViewModel(preview, adapterName));
            var owner = Application.Current?.MainWindow;
            if (owner != null && owner.IsVisible)
            {
                window.Owner = owner;
            }
            else
            {
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            return window.ShowDialog() == true;
        }

        public bool ConfirmDiscard()
        {
            var owner = Application.Current?.MainWindow;
            var text = LanguageService.Get("Recovery_DiscardConfirm");
            var title = LanguageService.Get("Recovery_Discard");
            var result = owner != null && owner.IsVisible
                ? MessageBox.Show(owner, text, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
                : MessageBox.Show(text, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            return result == MessageBoxResult.Yes;
        }
    }
}
