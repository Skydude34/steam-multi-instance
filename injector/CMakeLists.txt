cmake_minimum_required(VERSION 3.20)
project(SteamMultiInstanceInjector LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

if (NOT WIN32)
    message(FATAL_ERROR "Этот проект собирается только под Windows (WinAPI).")
endif()

find_package(minhook CONFIG REQUIRED)

# --- hooks.dll: инжектируется в целевой процесс ---
add_library(hooks SHARED
    src/dllmain.cpp
)
target_link_libraries(hooks PRIVATE minhook::minhook)
target_compile_definitions(hooks PRIVATE UNICODE _UNICODE)
set_target_properties(hooks PROPERTIES OUTPUT_NAME "injector_hooks")

# --- launcher.exe: запускает целевой процесс suspended и инжектирует DLL ---
add_executable(launcher
    src/launcher.cpp
)
target_compile_definitions(launcher PRIVATE UNICODE _UNICODE)
set_target_properties(launcher PROPERTIES OUTPUT_NAME "smi_launcher")
