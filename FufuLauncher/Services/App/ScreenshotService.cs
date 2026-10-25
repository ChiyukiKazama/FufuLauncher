/*
Copyright (c) FufuLauncher Dev Team. All rights reserved.
Licensed under the MIT License.
*/

using System.Diagnostics;
using System.Runtime.InteropServices;
using CommunityToolkit.Mvvm.Messaging;
using FufuLauncher.Contracts.Services;
using FufuLauncher.Helpers;
using FufuLauncher.Messages;
using Windows.Graphics;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.System;

namespace FufuLauncher.Services;

public class ScreenshotService : IScreenshotService, IDisposable
{
    private readonly ILocalSettingsService _settingsService;
    private readonly INotificationService _notificationService;

    private const string ScreenshotEnabledKey = "IsScreenshotEnabled";
    private const string ScreenshotHotkeyKey = "ScreenshotHotkey";
    private const string ScreenshotSavePathKey = "ScreenshotSavePath";

    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int WM_SYSKEYDOWN = 0x0104;

    private IntPtr _keyboardHookId = IntPtr.Zero;
    private readonly LowLevelKeyboardProc _keyboardHookCallback;
    private Thread _hookThread;

    private int _gamePid;
    private IntPtr _gameWindowHandle = IntPtr.Zero;
    private bool _isRunning;
    private bool _isCapturing;

    private VirtualKey _hotkey = VirtualKey.F12;
    private bool _hotkeyCtrl;
    private bool _hotkeyAlt;
    private bool _hotkeyShift;

    public bool IsRunning => _isRunning;

    public ScreenshotService(ILocalSettingsService settingsService, INotificationService notificationService)
    {
        _settingsService = settingsService;
        _notificationService = notificationService;
        _keyboardHookCallback = KeyboardHookCallback;
    }

    public async Task StartAsync(int gamePid)
    {
        if (_isRunning) return;

        var enabledObj = await _settingsService.ReadSettingAsync(ScreenshotEnabledKey);
        if (enabledObj == null || !Convert.ToBoolean(enabledObj))
            return;

        _gamePid = gamePid;
        await LoadHotkeySettingsAsync();

        _gameWindowHandle = FindMainWindowByPid(gamePid);
        if (_gameWindowHandle == IntPtr.Zero)
        {
            DebugLog.Info("ScreenshotService", "[Screenshot] Game window handle not found, retrying after wait...");
            await Task.Delay(3000);
            _gameWindowHandle = FindMainWindowByPid(gamePid);
        }

        if (_gameWindowHandle == IntPtr.Zero)
        {
            DebugLog.Info("ScreenshotService",
                "[Screenshot] Game window not found, screenshot service startup aborted");
            return;
        }

        _hookThread = new Thread(HookThreadProc)
        {
            IsBackground = true,
            Name = "ScreenshotHookThread"
        };
        _hookThread.Start();

        _isRunning = true;
        DebugLog.Info("ScreenshotService",
            $"[Screenshot] Started, listening for hotkey, game PID: {gamePid}, window handle: {_gameWindowHandle}");
    }

    public Task StopAsync()
    {
        if (!_isRunning) return Task.CompletedTask;

        try
        {
            if (_hookThread != null && _hookThread.IsAlive)
            {
                PostThreadMessage((uint)_hookThread.ManagedThreadId, 0x0012, IntPtr.Zero, IntPtr.Zero);
                _hookThread = null;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("ScreenshotService", $"[Screenshot] Stop error: {ex.Message}");
        }

        _isRunning = false;
        _gameWindowHandle = IntPtr.Zero;
        _gamePid = 0;
        DebugLog.Info("ScreenshotService", "[Screenshot] Stopped");
        return Task.CompletedTask;
    }

    private async Task LoadHotkeySettingsAsync()
    {
        try
        {
            var hotkeyObj = await _settingsService.ReadSettingAsync(ScreenshotHotkeyKey);
            var hotkeyStr = hotkeyObj?.ToString() ?? "F12";
            ParseHotkey(hotkeyStr);
        }
        catch
        {
            _hotkey = VirtualKey.F12;
            _hotkeyCtrl = false;
            _hotkeyAlt = false;
            _hotkeyShift = false;
        }
    }

    private void ParseHotkey(string hotkeyStr)
    {
        _hotkeyCtrl = false;
        _hotkeyAlt = false;
        _hotkeyShift = false;
        _hotkey = VirtualKey.F12;

        if (string.IsNullOrWhiteSpace(hotkeyStr)) return;

        var parts = hotkeyStr.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var upper = part.ToUpperInvariant();
            switch (upper)
            {
                case "CTRL":
                case "CONTROL":
                    _hotkeyCtrl = true;
                    break;
                case "ALT":
                    _hotkeyAlt = true;
                    break;
                case "SHIFT":
                    _hotkeyShift = true;
                    break;
                default:
                    if (Enum.TryParse<VirtualKey>(part, true, out var vk))
                        _hotkey = vk;
                    else if (int.TryParse(part, out var code))
                        _hotkey = (VirtualKey)code;
                    break;
            }
        }
    }

