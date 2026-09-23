using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Fort.ind_UWP
{
    public static class AppearanceService
    {
        public static event EventHandler Changed;

        private static readonly Color s_surfaceTintDark = Color.FromArgb(255, 0x2B, 0x2B, 0x2B);
        private static readonly Color s_surfaceTintLight = Color.FromArgb(255, 0xF2, 0xF2, 0xF2);

        private static readonly Color s_paneTintDark = Color.FromArgb(255, 0x1F, 0x1F, 0x1F);
        private static readonly Color s_paneTintLight = Color.FromArgb(255, 0xE6, 0xE6, 0xE6);

        private static readonly string[] s_paneBrushKeys =
        {
            "NavigationViewExpandedPaneBackground",
            "NavigationViewDefaultPaneBackground",
        };

        private static List<KeyValuePair<bool, AcrylicBrush>> s_paneBrushes;

        private static AcrylicBrush s_surfaceBrush;

        private static volatile SurfacePaint s_surfacePaint;

        private static readonly object s_windowSurfacesLock = new object();

        private static readonly List<WindowSurface> s_windowSurfaces = new List<WindowSurface>();

        private static readonly Debouncer s_persistDebouncer = new Debouncer();

        private static string s_themePaintKey;

        private static bool s_acrylicUnsaved;

        public static string Theme { get; private set; } = AppConstants.ThemeDefault;

        public static string TintTag { get; private set; } = AppConstants.ThemeDefault;

        public static string TintScope { get; private set; } = AppConstants.TintScopeDefault;

        public static double BodyAcrylicOpacity { get; private set; } = AppConstants.DefaultBodyAcrylicOpacity;

        public static double PaneAcrylicOpacity { get; private set; } = AppConstants.DefaultPaneAcrylicOpacity;

        public static AcrylicBrush SurfaceBrush
        {
            get
            {
                if (s_surfaceBrush == null)
                {
                    s_surfaceBrush = new AcrylicBrush()
                    {
                        BackgroundSource = AcrylicBackgroundSource.HostBackdrop
                    };
                }
                return s_surfaceBrush;
            }
        }

        public static void Initialize()
        {
            try
            {
                var localSettings = ApplicationData.Current.LocalSettings;

                Theme = ReadTag(localSettings, AppConstants.SettingAppTheme, AppConstants.ThemeDefault);
                ApplyThemeToRootFrame(Theme);

                var tintTag = ReadTag(localSettings, AppConstants.SettingAppTintColor, AppConstants.ThemeDefault);
                if (!IsUsableTintTag(tintTag))
                {
                    Debug.WriteLine($"AppearanceService: tint tag '{tintTag}' is not a colour; using the default surface");
                    tintTag = AppConstants.ThemeDefault;
                }
                TintTag = tintTag;

                BodyAcrylicOpacity = ReadOpacity(localSettings, AppConstants.SettingAppBodyAcrylicOpacity,
                                                 AppConstants.DefaultBodyAcrylicOpacity);
                PaneAcrylicOpacity = ReadOpacity(localSettings, AppConstants.SettingAppPaneAcrylicOpacity,
                                                 AppConstants.DefaultPaneAcrylicOpacity);

                TintScope = AppConstants.TintScopeDefault;
                if (localSettings.Values.ContainsKey(AppConstants.SettingAppTintScope))
                {
                    var saved = localSettings.Values[AppConstants.SettingAppTintScope]?.ToString();
                    if (IsUsableTintScope(saved)) TintScope = saved;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppearanceService: Initialize failed - {ex.Message}");
            }

            s_acrylicUnsaved = false;
            s_themePaintKey = null;
            RepaintSurfaces();
            s_themePaintKey = PaintKey();
        }

        public static void Reload()
        {
            Initialize();
            RaiseChanged();
        }

        public static void SetTheme(string theme)
        {
            if (theme != AppConstants.ThemeLight && theme != AppConstants.ThemeDark)
            {
                theme = AppConstants.ThemeDefault;
            }

            Theme = theme;
            ApplyThemeToRootFrame(theme);

            try
            {
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTheme] = theme;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppearanceService: could not save the theme - {ex.Message}");
            }

            RepaintThemeDependentChrome();
        }

        public static void SetTint(string colorTag, bool persist)
        {
            if (!IsUsableTintTag(colorTag))
            {
                Debug.WriteLine($"AppearanceService: tint tag '{colorTag}' is not a colour; using the default surface");
                colorTag = AppConstants.ThemeDefault;
            }

            s_themePaintKey = null;
            TintTag = colorTag;
            RepaintSurfaces();

            if (!persist) return;

            try
            {
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTintColor] = colorTag;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppearanceService: could not save the tint - {ex.Message}");
            }
        }

        public static void SetTintScope(string scope)
        {
            if (!IsUsableTintScope(scope)) return;

            TintScope = scope;

            try
            {
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingAppTintScope] = scope;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppearanceService: could not save the tint scope - {ex.Message}");
            }

            RepaintSurfaces();
        }

        public static void SetAcrylicOpacities(double body, double pane)
        {
            BodyAcrylicOpacity = body;
            PaneAcrylicOpacity = pane;
            s_acrylicUnsaved = true;
            RepaintSurfaces();
            QueueAcrylicPersist();
        }

        public static void FlushAcrylicPersist()
        {
            if (!s_acrylicUnsaved) return;

            try
            {
                s_persistDebouncer.Cancel();
                PersistAcrylicOpacities();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppearanceService: could not flush the acrylic opacities - {ex.Message}");
            }
        }

        private static async void QueueAcrylicPersist()
        {
            try
            {
                var token = s_persistDebouncer.Restart();

                await Task.Delay(AppConstants.AcrylicPersistDebounceMilliseconds);
                if (token.IsCancellationRequested) return;

                PersistAcrylicOpacities();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppearanceService: could not save the acrylic opacities - {ex.Message}");
            }
        }

        private static void PersistAcrylicOpacities()
        {
            var values = ApplicationData.Current.LocalSettings.Values;
            values[AppConstants.SettingAppBodyAcrylicOpacity] = BodyAcrylicOpacity;
            values[AppConstants.SettingAppPaneAcrylicOpacity] = PaneAcrylicOpacity;
            s_acrylicUnsaved = false;
        }

        public static void RepaintThemeDependentChrome()
        {
            var key = PaintKey();
            if (string.Equals(key, s_themePaintKey, StringComparison.Ordinal)) return;

            RepaintSurfaces();
            s_themePaintKey = key;

            RaiseChanged();
        }

        private static string PaintKey()
        {
            return (IsEffectiveThemeDark() ? "Dark|" : "Light|") + TintTag;
        }

        private static void RaiseChanged()
        {
            var handler = Changed;
            if (handler == null) return;

            try
            {
                handler(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppearanceService: a Changed subscriber threw - {ex.Message}");
            }
        }

        private static void RepaintSurfaces()
        {
            try
            {
                var isDark = IsEffectiveThemeDark();
                var colorTag = TintTag;
                var isTinted = !(string.IsNullOrEmpty(colorTag) || colorTag == AppConstants.ThemeDefault);

                var tintBody = isTinted && TintScope != AppConstants.TintScopeSidebar;
                var tintPane = isTinted && TintScope != AppConstants.TintScopeContent;

                var paint = new SurfacePaint(
                    tintBody ? ColorHelper.HexToColor(colorTag) : s_surfaceTintDark,
                    tintBody ? ColorHelper.ForLightTheme(colorTag) : s_surfaceTintLight,
                    BodyAcrylicOpacity);
                s_surfacePaint = paint;

                paint.ApplyTo(SurfaceBrush, isDark);

                RepaintPaneBrushes(colorTag, tintPane);

                RepaintWindowSurfaces();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppearanceService: RepaintSurfaces failed - {ex.Message}");
            }
        }

        public static AcrylicBrush AttachWindowSurface(FrameworkElement windowRoot)
        {
            var surface = new WindowSurface(windowRoot, new AcrylicBrush()
            {
                BackgroundSource = AcrylicBackgroundSource.HostBackdrop
            });

            lock (s_windowSurfacesLock)
            {
                s_windowSurfaces.Add(surface);
            }

            ApplyThemeTo(windowRoot);
            PaintWindowSurface(surface);

            return surface.Brush;
        }

        public static void RepaintWindowSurface(FrameworkElement windowRoot)
        {
            var surface = FindWindowSurface(windowRoot);
            if (surface != null) PaintWindowSurface(surface);
        }

        public static void DetachWindowSurface(FrameworkElement windowRoot)
        {
            lock (s_windowSurfacesLock)
            {
                s_windowSurfaces.RemoveAll(s => s.Root == windowRoot);
            }
        }

        private static WindowSurface FindWindowSurface(FrameworkElement windowRoot)
        {
            lock (s_windowSurfacesLock)
            {
                return s_windowSurfaces.Find(s => s.Root == windowRoot);
            }
        }

        private static WindowSurface[] WindowSurfacesSnapshot()
        {
            lock (s_windowSurfacesLock)
            {
                return s_windowSurfaces.ToArray();
            }
        }

        private static void RepaintWindowSurfaces()
        {
            foreach (var surface in WindowSurfacesSnapshot())
            {
                var target = surface;
                RunOnWindow(target, () => PaintWindowSurface(target));
            }
        }

        private static void PaintWindowSurface(WindowSurface surface)
        {
            try
            {
                var paint = s_surfacePaint;
                if (paint == null) return;

                paint.ApplyTo(surface.Brush, IsEffectiveThemeDark(surface.Root));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppearanceService: could not paint a secondary window - {ex.Message}");
            }
        }

        private static void RunOnWindow(WindowSurface surface, DispatchedHandler action)
        {
            try
            {
                var ignored = surface.Dispatcher.RunAsync(CoreDispatcherPriority.Normal, action);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppearanceService: could not reach a secondary window - {ex.Message}");
            }
        }

        private static void RepaintPaneBrushes(string colorTag, bool tinted)
        {
            var darkTint = tinted ? ColorHelper.HexToColor(colorTag) : s_paneTintDark;
            var lightTint = tinted ? ColorHelper.ForLightTheme(colorTag) : s_paneTintLight;

            foreach (var pair in PaneAcrylicBrushes())
            {
                var tint = pair.Key ? darkTint : lightTint;
                pair.Value.TintColor = tint;
                pair.Value.FallbackColor = tint;
                pair.Value.TintOpacity = PaneAcrylicOpacity;
                pair.Value.AlwaysUseFallback = PaneAcrylicOpacity >= 1.0;
            }
        }

        private static List<KeyValuePair<bool, AcrylicBrush>> PaneAcrylicBrushes()
        {
            if (s_paneBrushes != null) return s_paneBrushes;

            var found = new List<KeyValuePair<bool, AcrylicBrush>>(s_paneBrushKeys.Length * 2);

            try
            {
                var themes = AccentColorService.FindOverrideDictionary(Application.Current.Resources).ThemeDictionaries;

                CollectPaneBrushes(themes, "Default", true, found);
                CollectPaneBrushes(themes, AppConstants.ThemeLight, false, found);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppearanceService: could not reach the pane acrylic brushes - {ex.Message}");
            }

            if (found.Count > 0) s_paneBrushes = found;

            return found;
        }

        private static void CollectPaneBrushes(IDictionary<object, object> themes, string themeKey, bool isDark,
                                               List<KeyValuePair<bool, AcrylicBrush>> into)
        {
            object entry;
            if (!themes.TryGetValue(themeKey, out entry)) return;

            var dictionary = entry as ResourceDictionary;
            if (dictionary == null) return;

            foreach (var key in s_paneBrushKeys)
            {
                object value;
                if (!dictionary.TryGetValue(key, out value)) continue;

                var acrylic = value as AcrylicBrush;
                if (acrylic != null)
                {
                    into.Add(new KeyValuePair<bool, AcrylicBrush>(isDark, acrylic));
                }
            }
        }

        private static void ApplyThemeToRootFrame(string theme)
        {
            ApplyTheme(Window.Current.Content as FrameworkElement, theme);

            foreach (var surface in WindowSurfacesSnapshot())
            {
                var target = surface;
                RunOnWindow(target, () =>
                {
                    try
                    {
                        ApplyTheme(target.Root, theme);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"AppearanceService: could not theme a secondary window - {ex.Message}");
                    }
                });
            }
        }

        public static void ApplyThemeTo(FrameworkElement windowRoot)
        {
            ApplyTheme(windowRoot, Theme);
        }

        private static void ApplyTheme(FrameworkElement root, string theme)
        {
            if (root == null) return;

            switch (theme)
            {
                case AppConstants.ThemeLight: root.RequestedTheme = ElementTheme.Light; break;
                case AppConstants.ThemeDark: root.RequestedTheme = ElementTheme.Dark; break;
                default: root.RequestedTheme = ElementTheme.Default; break;
            }
        }

        public static bool IsEffectiveThemeDark()
        {
            return IsEffectiveThemeDark(Window.Current.Content as FrameworkElement);
        }

        public static bool IsEffectiveThemeDark(FrameworkElement windowRoot)
        {
            if (windowRoot == null)
            {
                return Application.Current.RequestedTheme == ApplicationTheme.Dark;
            }

            if (windowRoot.RequestedTheme != ElementTheme.Default)
            {
                return windowRoot.RequestedTheme == ElementTheme.Dark;
            }

            return windowRoot.ActualTheme == ElementTheme.Dark;
        }

        public static bool IsUsableTintTag(string colorTag)
        {
            if (string.IsNullOrEmpty(colorTag) || colorTag == AppConstants.ThemeDefault) return true;

            Color ignored;
            return ColorHelper.TryHexToColor(colorTag, out ignored);
        }

        public static bool IsUsableTintScope(string scope)
        {
            return scope == AppConstants.TintScopeContent
                   || scope == AppConstants.TintScopeSidebar
                   || scope == AppConstants.TintScopeBoth;
        }

        private static string ReadTag(ApplicationDataContainer localSettings, string key, string fallback)
        {
            if (!localSettings.Values.ContainsKey(key)) return fallback;
            return localSettings.Values[key]?.ToString() ?? fallback;
        }

        private static double ReadOpacity(ApplicationDataContainer localSettings, string key, double fallback)
        {
            try
            {
                if (!localSettings.Values.ContainsKey(key)) return fallback;

                var raw = localSettings.Values[key];
                if (raw == null) return fallback;

                var value = Convert.ToDouble(raw);
                if (double.IsNaN(value)) return fallback;

                return Math.Max(AppConstants.MinimumAcrylicOpacity, Math.Min(1.0, value));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AppearanceService: could not read {key} - {ex.Message}");
                return fallback;
            }
        }

        private sealed class SurfacePaint
        {
            private readonly Color _darkTint;
            private readonly Color _lightTint;
            private readonly double _opacity;

            public SurfacePaint(Color darkTint, Color lightTint, double opacity)
            {
                _darkTint = darkTint;
                _lightTint = lightTint;
                _opacity = opacity;
            }

            public void ApplyTo(AcrylicBrush brush, bool isDark)
            {
                var tint = isDark ? _darkTint : _lightTint;
                brush.TintColor = tint;
                brush.TintOpacity = _opacity;
                brush.FallbackColor = tint;
                brush.AlwaysUseFallback = _opacity >= 1.0;
            }
        }

        private sealed class WindowSurface
        {
            public WindowSurface(FrameworkElement root, AcrylicBrush brush)
            {
                Root = root;
                Brush = brush;
                Dispatcher = root.Dispatcher;
            }

            public FrameworkElement Root { get; private set; }

            public AcrylicBrush Brush { get; private set; }

            public CoreDispatcher Dispatcher { get; private set; }
        }
    }
}
