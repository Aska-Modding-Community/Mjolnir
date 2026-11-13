cmake_minimum_required(VERSION 3.25)

# /MT everywhere (including MinHook): players need no VC++ redistributable.
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")

project(Mjolnir.Native LANGUAGES C CXX)

if(NOT MSVC OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
    message(FATAL_ERROR "Mjolnir.Native targets MSVC x64 only")
endif()

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

include(FetchContent)
FetchContent_Declare(minhook
    GIT_REPOSITORY https://github.com/TsudaKageyu/minhook.git
    GIT_TAG        c3fcafdc10146beb5919319d0683e44e3c30d537  # v1.3.4
)
FetchContent_MakeAvailable(minhook)

add_library(version SHARED
    src/dllmain.cpp
    src/proxy.cpp
    src/iat.cpp
    src/il2cpp_hook.cpp
    src/clr_host.cpp
    src/log.cpp
    src/paths.cpp
    version.def
)
target_include_directories(version PRIVATE src third_party/dotnet)
target_link_libraries(version PRIVATE minhook)
target_compile_definitions(version PRIVATE WIN32_LEAN_AND_MEAN NOMINMAX UNICODE _UNICODE)
target_compile_options(version PRIVATE /W4 /permissive- /EHsc /utf-8)
target_link_options(version PRIVATE /INCREMENTAL:NO $<$<NOT:$<CONFIG:Debug>>:/OPT:REF /OPT:ICF>)
set_target_properties(version PROPERTIES OUTPUT_NAME "version")
