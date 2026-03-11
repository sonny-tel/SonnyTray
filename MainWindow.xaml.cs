using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SonnyTray.ViewModels;

namespace SonnyTray;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        Hide();
    }

    private void OnPeerClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PeerItem peer }
            && DataContext is MainViewModel vm)
        {
            vm.OpenPeerDetailCommand.Execute(peer);
        }
    }

    private void OnThemeDarkClick(object sender, RoutedEventArgs e)
    {
        ThemeManager.ApplyTheme(AppThemeMode.Dark);
    }

    private void OnThemeLightClick(object sender, RoutedEventArgs e)
    {
        ThemeManager.ApplyTheme(AppThemeMode.Light);
    }

    private void OnThemeSystemClick(object sender, RoutedEventArgs e)
    {
        ThemeManager.ApplyTheme(AppThemeMode.System);
    }
}