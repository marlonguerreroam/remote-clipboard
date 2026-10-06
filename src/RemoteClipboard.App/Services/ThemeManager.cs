// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using RemoteClipboard.Core.Configuration;

namespace RemoteClipboard.App.Services;

/// <summary>
/// Light/dark theme. "System" follows the Windows app theme (AppsUseLightTheme) and reacts live when the
/// user changes it. Title bars are darkened through DWM where supported (Windows 10 2004+ / 11).
/// </summary>
internal sealed partial class ThemeManager : IDisposable
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmUseImmersiveDarkModeBefore20H1 = 19;

    private static readonly Uri LightUri = new("Themes/Light.xaml", UriKind.Relative);
    private static readonly Uri DarkUri = new("Themes/Dark.xaml", UriKind.Relative);

    private AppTheme _preference = AppTheme.System;

    public ThemeManager() => SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;

    public bool IsDark { get; private set; }

    public void Apply(AppTheme preference)
    {
        _preference = preference;
        IsDark = preference == AppTheme.Dark || (preference == AppTheme.System && SystemPrefersDark());

        var dictionaries = Application.Current.Resources.MergedDictionaries;
        dictionaries[0] = new ResourceDictionary { Source = IsDark ? DarkUri : LightUri };
        foreach (Window window in Application.Current.Windows)
        {
            ApplyTitleBar(window);
        }
    }

    /// <summary>Call from each window's constructor.</summary>
    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        window.SourceInitialized += (_, _) => ApplyTitleBar(window);
    }

    public void Dispose() => SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && _preference == AppTheme.System)
        {
            Application.Current?.Dispatcher.BeginInvoke(() => Apply(AppTheme.System));
        }
    }

    private void ApplyTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var value = IsDark ? 1 : 0;
        if (DwmSetWindowAttribute(handle, DwmUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
        {
            // Older Windows 10 builds used attribute 19; unsupported versions simply keep a light title bar.
            _ = DwmSetWindowAttribute(handle, DwmUseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
        }
    }

    private static bool SystemPrefersDark()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        // Missing value (e.g. some Windows Server installs) means light.
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    [LibraryImport("dwmapi.dll")]
    private static partial int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
