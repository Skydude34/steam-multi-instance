using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using SteamMultiInstance.Launcher.SteamGuard;

namespace SteamMultiInstance.Launcher;

public partial class MainWindow : Window
{
    private readonly InstanceManager _manager = new();
    private readonly MobileAuthStore _mobileAuthStore = new();
    private readonly AccountStore _accountStore = new();
    private readonly DispatcherTimer _codeRefreshTimer;

    public MainWindow()
    {
        InitializeComponent();
        RefreshList();

        // Выбор строки в списке подставляет её ID в поле запуска — чтобы
        // не перепечатывать руками логин уже известного аккаунта.
        InstancesList.SelectionChanged += (_, _) =>
        {
            if (InstancesList.SelectedItem is InstanceRow row)
            {
                InstanceIdBox.Text = row.InstanceId;
            }
        };

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

        // Список показывает не только СЕЙЧАС запущенные инстансы (их знает
        // только InstanceManager, и это забывается при перезапуске GUI), а
        // объединение: сохранённые аккаунты (AccountStore) + все .maFile на
        // диске + реально работающие боксы — чтобы один раз добавленный
        // аккаунт всегда был виден в списке и его можно было выбрать и
        // запустить, не вводя ID заново.
        var knownIds = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var account in _accountStore.LoadAll())
        {
            knownIds.Add(account.Login);
        }

        if (Directory.Exists(_mobileAuthStore.MaFilesDirectory))
        {
            foreach (string file in Directory.GetFiles(_mobileAuthStore.MaFilesDirectory, "*.maFile"))
            {
                knownIds.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        var runningById = _manager.Instances.ToDictionary(i => i.InstanceId, StringComparer.OrdinalIgnoreCase);
        foreach (string runningId in runningById.Keys)
        {
            knownIds.Add(runningId);
        }

        var rows = knownIds.Select(id =>
        {
            bool isRunning = runningById.TryGetValue(id, out var instance);
            return new InstanceRow
            {
                InstanceId = id,
                Pid = isRunning ? instance!.Pid : -1,
                IsRunning = isRunning && instance!.IsRunning,
                ProfileDirectory = isRunning ? instance!.ProfileDirectory : "—",
                SteamGuardCode = _mobileAuthStore.GetCurrentCode(id) ?? "—",
                HasPassword = _accountStore.Has(id),
                HasMaFile = File.Exists(Path.Combine(_mobileAuthStore.MaFilesDirectory, $"{id}.maFile")),
            };
        }).ToList();

        InstancesList.ItemsSource = rows;

        if (selectedInstanceId is not null)
        {
            InstancesList.SelectedItem = rows.FirstOrDefault(r => r.InstanceId == selectedInstanceId);
        }
    }

    private void OnImportLogPassClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выбери файл с парами login:password (по одной на строку)",
            Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            int imported = _accountStore.ImportFromLogPassFile(dialog.FileName);
            MessageBox.Show(this, $"Импортировано аккаунтов: {imported}", "Импорт логин:пароль",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Не удалось импортировать", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        RefreshList();
    }

    private void OnImportMaFileClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Выбери один или несколько .maFile",
            Filter = "Steam .maFile (*.maFile)|*.maFile|Все файлы (*.*)|*.*",
            Multiselect = true,
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        Directory.CreateDirectory(_mobileAuthStore.MaFilesDirectory);

        int imported = 0;
        var errors = new List<string>();
        foreach (string file in dialog.FileNames)
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<MobileAuthFile>(File.ReadAllText(file));
                if (parsed is null || string.IsNullOrEmpty(parsed.AccountName))
                {
                    errors.Add($"{Path.GetFileName(file)}: нет account_name в файле");
                    continue;
                }

                string dest = Path.Combine(_mobileAuthStore.MaFilesDirectory, $"{parsed.AccountName}.maFile");
                File.Copy(file, dest, overwrite: true);
                imported++;
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
            }
        }

        string summary = $"Импортировано .maFile: {imported}";
        if (errors.Count > 0)
        {
            summary += "\n\nОшибки:\n" + string.Join("\n", errors);
        }

        MessageBox.Show(this, summary, "Импорт .maFile", MessageBoxButton.OK, MessageBoxImage.Information);
        RefreshList();
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
