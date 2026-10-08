using System;
using System.Diagnostics;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class SocialUserPage : Page, IReleasablePage, IReopenablePage, IImmersiveWindowPage, ITrimmablePage
    {
        private SocialUserWindowArgs _args;

        public SocialUserPage()
        {
            this.InitializeComponent();

            ProfileView.FollowListRequested += ProfileView_FollowListRequested;
            ProfileView.ProfileLoaded += ProfileView_ProfileLoaded;
        }

        public event EventHandler TitleBarChanged
        {
            add { ProfileView.TitleBarChanged += value; }
            remove { ProfileView.TitleBarChanged -= value; }
        }

        public bool ExtendsUnderTitleBar
        {
            get { return ProfileView.ExtendsUnderTitleBar; }
        }

        public ElementTheme TitleBarTheme
        {
            get { return ProfileView.TitleBarTheme; }
        }

        public void SetTitleBarInset(double inset)
        {
            ProfileView.SetTitleBarInset(inset);
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            try
            {
                if (e.NavigationMode == NavigationMode.Back && _args != null)
                {
                    if (!SocialContentService.IsCurrentAccount(_args.AccountId)) WindowManagerService.CloseCurrentWindow();
                    return;
                }

                Show(e.Parameter as SocialUserWindowArgs);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialUserPage: could not show the profile - {ex.GetType().Name}: {ex.Message}");
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);

            if (SocialThreads.IsInPlacePage(e.SourcePageType)) ProfileView.KeepListsOnNextUnload = true;
        }

        public void Reopen(object parameter)
        {
            var args = parameter as SocialUserWindowArgs;
            if (args == null || _args == null) return;

            if (!SocialContentService.IsCurrentAccount(args.AccountId))
            {
                WindowManagerService.CloseCurrentWindow();
            }
        }

        public void Release()
        {
            ProfileView.Release();
        }

        public void Trim()
        {
            ProfileView.Trim();
        }

        private void Show(SocialUserWindowArgs args)
        {
            if (args == null) return;

            if (!SocialContentService.IsCurrentAccount(args.AccountId))
            {
                WindowManagerService.CloseCurrentWindow();
                return;
            }

            _args = args;
            ProfileView.ShowUser(args.UserId, args.Handle, args.User, args.Detail);
        }

        private void ProfileView_ProfileLoaded(object sender, SocialUserDetail detail)
        {
            WindowManagerService.SetCurrentWindowTitle(SocialNoteItem.DisplayNameOf(detail.User));
        }

        private void ProfileView_FollowListRequested(object sender, SocialFollowList list)
        {
            try
            {
                if (_args == null || Frame == null) return;

                Frame.Navigate(typeof(SocialUserListPage),
                               new SocialUserListArgs(_args.AccountId, _args.UserId, ProfileView.Handle, ProfileView.User, list));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialUserPage: could not open the follow list - {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