    #region Keyboard Hook

    private void HookThreadProc()
    {
        try
        {
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule;
            var moduleHandle = GetModuleHandle(curModule.ModuleName);
            _keyboardHookId = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardHookCallback, moduleHandle, 0);

            DebugLog.Info("ScreenshotService", _keyboardHookId == IntPtr.Zero
                ? "[Screenshot] Keyboard hook install failed"
                : "[Screenshot] Keyboard hook installed");

            if (_keyboardHookId != IntPtr.Zero)
            {
                MSG msg;
                while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
                {
                    TranslateMessage(ref msg);
                    DispatchMessage(ref msg);
                }

                UnhookWindowsHookEx(_keyboardHookId);
                _keyboardHookId = IntPtr.Zero;
                DebugLog.Info("ScreenshotService", "[Screenshot] Keyboard hook unloaded");
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("ScreenshotService", $"[Screenshot] HookThreadProc error: {ex.Message}");
        }
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && _isRunning)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            int flags = Marshal.ReadInt32(lParam, 8);
            bool isInjected = (flags & 0x10) != 0;

            if (!isInjected)
            {
                int wp = wParam.ToInt32();
                bool down = wp == WM_KEYDOWN || wp == WM_SYSKEYDOWN;

                if (down && (VirtualKey)vkCode == _hotkey && CheckModifiers())
                {
                    Task.Run(CaptureScreenshotAsync);
                }
            }
        }

        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private bool CheckModifiers()
    {
        bool ctrlPressed = (GetAsyncKeyState((int)VirtualKey.Control) & 0x8000) != 0;
        bool altPressed = (GetAsyncKeyState((int)VirtualKey.Menu) & 0x8000) != 0;
        bool shiftPressed = (GetAsyncKeyState((int)VirtualKey.Shift) & 0x8000) != 0;

        return ctrlPressed == _hotkeyCtrl && altPressed == _hotkeyAlt && shiftPressed == _hotkeyShift;
    }

    #endregion

    #region Screenshot Capture

    private async Task CaptureScreenshotAsync()
    {
        if (_isCapturing) return;
        _isCapturing = true;

        try
        {
            try
            {
                var proc = Process.GetProcessById(_gamePid);
                if (proc.HasExited)
                {
                    await StopAsync();
                    return;
                }
            }
            catch
            {
                await StopAsync();
                return;
            }

            if (!IsWindow(_gameWindowHandle))
            {
                _gameWindowHandle = FindMainWindowByPid(_gamePid);
                if (_gameWindowHandle == IntPtr.Zero)
                {
                    DebugLog.Info("ScreenshotService", "[Screenshot] Game window is no longer available");
                    return;
                }
            }

            var savePath = await GetSavePathAsync();
            Directory.CreateDirectory(savePath);

            var fileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png";
            var filePath = Path.Combine(savePath, fileName);

            var success = await CaptureWindowAsync(_gameWindowHandle, filePath);

            if (success)
            {
                DebugLog.Info("ScreenshotService", $"[Screenshot] Screenshot saved: {filePath}");
                WeakReferenceMessenger.Default.Send(new ScreenshotTakenMessage(filePath, true));
                _notificationService.Show("Screenshot_Saved".GetLocalized(), fileName, NotificationType.Success, 0);
            }
            else
            {
                DebugLog.Info("ScreenshotService", "[Screenshot] Screenshot failed");
                WeakReferenceMessenger.Default.Send(new ScreenshotTakenMessage("", false,
                    "Screenshot_CaptureFailed".GetLocalized()));
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("ScreenshotService", $"[Screenshot] Screenshot error: {ex.Message}");
            WeakReferenceMessenger.Default.Send(new ScreenshotTakenMessage("", false, ex.Message));
        }
        finally
        {
            _isCapturing = false;
        }
    }

    private async Task<bool> CaptureWindowAsync(IntPtr hwnd, string filePath)
    {
        IDirect3DDevice device = null;
        Direct3D11CaptureFramePool framePool = null;
        GraphicsCaptureSession session = null;
        Direct3D11CaptureFrame frame = null;

        try
        {
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
            var winUIWindowId = new Windows.UI.WindowId(windowId.Value);
            var captureItem = GraphicsCaptureItem.TryCreateFromWindowId(winUIWindowId);

            if (captureItem == null)
            {
                DebugLog.Info("ScreenshotService", "[Screenshot] Failed to create GraphicsCaptureItem");
                return false;
            }

            device = CreateDirect3DDevice();
            if (device == null)
            {
                DebugLog.Info("ScreenshotService", "[Screenshot] Failed to create Direct3D device");
                return false;
            }

            framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                device,
                DirectXPixelFormat.B8G8R8A8UIntNormalized,
                1,
                captureItem.Size);

            session = framePool.CreateCaptureSession(captureItem);

            try
            {
                session.IsBorderRequired = false;
            }
            catch
            {
            }

            var tcs = new TaskCompletionSource<Direct3D11CaptureFrame>();
            framePool.FrameArrived += (pool, _) =>
            {
                var f = pool.TryGetNextFrame();
                tcs.TrySetResult(f);
            };

            session.StartCapture();

            var completedTask = await Task.WhenAny(tcs.Task, Task.Delay(2000));

            session.Dispose();
            session = null;

            if (completedTask != tcs.Task)
            {
                DebugLog.Info("ScreenshotService", "[Screenshot] Capture timed out");
                return false;
            }

            frame = tcs.Task.Result;
            var frameSize = frame.ContentSize;

            var saved = await SaveFrameToFileAsync(frame, filePath, frameSize);
            return saved;
        }
        catch (Exception ex)
        {
            DebugLog.Info("ScreenshotService", $"[Screenshot] CaptureWindowAsync error: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
        finally
        {
            try
            {
                frame?.Dispose();
            }
            catch
            {
            }

            try
            {
                framePool?.Dispose();
            }
            catch
            {
            }

            try
            {
                session?.Dispose();
            }
            catch
            {
            }

            if (device != null)
            {
                try
                {
                    device.Dispose();
                }
                catch
                {
                    Marshal.FinalReleaseComObject(device);
                }
            }
        }
    }

    private async Task<bool> SaveFrameToFileAsync(Direct3D11CaptureFrame frame, string filePath, SizeInt32 size)
    {
        try
        {
            var softwareBitmap =
                await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied);

            using var stream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
            encoder.SetSoftwareBitmap(softwareBitmap);

            if (size.Width > 0 && size.Height > 0)
            {
                encoder.BitmapTransform.Bounds = new BitmapBounds
                {
                    X = 0,
                    Y = 0,
                    Width = (uint)size.Width,
                    Height = (uint)size.Height
                };
            }

            await encoder.FlushAsync();

            stream.Seek(0);
            var buffer = new byte[stream.Size];
            var reader = new DataReader(stream);
            await reader.LoadAsync((uint)stream.Size);
            reader.ReadBytes(buffer);

            await File.WriteAllBytesAsync(filePath, buffer);
            return true;
        }
        catch (Exception ex)
        {
            DebugLog.Info("ScreenshotService", $"[Screenshot] SaveFrameToFileAsync error: {ex.Message}");
            return false;
        }
    }

    private async Task<string> GetSavePathAsync()
    {
        try
        {
            var pathObj = await _settingsService.ReadSettingAsync(ScreenshotSavePathKey);
            var path = pathObj?.ToString()?.Trim('"')?.Trim();
            if (!string.IsNullOrEmpty(path))
                return path;
        }
        catch
        {
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "FufuScreenshots");
    }

    #endregion

    #region Window Finding

    private static IntPtr FindMainWindowByPid(int pid)
    {
        IntPtr foundHwnd = IntPtr.Zero;

        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out uint windowPid);
            if (windowPid == pid && IsWindowVisible(hwnd))
            {
                var style = GetWindowLong(hwnd, -16);
                if ((style & 0x40000000) == 0)
                {
                    var length = GetWindowTextLength(hwnd);
                    if (length > 0)
                    {
                        foundHwnd = hwnd;
                        return false;
                    }
                }
            }

            return true;
        }, IntPtr.Zero);

        return foundHwnd;
    }

    #endregion

    #region Direct3D Device Creation

    [DllImport("d3d11.dll")]
    private static extern int D3D11CreateDevice(
        IntPtr pAdapter,
        int DriverType,
        IntPtr Software,
        uint Flags,
        int[] pFeatureLevels,
        uint FeatureLevels,
        uint SDKVersion,
        out IntPtr ppDevice,
        out int pFeatureLevel,
        out IntPtr ppImmediateContext);

    [ComImport]
    [Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        IntPtr GetInterface([In] ref Guid iid);
    }


    private static IDirect3DDevice CreateDirect3DDevice()
    {
        try
        {
            var hr = D3D11CreateDevice(
                IntPtr.Zero,
                1,
                IntPtr.Zero,
                0x20,
                null,
                0,
                7,
                out var d3dDevice,
                out _,
                out var immediateContext);

            if (hr != 0 || d3dDevice == IntPtr.Zero)
            {
                DebugLog.Info("ScreenshotService",
                    $"[Screenshot] D3D11CreateDevice HARDWARE failed, HRESULT: 0x{hr:X8}");
                hr = D3D11CreateDevice(
                    IntPtr.Zero,
                    5,
                    IntPtr.Zero,
                    0x20,
                    null,
                    0,
                    7,
                    out d3dDevice,
                    out _,
                    out immediateContext);

                if (hr != 0 || d3dDevice == IntPtr.Zero)
                {
                    DebugLog.Info("ScreenshotService",
                        $"[Screenshot] D3D11CreateDevice WARP also failed, HRESULT: 0x{hr:X8}");
                    return null;
                }
            }

            if (immediateContext != IntPtr.Zero)
                Marshal.Release(immediateContext);


            var iidDxgi = new Guid("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
            hr = Marshal.QueryInterface(d3dDevice, ref iidDxgi, out var dxgiDevice);
            Marshal.Release(d3dDevice);

            if (hr != 0 || dxgiDevice == IntPtr.Zero)
            {
                DebugLog.Info("ScreenshotService",
                    $"[Screenshot] QueryInterface IDXGIDevice failed, HRESULT: 0x{hr:X8}");
                return null;
            }


            var inspectable = IntPtr.Zero;
            var funcPtr = GetCreateDirect3DDeviceFuncPtr();

            if (funcPtr != IntPtr.Zero)
            {
                var del = Marshal.GetDelegateForFunctionPointer<CreateDirect3D11DeviceFromDXGIDeviceDelegate>(funcPtr);
                hr = del(dxgiDevice, out inspectable);
            }

            Marshal.Release(dxgiDevice);

            if (hr != 0 || inspectable == IntPtr.Zero)
            {
                DebugLog.Info("ScreenshotService",
                    $"[Screenshot] CreateDirect3D11DeviceFromDXGIDevice failed, HRESULT: 0x{hr:X8}");
                return null;
            }


            var winrtDevice = MarshalIDirect3DDevice(inspectable);
            Marshal.Release(inspectable);

            if (winrtDevice == null)
            {
                DebugLog.Info("ScreenshotService", "[Screenshot] MarshalIDirect3DDevice returned null");
            }

            return winrtDevice;
        }
        catch (Exception ex)
        {
            DebugLog.Info("ScreenshotService",
                $"[Screenshot] CreateDirect3DDevice error: {ex.Message}\n{ex.StackTrace}");
            return null;
        }
    }

    private static IDirect3DDevice MarshalIDirect3DDevice(IntPtr inspectable)
    {
        try
        {
            var iidDirect3DDevice = new Guid("A37624AB-8D5F-4650-9D3E-9EAE3D9BC670");
            Marshal.QueryInterface(inspectable, ref iidDirect3DDevice, out var devicePtr);

            if (devicePtr != IntPtr.Zero)
            {
                var device = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(devicePtr);
                Marshal.Release(devicePtr);
                return device;
            }
        }
        catch (Exception ex)
        {
            DebugLog.Info("ScreenshotService", $"[Screenshot] MarshalInterface approach failed: {ex.Message}");
        }

        return null;
    }

    private static IntPtr GetCreateDirect3DDeviceFuncPtr()
    {
        string[] dlls = { "d3d11.dll", "api-ms-win-gaming-deviceinformation-l1-1-0.dll" };
        foreach (var dll in dlls)
        {
            var hModule = LoadLibraryEx(dll, IntPtr.Zero, 0);
            if (hModule == IntPtr.Zero) continue;
            var proc = GetProcAddress(hModule, "CreateDirect3D11DeviceFromDXGIDevice");
            if (proc != IntPtr.Zero) return proc;
        }

        return IntPtr.Zero;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateDirect3D11DeviceFromDXGIDeviceDelegate(IntPtr dxgiDevice, out IntPtr graphicsDevice);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr LoadLibraryEx(string lpFileName, IntPtr hFile, uint dwFlags);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    #endregion

    #region Dispose

    public void Dispose()
    {
        _ = StopAsync();
    }

    #endregion

    #region P/Invoke

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
    }

    [DllImport("user32.dll")]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    #endregion
}