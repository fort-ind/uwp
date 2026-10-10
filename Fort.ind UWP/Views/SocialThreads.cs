using System;
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
                AppLog.Error("SocialThreads: could not open the note", ex);
                return false;
            }
        }

        public static bool OpenTag(DependencyObject origin, string tag)
        {
            try
            {
                var name = tag == null ? null : tag.Trim().TrimStart('#');
                if (string.IsNullOrEmpty(name)) return false;

                var page = PageOf(origin);
                if (page == null || page.Frame == null) return false;

                var current = page as SocialTagPage;
                if (current != null && current.Shows(name)) return true;

                return page.Frame.Navigate(typeof(SocialTagPage), new SocialTagArgs(SocialContentService.CurrentAccountId(), name));
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialThreads: could not open the tag", ex);
                return false;
            }
        }

        public static bool IsInPlacePage(Type pageType)
        {
            return pageType == typeof(SocialNotePage) || pageType == typeof(SocialTagPage);
        }

        public static void ReturnToProfile(Page page, object parameter)
        {
            var args = parameter as SocialUserWindowArgs;
            if (args == null || page.Frame == null) return;

            if (!SocialContentService.IsCurrentAccount(args.AccountId))
            {
                WindowManagerService.CloseCurrentWindow();
                return;
            }

            var stack = page.Frame.BackStack;
            var target = -1;
            for (var i = stack.Count - 1; i >= 0; i--)
            {
                if (stack[i].SourcePageType == typeof(SocialUserPage))
                {
                    target = i;
                    break;
                }
            }

            if (target < 0) return;

            while (stack.Count - 1 > target)
            {
                stack.RemoveAt(stack.Count - 1);
            }

            page.Frame.GoBack();
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
