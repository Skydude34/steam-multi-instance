using System.Windows;
using System.Windows.Threading;
using SteamMultiInstance.Launcher.SteamGuard;

namespace SteamMultiInstance.Launcher;

public partial class MainWindow : Window
{
    private readonly InstanceManager _manager = new();
    private readonly MobileAuthStore _mobileAuthStore = new();
    private readonly CredentialStore _credentialStore = new();
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
        // ItemsSource пересоздаётся каждую секунду (таймер обновления Steam
        // Guard кода), поэтому WPF теряет выделение строки — иначе кнопки
        // "Скопировать код"/"Завершить выбранный" молча ничего не делают.
        // Запоминаем ID выбранной строки и восстанавливаем выделение после
        // переприсвоения ItemsSource.
        string? selectedInstanceId = (InstancesList.SelectedItem as InstanceRow)?.InstanceId;

        var rows = _manager.Instances
            .Select(i => new InstanceRow
            {
                InstanceId = i.InstanceId,
                Pid = i.Pid,
                IsRunning = i.IsRunning,
                ProfileDirectory = i.ProfileDirectory,
                SteamGuardCode = _mobileAuthStore.GetCurrentCode(i.InstanceId) ?? "—",
                HasCredentials = _credentialStore.Has(i.InstanceId),
            })
            .ToList();

        InstancesList.ItemsSource = rows;

        if (selectedInstanceId is not null)
        {
            InstancesList.SelectedItem = rows.FirstOrDefault(r => r.InstanceId == selectedInstanceId);
        }
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

    private void OnSaveCredentialClicked(object sender, RoutedEventArgs e)
    {
        if (InstancesList.SelectedItem is not InstanceRow row)
        {
            MessageBox.Show(this, "Сначала выбери инстанс в списке.", "Steam Multi-Instance",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string login = CredentialLoginBox.Text.Trim();
        string password = CredentialPasswordBox.Password;

        if (string.IsNullOrEmpty(login) || string.IsNullOrEmpty(password))
        {
            MessageBox.Show(this, "Укажи логин и пароль.", "Steam Multi-Instance",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Сохраняем локально (зашифровано DPAPI) и сразу чистим поле пароля
        // из UI — оно больше не нужно в памяти формы после записи на диск.
        _credentialStore.Save(row.InstanceId, login, password);
        CredentialPasswordBox.Clear();

        RefreshList();
    }

    private async void OnAutoLoginClicked(object sender, RoutedEventArgs e)
    {
        if (InstancesList.SelectedItem is not InstanceRow row)
        {
            MessageBox.Show(this, "Сначала выбери инстанс в списке.", "Steam Multi-Instance",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_credentialStore.TryLoad(row.InstanceId, out string login, out string password))
        {
            MessageBox.Show(this,
                $"Для инстанса \"{row.InstanceId}\" не сохранены учётные данные — сначала заполни логин/пароль и нажми \"Сохранить\".",
                "Steam Multi-Instance", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (row.Pid <= 0)
        {
            MessageBox.Show(this, "Инстанс не запущен — сначала нажми \"Запустить\".", "Steam Multi-Instance",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        int pid = row.Pid;
        string instanceId = row.InstanceId;

        LoginButton.IsEnabled = false;
        try
        {
            bool loggedIn = await Task.Run(() => SteamLoginAutomator.TryLogin(pid, login, password));
            if (!loggedIn)
            {
                MessageBox.Show(this, "Не нашёл окно входа Steam в отведённое время — попробуй ещё раз или введи данные вручную.",
                    "Автологин", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Код Steam Guard вводим отдельным шагом — окно запроса кода
            // появляется только после того, как Steam принял пароль.
            string? code = _mobileAuthStore.GetCurrentCode(instanceId);
            if (code is not null)
            {
                bool codeEntered = await Task.Run(() => SteamLoginAutomator.TryEnterGuardCode(pid, code));
                if (!codeEntered)
                {
                    MessageBox.Show(this,
                        "Пароль введён, но не нашёл окно запроса кода Steam Guard (или .maFile не нужен для этого аккаунта) — проверь вручную.",
                        "Автологин", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }
        finally
        {
            LoginButton.IsEnabled = true;
        }
    }
}
