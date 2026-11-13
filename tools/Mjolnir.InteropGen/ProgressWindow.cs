using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;

namespace Mjolnir.InteropGen;

/// <summary>
/// The window the player sees while the loader generates the interop assemblies at the game's start: the game has no
/// window of its own yet (it is waiting in il2cpp_init), so without it nothing would show for half
/// a minute. Plain Win32, because the private .NET runtime the loader ships has no Windows desktop libraries. Its button
/// (and its close box) starts the game without mods: the process ends with <see cref="Cancelled"/> and the generation runs
/// again at the next start. The texts follow the language of Windows: Russian, else English.
/// </summary>
internal static class ProgressWindow
{
    public const int Cancelled = 3;

    private const string ClassName = "MjolnirInteropProgress";
    private const int ButtonId = 100, TimerId = 1, Done = 0x8001; // WM_APP + 1
    private const int WM_DESTROY = 0x0002, WM_CLOSE = 0x0010, WM_SETFONT = 0x0030, WM_COMMAND = 0x0111, WM_TIMER = 0x0113,
                      WM_CTLCOLORSTATIC = 0x0138, PBM_SETPOS = 0x0402, PBM_SETRANGE32 = 0x0406;
    private const uint WS_OVERLAPPED = 0, WS_CAPTION = 0x00C00000, WS_SYSMENU = 0x00080000, WS_CHILD = 0x40000000,
                       WS_VISIBLE = 0x10000000, WS_TABSTOP = 0x00010000, WS_EX_TOPMOST = 0x8, WS_EX_APPWINDOW = 0x40000,
                       SS_LEFT = 0, BS_PUSHBUTTON = 0;

    private static WndProc? s_proc; // kept alive: Windows calls it for the window's life
    private static IntPtr s_window, s_bar, s_stage;
    private static Stage s_shownStage = (Stage)(-1);
    private static Texts s_texts = Texts.English;

    /// <summary>Runs <paramref name="work"/> on another thread while the window shows; its result is the exit code.</summary>
    public static int Run(Func<int> work)
    {
        s_texts = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? Texts.Russian : Texts.English;
        if (!TryCreate()) return work();

        int result = 1;
        var worker = new Thread(() =>
        {
            try { result = work(); }
            finally { PostMessage(s_window, Done, IntPtr.Zero, IntPtr.Zero); }
        }) { IsBackground = true, Name = "Interop generation" };
        worker.Start();

        while (GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
        worker.Join();
        return result;
    }

    private static bool TryCreate()
    {
        try
        {
            SetProcessDpiAwarenessContext(new IntPtr(-4)); // per monitor v2
            var controls = new InitCommonControlsExArgs { Size = 8, Classes = 0x20 }; // ICC_PROGRESS_CLASS
            InitCommonControlsEx(ref controls);
            IntPtr instance = GetModuleHandle(null);
            s_proc = Proc;
            var cls = new WndClassEx
            {
                Size = (uint)Marshal.SizeOf<WndClassEx>(),
                Proc = Marshal.GetFunctionPointerForDelegate(s_proc),
                Instance = instance,
                Cursor = LoadCursor(IntPtr.Zero, new IntPtr(32512)), // IDC_ARROW
                Background = GetSysColorBrush(5), // COLOR_WINDOW
                ClassName = ClassName,
            };
            if (RegisterClassEx(ref cls) == 0) return Failed("RegisterClassEx");

            uint dpi = GetDpiForSystem();
            int S(int v) => v * (int)dpi / 96;
            const int width = 520, height = 206;
            var frame = new Rect { Right = S(width), Bottom = S(height) };
            uint style = WS_OVERLAPPED | WS_CAPTION | WS_SYSMENU;
            AdjustWindowRectExForDpi(ref frame, style, false, WS_EX_TOPMOST | WS_EX_APPWINDOW, dpi);
            int w = frame.Right - frame.Left, h = frame.Bottom - frame.Top;
            int x = (GetSystemMetrics(0) - w) / 2, y = (GetSystemMetrics(1) - h) / 2;
            s_window = CreateWindowEx(WS_EX_TOPMOST | WS_EX_APPWINDOW, ClassName, "Mjolnir", style, x, y, w, h,
                                      IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
            if (s_window == IntPtr.Zero) return Failed("CreateWindowEx");

            IntPtr Font(int points, int weight) =>
                CreateFont(-points * (int)dpi / 72, 0, 0, 0, weight, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
            IntPtr Child(string cls, string text, uint style, int left, int top, int cw, int ch, IntPtr font, int id = 0)
            {
                var child = CreateWindowEx(0, cls, text, WS_CHILD | WS_VISIBLE | style, S(left), S(top), S(cw), S(ch),
                                           s_window, new IntPtr(id), instance, IntPtr.Zero);
                SendMessage(child, WM_SETFONT, font, new IntPtr(1));
                return child;
            }
            IntPtr title = Font(13, 600), body = Font(10, 400);
            Child("STATIC", s_texts.Title, SS_LEFT, 24, 18, 472, 28, title);
            Child("STATIC", s_texts.Description, SS_LEFT, 24, 52, 472, 44, body);
            s_bar = Child("msctls_progress32", "", 0, 24, 106, 472, 18, body);
            SendMessage(s_bar, PBM_SETRANGE32, IntPtr.Zero, new IntPtr(1000));
            s_stage = Child("STATIC", "", SS_LEFT, 24, 130, 472, 22, body);
            Child("BUTTON", s_texts.WithoutMods, BS_PUSHBUTTON | WS_TABSTOP, 296, 160, 200, 32, body, ButtonId);

            SetTimer(s_window, new IntPtr(TimerId), 100, IntPtr.Zero);
            Refresh();
            ShowWindow(s_window, 5); // SW_SHOW
            SetForegroundWindow(s_window);
            return true;
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] No progress window: {e.Message}");
            return false;
        }
    }

    // Without the window the generation still runs: the loader waits for it all the same.
    private static bool Failed(string call)
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] No progress window: {call} failed, error {Marshal.GetLastPInvokeError()}");
        return false;
    }

