using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation.Metadata;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

namespace Fort.ind_UWP
{
    public static class DialogService
    {
        private static readonly SemaphoreSlim s_gate = new SemaphoreSlim(1, 1);

        private static readonly bool s_xamlRootSupported =
            ApiInformation.IsPropertyPresent("Windows.UI.Xaml.UIElement", "XamlRoot");

        public static bool IsDialogOpen
        {
            get { return s_gate.CurrentCount == 0; }
        }

        public static void AttachToOwner(ContentDialog dialog, UIElement owner)
        {
            if (s_xamlRootSupported && owner != null)
            {
                dialog.XamlRoot = owner.XamlRoot;
            }

            var themed = owner as FrameworkElement ?? Window.Current?.Content as FrameworkElement;
            if (themed != null)
            {
                dialog.RequestedTheme = themed.ActualTheme;
            }
        }

        public static async Task<bool> ShowMessageAsync(UIElement owner, string title, string content, string closeText)
        {
            return await RunGatedAsync(true, async () =>
            {
                var dialog = new ContentDialog()
                {
                    Title = title,
                    Content = content,
                    CloseButtonText = closeText
                };
                AttachToOwner(dialog, owner);
                await dialog.ShowAsync();
            });
        }

        public static async Task<bool> ShowConfirmAsync(UIElement owner,
                                                        string title,
                                                        string content,
                                                        string primaryText,
                                                        string closeText,
                                                        ContentDialogButton defaultButton)
        {
            bool confirmed = false;

            await RunExclusiveAsync(async () =>
            {
                confirmed = await ShowConfirmCoreAsync(owner, title, content, primaryText, closeText, defaultButton);
            });

            return confirmed;
        }

        public static async Task<bool> ShowConfirmCoreAsync(UIElement owner,
                                                            string title,
                                                            string content,
                                                            string primaryText,
                                                            string closeText,
                                                            ContentDialogButton defaultButton)
        {
            var dialog = new ContentDialog()
            {
                Title = title,
                Content = content,
                PrimaryButtonText = primaryText,
                CloseButtonText = closeText,
                DefaultButton = defaultButton
            };
            AttachToOwner(dialog, owner);

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        public static async Task<ContentDialogResult> ShowAsync(UIElement owner, ContentDialog dialog)
        {
            var result = ContentDialogResult.None;

            await RunExclusiveAsync(async () =>
            {
                AttachToOwner(dialog, owner);
                result = await dialog.ShowAsync();
            });

            return result;
        }

        public static Task<bool> RunExclusiveAsync(Func<Task> body)
        {
            return RunGatedAsync(false, body);
        }

        private static async Task<bool> RunGatedAsync(bool waitForGate, Func<Task> body)
        {
            if (waitForGate)
            {
                await s_gate.WaitAsync();
            }
            else if (!await s_gate.WaitAsync(0))
            {
                return false;
            }

            try
            {
                await body();
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"DialogService: dialog failed - {ex.Message}");
                return false;
            }
            finally
            {
                s_gate.Release();
            }
        }
    }
}
