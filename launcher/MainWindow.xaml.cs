using System.Windows;

namespace SteamMultiInstance.Launcher;

public partial class MainWindow : Window
{
    private readonly InstanceManager _manager = new();

    public MainWindow()
    {
        InitializeComponent();
        RefreshList();
    }

    private void OnLaunchClicked(object sender, RoutedEventArgs e)
    {
        string instanceId = InstanceIdBox.Text.Trim();
        string exePath = ExePathBox.Text.Trim();

        if (string.IsNullOrEmpty(instanceId) || string.IsNullOrEmpty(exePath))
        {
            MessageBox.Show(this, "Укажи ID инстанса и путь к .exe.", "Steam Multi-Instance",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            _manager.Launch(instanceId, exePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Не удалось запустить инстанс",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }

        RefreshList();
    }

    private void OnRefreshClicked(object sender, RoutedEventArgs e) => RefreshList();

    private void OnKillSelectedClicked(object sender, RoutedEventArgs e)
    {
        if (InstancesList.SelectedItem is InstanceRow row)
        {
            _manager.Kill(row.InstanceId);
        }
        RefreshList();
    }

    private void OnKillAllClicked(object sender, RoutedEventArgs e)
    {
        _manager.KillAll();
        RefreshList();
    }

    private void RefreshList()
    {
        InstancesList.ItemsSource = _manager.Instances
            .Select(i => new InstanceRow
            {
                InstanceId = i.InstanceId,
                Pid = i.Pid,
                IsRunning = i.IsRunning,
                ProfileDirectory = i.ProfileDirectory,
            })
            .ToList();
    }
}
