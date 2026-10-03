using System;
using System.Diagnostics;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;

namespace Fort.ind_UWP
{
    public static class SocialThreads
    {
        public static bool Open(DependencyObject origin, SocialNote note, string noteId, bool focusReply, SocialThreadTab tab)
        {
            try
            {
                if (string.IsNullOrEmpty(noteId)) return false;

                var account = SocialContentService.CurrentAccountId();
                if (account == null) return false;

                var page = PageOf(origin);
                if (page == null || page.Frame == null) return false;

                var current = page as SocialNotePage;
                if (current != null && current.Shows(noteId))
                {
                    current.Reveal(focusReply, tab);
                    return true;
                }

                return page.Frame.Navigate(typeof(SocialNotePage), new SocialNoteArgs(account, noteId, note, focusReply, tab));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialThreads: could not open the note - {ex.GetType().Name}: {ex.Message}"
                                + (ex.InnerException != null ? $" | inner: {ex.InnerException.Message}" : ""));
                return false;
            }
        }

        private static Page PageOf(DependencyObject origin)
        {
            var current = origin;
            while (current != null)
            {
                var page = current as Page;
                if (page != null) return page;

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }
    }
}
