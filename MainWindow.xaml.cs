using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SonnyTray.ViewModels;

namespace SonnyTray;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldVm)
            oldVm.PropertyChanged -= OnViewModelPropertyChanged;
        if (e.NewValue is INotifyPropertyChanged newVm)
            newVm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainViewModel vm) return;

        if (e.PropertyName == nameof(MainViewModel.ShowExitNodePicker) && vm.ShowExitNodePicker)
            Dispatcher.InvokeAsync(() => ExitNodeHeader.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);
        else if (e.PropertyName == nameof(MainViewModel.ShowSettings) && vm.ShowSettings)
            Dispatcher.InvokeAsync(() => SettingsHeader.BringIntoView(), System.Windows.Threading.DispatcherPriority.Loaded);

        if (e.PropertyName is nameof(MainViewModel.ShowExitNodePicker) or nameof(MainViewModel.ShowSettings))
            Dispatcher.InvokeAsync(UpdateStickyHeaders, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnContentScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        UpdateStickyHeaders();
    }

    private void UpdateStickyHeaders()
    {
        if (DataContext is not MainViewModel vm) return;

        bool exitSticky = vm.ShowExitNodePicker && IsScrolledOutOfView(ExitNodeHeader);
        StickyExitNodeHeader.Visibility = exitSticky ? Visibility.Visible : Visibility.Collapsed;
        StickyExitNodeDivider.Visibility = exitSticky ? Visibility.Visible : Visibility.Collapsed;

        bool settingsSticky = vm.ShowSettings && IsScrolledOutOfView(SettingsHeader);
        StickySettingsHeader.Visibility = settingsSticky ? Visibility.Visible : Visibility.Collapsed;
        StickySettingsDivider.Visibility = settingsSticky ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool IsScrolledOutOfView(FrameworkElement element)
    {
        var transform = element.TransformToAncestor(ContentScroll);
        var position = transform.Transform(new Point(0, 0));
        // The element is out of view if its bottom edge is above the scroll viewport top
        return position.Y + element.ActualHeight < 0;
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

    private void OnHyperlinkRequestNavigate(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}