// injector_hooks.dll
//
// Инжектируется в целевой процесс (steam.exe и/или саму игру) до того, как
// процесс выполнит свою точку входа. Подменяет имена named-объектов, которые
// приложение использует для detect "уже запущено", добавляя к ним суффикс
// конкретного инстанса — так второй процесс не видит "чужой" мьютекс/окно и
// не считает, что уже запущен.
//
// Суффикс инстанса передаётся через переменную окружения SMI_INSTANCE_ID,
// которую выставляет launcher.exe перед CreateProcess.

#include <windows.h>
#include <MinHook.h>
#include <string>
#include <cwchar>
#include <cstdio>

namespace {

// Оригинальные указатели на перехваченные функции.
decltype(&CreateMutexW) g_origCreateMutexW = nullptr;
decltype(&OpenMutexW) g_origOpenMutexW = nullptr;
decltype(&CreateMutexExW) g_origCreateMutexExW = nullptr;
decltype(&FindWindowW) g_origFindWindowW = nullptr;
decltype(&FindWindowExW) g_origFindWindowExW = nullptr;
decltype(&RegisterClassW) g_origRegisterClassW = nullptr;
decltype(&RegisterClassExW) g_origRegisterClassExW = nullptr;

std::wstring g_instanceSuffix;

// DIAGNOSTIC: если задана переменная окружения SMI_LOG_ONLY=1, хуки не
// переименовывают объекты (вызывают оригинал как есть), а только пишут в
// лог-файл C:\steam\smi_hooks_<PID>.log каждое имя мьютекса/класса окна,
// которое процесс создаёт/ищет при старте — чтобы найти точное имя,
// отвечающее за single-instance detect, прежде чем сужать hook до whitelist.
bool g_logOnly = false;

void LogLine(const wchar_t* tag, const wchar_t* name) {
    wchar_t path[MAX_PATH];
    swprintf(path, MAX_PATH, L"C:\\steam\\smi_hooks_%lu.log", GetCurrentProcessId());
    FILE* f = nullptr;
    if (_wfopen_s(&f, path, L"a, ccs=UTF-8") == 0 && f) {
        fwprintf(f, L"[%s] %s\n", tag, name ? name : L"(null)");
        fclose(f);
    }
}

// Возвращает true, если имя похоже на объект, который стоит переименовать.
// TODO: заменить на конфигурируемый список точных имён для конкретных
// приложений (Steam, саму игру), найденных через Process Hacker/WinObj —
// пока подмешиваем суффикс ко всем named-объектам этого процесса, что проще,
// но может задеть системные объекты, если их создаёт сам процесс.
std::wstring Suffixed(LPCWSTR name) {
    if (!name) return {};
    std::wstring result(name);
    result += L"_";
    result += g_instanceSuffix;
    return result;
}

HANDLE WINAPI HookedCreateMutexW(LPSECURITY_ATTRIBUTES attrs, BOOL initialOwner, LPCWSTR name) {
    if (g_logOnly) {
        LogLine(L"CreateMutexW", name);
        return g_origCreateMutexW(attrs, initialOwner, name);
    }
    if (!name) return g_origCreateMutexW(attrs, initialOwner, name);
    std::wstring renamed = Suffixed(name);
    return g_origCreateMutexW(attrs, initialOwner, renamed.c_str());
}

HANDLE WINAPI HookedOpenMutexW(DWORD desiredAccess, BOOL inheritHandle, LPCWSTR name) {
    if (g_logOnly) {
        LogLine(L"OpenMutexW", name);
        return g_origOpenMutexW(desiredAccess, inheritHandle, name);
    }
    if (!name) return g_origOpenMutexW(desiredAccess, inheritHandle, name);
    std::wstring renamed = Suffixed(name);
    return g_origOpenMutexW(desiredAccess, inheritHandle, renamed.c_str());
}

HANDLE WINAPI HookedCreateMutexExW(LPSECURITY_ATTRIBUTES attrs, LPCWSTR name, DWORD flags, DWORD desiredAccess) {
    if (g_logOnly) {
        LogLine(L"CreateMutexExW", name);
        return g_origCreateMutexExW(attrs, name, flags, desiredAccess);
    }
    if (!name) return g_origCreateMutexExW(attrs, name, flags, desiredAccess);
    std::wstring renamed = Suffixed(name);
    return g_origCreateMutexExW(attrs, renamed.c_str(), flags, desiredAccess);
}

HWND WINAPI HookedFindWindowW(LPCWSTR className, LPCWSTR windowName) {
    if (g_logOnly) {
        LogLine(L"FindWindowW", className);
        return g_origFindWindowW(className, windowName);
    }
    // Второй инстанс не должен находить окно первого по оригинальному имени
    // класса — это именно та проверка, через которую приложение решает
    // "активировать существующее окно вместо запуска нового".
    std::wstring renamedClass = className ? Suffixed(className) : std::wstring();
    return g_origFindWindowW(className ? renamedClass.c_str() : nullptr, windowName);
}

HWND WINAPI HookedFindWindowExW(HWND parent, HWND childAfter, LPCWSTR className, LPCWSTR windowName) {
    if (g_logOnly) {
        LogLine(L"FindWindowExW", className);
        return g_origFindWindowExW(parent, childAfter, className, windowName);
    }
    std::wstring renamedClass = className ? Suffixed(className) : std::wstring();
    return g_origFindWindowExW(parent, childAfter, className ? renamedClass.c_str() : nullptr, windowName);
}

ATOM WINAPI HookedRegisterClassW(const WNDCLASSW* wndClass) {
    if (g_logOnly) {
        LogLine(L"RegisterClassW", wndClass ? wndClass->lpszClassName : nullptr);
        return g_origRegisterClassW(wndClass);
    }
    if (!wndClass || !wndClass->lpszClassName) return g_origRegisterClassW(wndClass);
    WNDCLASSW copy = *wndClass;
    std::wstring renamed = Suffixed(wndClass->lpszClassName);
    copy.lpszClassName = renamed.c_str();
    return g_origRegisterClassW(&copy);
}

ATOM WINAPI HookedRegisterClassExW(const WNDCLASSEXW* wndClass) {
    if (g_logOnly) {
        LogLine(L"RegisterClassExW", wndClass ? wndClass->lpszClassName : nullptr);
        return g_origRegisterClassExW(wndClass);
    }
    if (!wndClass || !wndClass->lpszClassName) return g_origRegisterClassExW(wndClass);
    WNDCLASSEXW copy = *wndClass;
    std::wstring renamed = Suffixed(wndClass->lpszClassName);
    copy.lpszClassName = renamed.c_str();
    return g_origRegisterClassExW(&copy);
}

template <typename T>
void CreateAndEnableHook(LPVOID target, T detour, T* original) {
    MH_CreateHook(target, detour, reinterpret_cast<LPVOID*>(original));
    MH_EnableHook(target);
}

void InstallHooks() {
    wchar_t buf[64] = {};
    DWORD len = GetEnvironmentVariableW(L"SMI_INSTANCE_ID", buf, 64);
    g_instanceSuffix = (len > 0) ? std::wstring(buf) : L"smi0";

    wchar_t logOnlyBuf[8] = {};
    g_logOnly = GetEnvironmentVariableW(L"SMI_LOG_ONLY", logOnlyBuf, 8) > 0
        && wcscmp(logOnlyBuf, L"1") == 0;

    if (MH_Initialize() != MH_OK) return;

    CreateAndEnableHook(&CreateMutexW, &HookedCreateMutexW, &g_origCreateMutexW);
    CreateAndEnableHook(&OpenMutexW, &HookedOpenMutexW, &g_origOpenMutexW);
    CreateAndEnableHook(&CreateMutexExW, &HookedCreateMutexExW, &g_origCreateMutexExW);
    CreateAndEnableHook(&FindWindowW, &HookedFindWindowW, &g_origFindWindowW);
    CreateAndEnableHook(&FindWindowExW, &HookedFindWindowExW, &g_origFindWindowExW);
    // RegisterClassW/Ex НЕ хукаем: лог (SMI_LOG_ONLY=1, 2026-10-02) на реальном
    // steam.exe показал, что здесь регистрируются только системные общие
    // классы контролов (Button/Static/Edit/msctls_progress32/...) для
    // собственного UI бутстраппера (BootstrapUpdateUIClass) — переименование
    // ломает сопоставление с последующими CreateWindowW(<исходное имя>) и
    // валит процесс ещё до окна логина. Ни одного Steam-специфичного имени
    // класса, похожего на single-instance detect, в логе не было.
}

void RemoveHooks() {
    MH_DisableHook(MH_ALL_HOOKS);
    MH_Uninitialize();
}

} // namespace

BOOL APIENTRY DllMain(HMODULE, DWORD reason, LPVOID) {
    switch (reason) {
        case DLL_PROCESS_ATTACH:
            InstallHooks();
            break;
        case DLL_PROCESS_DETACH:
            RemoveHooks();
            break;
    }
    return TRUE;
}
