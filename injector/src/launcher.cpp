// smi_launcher.exe
//
// Запускает целевой .exe в приостановленном состоянии (CREATE_SUSPENDED),
// инжектирует injector_hooks.dll через классическую схему
// WriteProcessMemory + CreateRemoteThread(LoadLibraryW) и резюмирует процесс
// только после того, как DLL загрузилась и хуки установлены — таким
// образом подмена мьютекса/окна происходит раньше, чем код приложения
// успевает их проверить.
//
// Использование:
//   smi_launcher.exe <instance_id> <путь_к_exe> [аргументы...]
//
// instance_id — произвольная уникальная строка на инстанс (например,
// "alice" / "bob"), пробрасывается в целевой процесс через переменную
// окружения SMI_INSTANCE_ID, которую читает injector_hooks.dll.

#include <windows.h>
#include <iostream>
#include <string>
#include <vector>

namespace {

std::wstring GetHooksDllPath() {
    wchar_t exePath[MAX_PATH] = {};
    GetModuleFileNameW(nullptr, exePath, MAX_PATH);
    std::wstring path(exePath);
    size_t slash = path.find_last_of(L"\\/");
    std::wstring dir = (slash == std::wstring::npos) ? L"." : path.substr(0, slash);
    return dir + L"\\injector_hooks.dll";
}

// Внедряет dllPath в process через LoadLibraryW, выполняемую удалённым
// потоком. Возвращает true при успехе (ждём завершения загрузки DLL).
bool InjectDll(HANDLE process, const std::wstring& dllPath) {
    SIZE_T size = (dllPath.size() + 1) * sizeof(wchar_t);
    LPVOID remoteBuf = VirtualAllocEx(process, nullptr, size, MEM_COMMIT, PAGE_READWRITE);
    if (!remoteBuf) {
        std::wcerr << L"VirtualAllocEx failed: " << GetLastError() << L"\n";
        return false;
    }

    if (!WriteProcessMemory(process, remoteBuf, dllPath.c_str(), size, nullptr)) {
        std::wcerr << L"WriteProcessMemory failed: " << GetLastError() << L"\n";
        VirtualFreeEx(process, remoteBuf, 0, MEM_RELEASE);
        return false;
    }

    LPTHREAD_START_ROUTINE loadLibraryAddr =
        reinterpret_cast<LPTHREAD_START_ROUTINE>(
            GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "LoadLibraryW"));
    if (!loadLibraryAddr) {
        std::wcerr << L"GetProcAddress(LoadLibraryW) failed: " << GetLastError() << L"\n";
        VirtualFreeEx(process, remoteBuf, 0, MEM_RELEASE);
        return false;
    }

    HANDLE remoteThread = CreateRemoteThread(
        process, nullptr, 0, loadLibraryAddr, remoteBuf, 0, nullptr);
    if (!remoteThread) {
        std::wcerr << L"CreateRemoteThread failed: " << GetLastError() << L"\n";
        VirtualFreeEx(process, remoteBuf, 0, MEM_RELEASE);
        return false;
    }

    WaitForSingleObject(remoteThread, INFINITE);

    DWORD exitCode = 0;
    GetExitCodeThread(remoteThread, &exitCode); // HMODULE загруженной DLL, 0 при ошибке
    CloseHandle(remoteThread);
    VirtualFreeEx(process, remoteBuf, 0, MEM_RELEASE);

    if (exitCode == 0) {
        std::wcerr << L"LoadLibraryW в целевом процессе вернул NULL — DLL не загрузилась.\n";
        return false;
    }
    return true;
}

} // namespace

int wmain(int argc, wchar_t* argv[]) {
    if (argc < 3) {
        std::wcerr << L"Использование: smi_launcher.exe <instance_id> <путь_к_exe> [аргументы...]\n";
        return 1;
    }

    const std::wstring instanceId = argv[1];
    const std::wstring targetExe = argv[2];

    std::wstring cmdLine = L"\"" + targetExe + L"\"";
    for (int i = 3; i < argc; ++i) {
        cmdLine += L" ";
        cmdLine += argv[i];
    }

    // Пробрасываем SMI_INSTANCE_ID в блок окружения дочернего процесса, чтобы
    // injector_hooks.dll знал, каким суффиксом переименовывать объекты.
    SetEnvironmentVariableW(L"SMI_INSTANCE_ID", instanceId.c_str());

    STARTUPINFOW si = {sizeof(si)};
    PROCESS_INFORMATION pi = {};

    std::vector<wchar_t> cmdLineBuf(cmdLine.begin(), cmdLine.end());
    cmdLineBuf.push_back(L'\0');

    BOOL ok = CreateProcessW(
        nullptr,
        cmdLineBuf.data(),
        nullptr, nullptr,
        FALSE,
        CREATE_SUSPENDED,
        nullptr, // наследуем текущее окружение (включая SMI_INSTANCE_ID)
        nullptr,
        &si, &pi);

    if (!ok) {
        std::wcerr << L"CreateProcessW failed: " << GetLastError() << L"\n";
        return 1;
    }

    const std::wstring hooksDll = GetHooksDllPath();
    if (!InjectDll(pi.hProcess, hooksDll)) {
        std::wcerr << L"Инъекция DLL не удалась, завершаю процесс.\n";
        TerminateProcess(pi.hProcess, 1);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        return 1;
    }

    ResumeThread(pi.hThread);

    std::wcout << L"Инстанс \"" << instanceId << L"\" запущен, PID=" << pi.dwProcessId << L"\n";

    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return 0;
}
