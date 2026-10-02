using System.Diagnostics;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Definitions;
using FlaUI.UIA3;

namespace SteamMultiInstance.Launcher;

/// <summary>
/// Автоматический ввод логина/пароля/кода Steam Guard в окно входа Steam
/// через UI Automation (FlaUI) — находит поля по роли элемента в дереве
/// окна, а не по хрупким хукам/координатам/мьютексам.
///
/// Логин/пароль читаются из <see cref="CredentialStore"/> (локально,
/// зашифровано DPAPI) — они никогда не проходят через чат/лог, только
/// через это хранилище и это окно.
/// </summary>
public static class SteamLoginAutomator
{
    private const int DefaultTimeoutMs = 30_000;
    private const int PollIntervalMs = 500;

    /// <summary>
    /// Ждёт окно входа Steam для процесса pid и вводит логин+пароль.
    /// Возвращает true, если поля найдены и заполнены (не гарантирует,
    /// что Steam принял пароль — это уже реакция самого Steam).
    /// </summary>
    public static bool TryLogin(int pid, string login, string password, int timeoutMs = DefaultTimeoutMs)
    {
        using var automation = new UIA3Automation();
        Window? loginWindow = WaitForLoginWindow(automation, pid, timeoutMs);
        if (loginWindow is null)
        {
            return false;
        }

        // Steam-окно входа: обычно два текстовых поля (логин, пароль — у
        // пароля IsPassword=true в дереве Automation) и одна кнопка "Войти".
        // TODO(проверить на живом Steam): если раскладка окна отличается
        // (другая версия клиента/локализация), сузить поиск по AutomationId,
        // который можно увидеть через FlaUI.Core.Tools или Inspect.exe.
        AutomationElement[] edits = loginWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit));
        if (edits.Length < 2)
        {
            return false;
        }

        TextBox? loginField = edits.Select(e => e.AsTextBox()).FirstOrDefault(tb => !tb.IsPassword);
        TextBox? passwordField = edits.Select(e => e.AsTextBox()).FirstOrDefault(tb => tb.IsPassword);

        if (loginField is null || passwordField is null)
        {
            // Фолбэк: не нашли по IsPassword — берём первые два поля по
            // порядку (логин, затем пароль), как они обычно идут в окне.
            loginField ??= edits[0].AsTextBox();
            passwordField ??= edits[1].AsTextBox();
        }

        loginField.Text = login;
        passwordField.Text = password;

        Button? signInButton = loginWindow
            .FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
            .Select(b => b.AsButton())
            .FirstOrDefault(b => b.Name.Contains("Sign in", StringComparison.OrdinalIgnoreCase)
                               || b.Name.Contains("Войти", StringComparison.OrdinalIgnoreCase));
        signInButton?.Invoke();

        return true;
    }

    /// <summary>
    /// Ждёт запрос кода Steam Guard (отдельное окно/поле после пароля) и
    /// вводит код, скопированный из <see cref="SteamGuard.MobileAuthStore"/>.
    /// </summary>
    public static bool TryEnterGuardCode(int pid, string code, int timeoutMs = DefaultTimeoutMs)
    {
        using var automation = new UIA3Automation();
        Window? guardWindow = WaitForGuardWindow(automation, pid, timeoutMs);
        if (guardWindow is null)
        {
            return false;
        }

        AutomationElement[] edits = guardWindow.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit));
        if (edits.Length == 0)
        {
            return false;
        }

        edits[0].AsTextBox().Text = code;

        Button? confirmButton = guardWindow
            .FindAllDescendants(cf => cf.ByControlType(ControlType.Button))
            .Select(b => b.AsButton())
            .FirstOrDefault(b => b.Name.Contains("Continue", StringComparison.OrdinalIgnoreCase)
                               || b.Name.Contains("Submit", StringComparison.OrdinalIgnoreCase)
                               || b.Name.Contains("Продолжить", StringComparison.OrdinalIgnoreCase));
        confirmButton?.Invoke();

        return true;
    }

    private static Window? WaitForLoginWindow(UIA3Automation automation, int pid, int timeoutMs) =>
        WaitForWindow(automation, pid, timeoutMs,
            titleHint: "Steam", requireEditCount: 2);

    private static Window? WaitForGuardWindow(UIA3Automation automation, int pid, int timeoutMs) =>
        WaitForWindow(automation, pid, timeoutMs,
            titleHint: "Steam", requireEditCount: 1);

    private static Window? WaitForWindow(
        UIA3Automation automation, int pid, int timeoutMs, string titleHint, int requireEditCount)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                Process process = Process.GetProcessById(pid);
                var app = Application.Attach(process);
                Window[] windows = app.GetAllTopLevelWindows(automation);

                Window? candidate = windows.FirstOrDefault(w =>
                    w.Title.Contains(titleHint, StringComparison.OrdinalIgnoreCase) &&
                    w.FindAllDescendants(cf => cf.ByControlType(ControlType.Edit)).Length >= requireEditCount);

                if (candidate is not null)
                {
                    return candidate;
                }
            }
            catch
            {
                // Процесс мог ещё не создать окно, или PID относится к
                // обёртке/дочернему процессу без своего окна — просто ждём
                // следующего опроса.
            }

            Thread.Sleep(PollIntervalMs);
        }

        return null;
    }
}
