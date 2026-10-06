// Copyright (c) 2026 Marlon Andrés Guerrero Meriño
// SPDX-License-Identifier: GPL-3.0-only

using System.Runtime.ExceptionServices;
using System.Runtime.Versioning;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Shapes;
using System.Windows.Threading;
using RemoteClipboard.App.Services;
using RemoteClipboard.Core.Configuration;

namespace RemoteClipboard.App.Tests;

/// <summary>
/// Loads the real theme dictionaries, applies every style to a control in an off-screen window (with the
/// Windows 11 backdrop when the machine supports it) and fires the animated triggers. XAML errors in
/// templates, resources or storyboards only surface at run time, so this catches them in CI.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ThemeSmokeTests
{
    [Fact]
    public void Every_style_renders_in_both_themes() => RunOnSta(() =>
    {
        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Clear();
        app.Resources.MergedDictionaries.Add(Load("Themes/Light.xaml"));
        app.Resources.MergedDictionaries.Add(Load("Themes/Styles.xaml"));

        using var theme = new ThemeManager();
        foreach (var preference in new[] { AppTheme.Light, AppTheme.Dark })
        {
            theme.Apply(preference);
            Assert.Equal(preference == AppTheme.Dark, theme.IsDark);
            Assert.NotNull(app.Resources["WindowBackground"]);

            var window = BuildWindow(app, out var toggles);
            theme.Attach(window);
            window.Show();
            try
            {
                window.UpdateLayout();
                foreach (var toggle in toggles)
                {
                    toggle.IsChecked = true; // starts the switch storyboard
                    Drain(window.Dispatcher);
                    toggle.IsChecked = false;
                    Drain(window.Dispatcher);
                }

                theme.Apply(preference == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark); // live switch
                window.UpdateLayout();
                Drain(window.Dispatcher);
            }
            finally
            {
                window.Close();
            }
        }
    });

    private static Window BuildWindow(Application app, out List<ToggleButton> toggles)
    {
        Style Style(string key) => (Style)app.Resources[key];

        var switchBox = new CheckBox { Style = Style("Switch"), Content = "Switch" };
        var segment = new RadioButton { Style = Style("Segment"), Content = "Segment", IsChecked = true };
        var options = new ToggleButton { Style = Style("GlassToggle"), Content = "Opciones" };
        toggles = [switchBox, segment, options];

        var panel = new StackPanel
        {
            Children =
            {
                new TextBlock { Style = Style("Title"), Text = "Title" },
                new TextBlock { Style = Style("SectionTitle"), Text = "SECTION" },
                new TextBlock { Style = Style("Caption"), Text = "Caption" },
                new Border { Style = Style("Card"), Child = new TextBox { Style = Style("Input"), Text = "Input" } },
                new Button { Style = Style("PrimaryButton"), Content = "Primary" },
                new Button { Style = Style("GhostButton"), Content = "Ghost" },
                new Button { Style = Style("GlassListItem"), Content = "Item" },
                new Border { Style = Style("SegmentHost"), Child = segment },
                options,
                switchBox,
                new Ellipse { Style = Style("StatusDot"), Width = 10, Height = 10, Tag = true },
                new Ellipse { Style = Style("StatusDot"), Width = 10, Height = 10, Tag = false },
            },
        };

        return new Window
        {
            Style = Style("AppWindow"),
            Content = panel,
            Width = 400,
            Height = 600,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -20000, // off screen
            Top = -20000,
            ShowInTaskbar = false,
            ShowActivated = false,
        };
    }

    private static ResourceDictionary Load(string path) =>
        (ResourceDictionary)Application.LoadComponent(new Uri($"/RemoteClipboard;component/{path}", UriKind.Relative));

    private static void Drain(Dispatcher dispatcher) => dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);

    private static void RunOnSta(Action action)
    {
        ExceptionDispatchInfo? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        failure?.Throw();
    }
}
