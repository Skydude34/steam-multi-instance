using System.Windows;
using System.Windows.Threading;
using SteamMultiInstance.Launcher.SteamGuard;

namespace SteamMultiInstance.Launcher;

public partial class MainWindow : Window
{
    private readonly InstanceManager _manager = new();
    private readonly MobileAuthStore _mobileAuthStore = new();
    private readonly DispatcherTimer _codeRefreshTimer;

    public MainWindow()
    {
        InitializeComponent();
        RefreshList();

        // Steam Guard код живёт 30 секунд — обновляем список каждую секунду,
        // чтобы в таблице всегда был актуальный код, а не протухший.
        _codeRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _codeRefreshTimer.Tick += (_, _) => RefreshList();
        _codeRefreshTimer.Start();
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
                SteamGuardCode = _mobileAuthStore.GetCurrentCode(i.InstanceId) ?? "—",
            })
            .ToList();
    }

    private void OnCopyCodeClicked(object sender, RoutedEventArgs e)
    {
        if (InstancesList.SelectedItem is not InstanceRow row)
        {
            return;
        }

        string? code = _mobileAuthStore.GetCurrentCode(row.InstanceId);
        if (code is null)
        {
            MessageBox.Show(this,
                $"Для инстанса \"{row.InstanceId}\" нет .maFile в {_mobileAuthStore.MaFilesDirectory}.",
                "Steam Guard", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        Clipboard.SetText(code);
    }
}