    private static IntPtr Proc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case WM_TIMER:
                Refresh();
                return IntPtr.Zero;
            case WM_COMMAND when (wParam.ToInt64() & 0xFFFF) == ButtonId:
            case WM_CLOSE:
                // The player starts the game without mods: what is written so far stays behind in the output folder,
                // which the loader clears before the next generation.
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Cancelled by the player");
                Console.Out.Flush();
                Environment.Exit(Cancelled);
                return IntPtr.Zero;
            case Done:
                KillTimer(hwnd, new IntPtr(TimerId));
                DestroyWindow(hwnd);
                return IntPtr.Zero;
            case WM_CTLCOLORSTATIC:
                SetBkMode(wParam, 1); // TRANSPARENT
                return GetSysColorBrush(5);
            case WM_DESTROY:
                PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, message, wParam, lParam);
    }

    private static void Refresh()
    {
        SendMessage(s_bar, PBM_SETPOS, new IntPtr((int)(Progress.Fraction * 1000)), IntPtr.Zero);
        var stage = Progress.Stage;
        if (stage == s_shownStage) return;
        s_shownStage = stage;
        SetWindowText(s_stage, s_texts.Of(stage));
    }

    private sealed record Texts(string Title, string Description, string WithoutMods, string Starting, string Download,
                                string ReadCode, string Types, string Assemblies, string Verify)
    {
        public static readonly Texts English = new(
            "Mjolnir is getting ready for this version of ASKA",
            "This happens once: after Mjolnir is installed and after the game is updated. It takes about half a minute, then the game starts by itself.",
            "Start without mods", "Starting…", "Downloading Unity's libraries…", "Reading the game's code…",
            "Describing the game's types…", "Building the assemblies mods use…", "Checking…");

        public static readonly Texts Russian = new(
            "Mjolnir готовится к этой версии ASKA",
            "Это делается один раз: после установки Mjolnir и после обновления игры. Около полуминуты, затем игра запустится сама.",
            "Запустить без модов", "Подготовка…", "Загрузка библиотек Unity…", "Чтение кода игры…",
            "Описание типов игры…", "Сборки для модов…", "Проверка…");

        public string Of(Stage stage) => stage switch
        {
            Stage.Download => Download,
            Stage.ReadCode => ReadCode,
            Stage.Types => Types,
            Stage.Assemblies => Assemblies,
            Stage.Verify => Verify,
            _ => Starting,
        };
    }

    // ---- Win32 ----

    private delegate IntPtr WndProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WndClassEx
    {
        public uint Size, Style;
        public IntPtr Proc;
        public int ClassExtra, WindowExtra;
        public IntPtr Instance, Icon, Cursor, Background;
        public string? MenuName, ClassName;
        public IntPtr SmallIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg { public IntPtr Window; public uint Message; public IntPtr WParam, LParam; public uint Time; public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct InitCommonControlsExArgs { public uint Size, Classes; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern ushort RegisterClassEx(ref WndClassEx cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string cls, string title, uint style, int x, int y, int w, int h,
                                                IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetMessage(out Msg msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref Msg msg);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DispatchMessage(ref Msg msg);
    [DllImport("user32.dll")] private static extern void PostQuitMessage(int code);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hwnd, int show);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowText(IntPtr hwnd, string text);
    [DllImport("user32.dll")] private static extern IntPtr SetTimer(IntPtr hwnd, IntPtr id, uint ms, IntPtr proc);
    [DllImport("user32.dll")] private static extern bool KillTimer(IntPtr hwnd, IntPtr id);
    [DllImport("user32.dll")] private static extern IntPtr LoadCursor(IntPtr instance, IntPtr name);
    [DllImport("user32.dll")] private static extern IntPtr GetSysColorBrush(int index);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern uint GetDpiForSystem();
    [DllImport("user32.dll")] private static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] private static extern bool AdjustWindowRectExForDpi(ref Rect rect, uint style, bool menu, uint exStyle, uint dpi);
    [DllImport("gdi32.dll")] private static extern int SetBkMode(IntPtr hdc, int mode);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic,
                                            uint underline, uint strikeOut, uint charSet, uint outPrecision, uint clipPrecision,
                                            uint quality, uint pitchAndFamily, string face);
    [DllImport("comctl32.dll")] private static extern bool InitCommonControlsEx(ref InitCommonControlsExArgs args);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
}
