using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using DhcpServer.App.Localization;
using DhcpServer.App.ViewModels;
using DhcpServer.App.Views;
using DhcpServer.Core.Addressing;
using DhcpServer.Tests.Fakes;
using Xunit;

namespace DhcpServer.Tests.ViewModels
{
    /// <summary>Loads the real XAML with a real view model: catches XAML errors and broken bindings.</summary>
    [Collection("UiLanguage")]
    public class MainWindowSmokeTests
    {
        private sealed class BindingErrors : TraceListener
        {
            public List<string> Messages { get; } = new List<string>();

            public override void Write(string message)
            {
                Messages.Add(message);
            }

            public override void WriteLine(string message)
            {
                Messages.Add(message);
            }
        }

        [Fact]
        public void MainWindow_Loads_BindsWithoutErrors_AndShowsLeases()
        {
            Exception failure = null;
            var errors = new BindingErrors();
            var thread = new Thread(() =>
            {
                try
                {
                    PresentationTraceSources.Refresh();
                    PresentationTraceSources.DataBindingSource.Listeners.Add(errors);
                    PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

                    var app = new DhcpServer.App.App();
                    app.InitializeComponent();
                    app.ShutdownMode = ShutdownMode.OnExplicitShutdown; // closing the first test window must not end the app

                    using (var f = new ViewModelFixture())
                    {
                        f.Select("eth0");
                        f.StartAndWait();
                        var endpoint = f.Session.Endpoints.Last;
                        endpoint.Enqueue(TestPackets.Discover(1, "192.168.137.12", "esp32"));
                        endpoint.Enqueue(TestPackets.RequestSelecting(1, "192.168.137.12", host: "esp32"));
                        endpoint.WaitForSent(2);

                        var window = new MainWindow { DataContext = f.Vm, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
                        window.Show();
                        window.UpdateLayout();

                        var grid = FindChildren<DataGrid>(window).Single();
                        Assert.Single(grid.Items);
                        Assert.Equal(6, grid.Columns.Count);
                        Assert.All(grid.Columns, c => Assert.DoesNotContain("[", c.Header.ToString()));
                        var combo = FindChildren<ComboBox>(window).Single();
                        Assert.Single(combo.Items);
                        Assert.False(combo.IsEnabled); // locked while running
                        window.Close();
                    }

                    // The address group, the recovery banner and the confirmation window.
                    using (var f = new AddressVmFixture(AddressVmFixture.LinkLocalAutomatic(), c =>
                    {
                        Assert.True(c.Configurator.Apply(c.Chosen, AddressingTestData.Settings()).Succeeded);
                        c.Service.FailOn(ApplyStep.SetAddressDhcp);
                    }))
                    {
                        var window = new MainWindow { DataContext = f.Vm, ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
                        window.Show();
                        window.UpdateLayout();

                        var buttons = FindChildren<Button>(window).ToList();
                        Assert.Contains(buttons, b => Equals(b.Content, LanguageService.Get("Recovery_Retry")) && b.IsVisible);
                        Assert.Contains(buttons, b => Equals(b.Content, LanguageService.Get("Recovery_Discard")) && b.IsVisible);
                        Assert.Equal(5, FindChildren<TextBox>(window).Count());
                        Assert.Single(FindChildren<CheckBox>(window));
                        window.Close();

                        var confirm = new ConfirmChangesWindow(new ConfirmChangesViewModel(
                            ChangePreview.Create(AddressingTestData.Dhcp(), AddressingTestData.Settings()), "Ethernet 5"))
                        { ShowInTaskbar = false, WindowStartupLocation = WindowStartupLocation.Manual, Left = -10000, Top = -10000 };
                        confirm.Show();
                        confirm.UpdateLayout();
                        Assert.Equal(5, FindChildren<ListViewItem>(confirm).Count());
                        confirm.Close();
                    }
                }
                catch (Exception e)
                {
                    failure = e;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "UI thread did not finish");

            Assert.Null(failure);
            Assert.DoesNotContain(errors.Messages, m => m.Contains("BindingExpression path error"));
        }

        private static IEnumerable<T> FindChildren<T>(DependencyObject root) where T : DependencyObject
        {
            var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (var i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T match)
                {
                    yield return match;
                }

                foreach (var nested in FindChildren<T>(child))
                {
                    yield return nested;
                }
            }
        }
    }
}
