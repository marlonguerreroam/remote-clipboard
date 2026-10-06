// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;
using RemoteClipboard.Core.Configuration;

namespace RemoteClipboard.App.Services;

/// <summary>
/// Light/dark theme. "System" follows the Windows app theme (AppsUseLightTheme) and reacts live when the
/// user changes it. Title bars are darkened through DWM where supported (Windows 10 2004+ / 11).
/// On Windows 11 22H2+ windows get the system acrylic backdrop (real blur of what is behind them) under
/// a translucent tint; elsewhere, in remote sessions, with transparency effects off or in high contrast,
/// they keep an opaque gradient. Both follow the same liquid-glass resources.
/// </summary>
internal sealed partial class ThemeManager : IDisposable
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmSystemBackdropType = 38;
    private const int BackdropNone = 1;
    private const int BackdropAcrylic = 3; // DWMSBT_TRANSIENTWINDOW
    private const int FirstBackdropBuild = 22621; // Windows 11 22H2

    // Component URIs: valid regardless of which assembly started the process (the app or the UI tests).
    private static readonly Uri LightUri = new("pack://application:,,,/RemoteClipboard;component/Themes/Light.xaml");
    private static readonly Uri DarkUri = new("pack://application:,,,/RemoteClipboard;component/Themes/Dark.xaml");

    private AppTheme _preference = AppTheme.System;
    private bool _applied;

    public ThemeManager() => SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

    public bool IsDark { get; private set; }

    /// <summary>True when windows use the Windows 11 acrylic backdrop instead of an opaque background.</summary>
    public bool UsesBackdrop { get; private set; }

    public void Apply(AppTheme preference)
    {
        var isDark = preference == AppTheme.Dark || (preference == AppTheme.System && SystemPrefersDark());
        var usesBackdrop = BackdropAvailable();
        if (_applied && preference == _preference && isDark == IsDark && usesBackdrop == UsesBackdrop)
        {
            return; // UserPreferenceChanged fires for many unrelated settings
        }

        _applied = true;
        _preference = preference;
        IsDark = isDark;
        UsesBackdrop = usesBackdrop;

        var theme = new ResourceDictionary { Source = IsDark ? DarkUri : LightUri };
        if (UsesBackdrop)
        {
            // The window background becomes a translucent tint so the acrylic shows through.
            theme["WindowBackground"] = theme["WindowTint"];
        }

        Application.Current.Resources.MergedDictionaries[0] = theme;
        foreach (Window window in Application.Current.Windows)
        {
            ApplyFrame(window);
        }
    }

    /// <summary>Call from each window's constructor.</summary>
    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SourceInitialized += (_, _) => ApplyFrame(window);
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        // General: light/dark and transparency effects; Accessibility/Color: high contrast.
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Accessibility or UserPreferenceCategory.Color)
        {
            Application.Current?.Dispatcher.BeginInvoke(() => Apply(_preference));
        }
    }

    private void ApplyFrame(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        ApplyTitleBar(handle);
        ApplyBackdrop(handle);
    }

    private void ApplyTitleBar(IntPtr handle)
    {
        var value = IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
        {
            // Older Windows 10 builds used attribute 19; unsupported versions simply keep a light title bar.
            _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
        }
    }

    private void ApplyBackdrop(IntPtr handle)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, FirstBackdropBuild))
        {
            return; // no system backdrops: the opaque background is already in place
        }

        // The backdrop is drawn in the window frame: extend the frame over the whole client area and let
        // WPF render on a transparent surface. Turning it off restores the defaults.
        var margins = UsesBackdrop ? new Margins(-1) : new Margins(0);
        _ = DwmExtendFrameIntoClientArea(handle, ref margins);
        if (HwndSource.FromHwnd(handle)?.CompositionTarget is { } target)
        {
            target.BackgroundColor = UsesBackdrop ? Colors.Transparent : SystemColors.WindowColor;
        }

        var type = UsesBackdrop ? BackdropAcrylic : BackdropNone;
        _ = DwmSetWindowAttribute(handle, DwmSystemBackdropType, ref type, sizeof(int));
    }

    private static bool BackdropAvailable() =>
        OperatingSystem.IsWindowsVersionAtLeast(10, 0, FirstBackdropBuild)
        && !SystemParameters.HighContrast
        && !SystemParameters.IsRemoteSession // RDP draws backdrops as flat color; the gradient looks better
        && TransparencyEffectsEnabled();

    private static bool TransparencyEffectsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        // Settings → Personalization → Colors → Transparency effects. Missing value means enabled.
        return key?.GetValue("EnableTransparency") is not int value || value != 0;
    }

    private static bool SystemPrefersDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        // Missing value (e.g. some Windows Server installs) means light.
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct Margins(int all)
    {
        public readonly int Left = all;
        public readonly int Right = all;
        public readonly int Top = all;
        public readonly int Bottom = all;
    }
}
