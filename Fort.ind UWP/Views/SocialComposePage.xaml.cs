using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Storage;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;

namespace Fort.ind_UWP
{
    public sealed partial class SocialComposePage : Page, IReleasablePage, IReopenablePage
    {
        private const string QuoteSlotPrefix = "quote:";

        private const string RedraftSlotPrefix = "redraft:";

        private readonly Debouncer _sizeDebounce = new Debouncer();

        private SocialComposeArgs _args;

        private string _slot;

        private int _showVersion;

        private bool _finished;

        private bool _released;

        public SocialComposePage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            var args = e.Parameter as SocialComposeArgs;
            if (args != null) Show(args);
        }

        public void Reopen(object parameter)
        {
            var args = parameter as SocialComposeArgs;
            if (args == null) return;

            SaveDraft();
            Show(args);
        }

        public void Release()
        {
            if (_released) return;
            _released = true;

            try
            {
                if (!_finished) SaveDraft();
                Composer.ReleaseUploads();
                FlushDrafts();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposePage: could not save the draft on close - {ex.Message}");
            }
        }

        private static async void FlushDrafts()
        {
            try
            {
                await SocialDraftService.FlushAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposePage: could not write the drafts - {ex.Message}");
            }
        }

        public static string TitleFor(SocialComposeMode mode)
        {
            switch (mode)
            {
                case SocialComposeMode.Quote: return LocalizedStrings.Get("WindowTitleQuote");
                case SocialComposeMode.Edit: return LocalizedStrings.Get("WindowTitleEditNote");
                default: return LocalizedStrings.Get("WindowTitleNewNote");
            }
        }

        private async void Show(SocialComposeArgs args)
        {
            var version = ++_showVersion;
            try
            {
                _args = args;
                _finished = false;

                if (!SocialContentService.IsCurrentAccount(args.AccountId))
                {
                    WindowManagerService.CloseCurrentWindow();
                    return;
                }

                WindowManagerService.SetCurrentWindowTitle(TitleFor(args.Mode));

                var slot = await SlotForAsync(args);
                var draft = await DraftForAsync(args, slot);
                if (version != _showVersion || _released) return;

                _slot = slot;

                var note = args.Note;
                var quote = args.Mode == SocialComposeMode.Quote ? note
                            : note != null && note.Renote != null && !note.IsPureRenote ? note.Renote
                            : null;
                var editing = args.Mode == SocialComposeMode.Edit ? note : null;

                Composer.Load(draft, new SocialComposeContext(SocialComposerMode.Window, null, quote, editing));

                if ((args.Mode == SocialComposeMode.Edit || args.Mode == SocialComposeMode.Redraft)
                    && note != null && note.Visibility == SocialPostService.DirectVisibility && draft.Recipients.Count == 0)
                {
                    Composer.AddRecipients(note.VisibleUserIds);
                }

                Composer.FocusEditor();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposePage: could not open the composer - {ex.GetType().Name}: {ex.Message}");
            }
        }

        private static async Task<string> SlotForAsync(SocialComposeArgs args)
        {
            var noteId = args.Note == null ? "" : args.Note.Id;
            switch (args.Mode)
            {
                case SocialComposeMode.Quote:
                    return QuoteSlotPrefix + noteId;
                case SocialComposeMode.Edit:
                    return SocialDraftService.EditSlot(noteId);
                case SocialComposeMode.Redraft:
                    var existing = await SocialDraftService.LoadAsync(args.AccountId, SocialDraftService.NewSlot);
                    return existing == null || existing.IsEmpty ? SocialDraftService.NewSlot : RedraftSlotPrefix + noteId;
                default:
                    return SocialDraftService.NewSlot;
            }
        }

        private static async Task<SocialComposeDraft> DraftForAsync(SocialComposeArgs args, string slot)
        {
            var saved = await SocialDraftService.LoadAsync(args.AccountId, slot);

            switch (args.Mode)
            {
                case SocialComposeMode.Edit:
                    return saved ?? SocialComposeDraft.FromNote(args.Note, null, true);
                case SocialComposeMode.Redraft:
                    if (args.Note == null) return saved ?? SocialComposeDraft.Empty;
                    var redraft = SocialComposeDraft.FromNote(args.Note, null, false);
                    SocialDraftService.Save(args.AccountId, slot, redraft);
                    return redraft;
                case SocialComposeMode.Quote:
                    if (saved != null) return saved;
                    return SocialComposeDraft.Create("", null, LastVisibility(), LastLocalOnly(), null, null, null, null,
                                                     args.Note == null ? null : args.Note.Id, null, null);
                default:
                    return saved ?? SocialComposeDraft.Create("", null, LastVisibility(), LastLocalOnly(), null, null, null, null, null, null, null);
            }
        }

        internal static string LastVisibility()
        {
            try
            {
                var stored = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialLastVisibility] as string;
                switch (stored)
                {
                    case SocialNoteActionService.HomeVisibility:
                    case SocialNoteActionService.FollowersVisibility:
                    case SocialPostService.DirectVisibility:
                        return stored;
                    default:
                        return SocialNoteActionService.PublicVisibility;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposePage: could not read the last visibility - {ex.Message}");
                return SocialNoteActionService.PublicVisibility;
            }
        }

        internal static bool LastLocalOnly()
        {
            try
            {
                var stored = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialLastLocalOnly];
                return stored != null && Convert.ToBoolean(stored);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposePage: could not read the last federation choice - {ex.Message}");
                return false;
            }
        }

        internal static void RememberVisibility(SocialComposeDraft draft)
        {
            try
            {
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialLastVisibility] = draft.Visibility;
                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialLastLocalOnly] = draft.LocalOnly;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposePage: could not remember the visibility - {ex.Message}");
            }
        }

        private void SaveDraft()
        {
            if (_args == null || _slot == null || _finished) return;

            SocialDraftService.Save(_args.AccountId, _slot, Composer.GetDraft());
        }

        private void Composer_DraftChanged(object sender, EventArgs e)
        {
            try
            {
                SaveDraft();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposePage: could not save the draft - {ex.Message}");
            }
        }

        private void Composer_Posted(object sender, SocialNote note)
        {
            try
            {
                var draft = Composer.GetDraft();
                if (_args.Mode != SocialComposeMode.Edit) RememberVisibility(draft);

                Finish();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposePage: could not finish after posting - {ex.Message}");
            }
        }

        private void Composer_Discarded(object sender, EventArgs e)
        {
            Finish();
        }

        private void Finish()
        {
            _finished = true;
            if (_args != null && _slot != null) SocialDraftService.Remove(_args.AccountId, _slot);
            FlushDrafts();
            WindowManagerService.CloseCurrentWindow();
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            RememberSize(_sizeDebounce.Restart());
        }

        private async void RememberSize(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(AppConstants.SocialComposeSizeSaveDelayMilliseconds);
                if (cancellationToken.IsCancellationRequested || _released) return;

                var bounds = Window.Current.Bounds;
                if (bounds.Width < AppConstants.SocialComposeMinWidth || bounds.Height < AppConstants.SocialComposeMinHeight) return;

                ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialComposeWindowSize] =
                    string.Format(CultureInfo.InvariantCulture, "{0:0},{1:0}", bounds.Width, bounds.Height);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposePage: could not remember the window size - {ex.Message}");
            }
        }

        public static Size RememberedSize()
        {
            try
            {
                var stored = ApplicationData.Current.LocalSettings.Values[AppConstants.SettingSocialComposeWindowSize] as string;
                if (!string.IsNullOrEmpty(stored))
                {
                    var parts = stored.Split(',');
                    double width, height;
                    if (parts.Length == 2
                        && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out width)
                        && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out height)
                        && width >= AppConstants.SocialComposeMinWidth && height >= AppConstants.SocialComposeMinHeight)
                    {
                        return new Size(width, height);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SocialComposePage: could not read the window size - {ex.Message}");
            }

            return new Size(AppConstants.SocialComposeWindowWidth, AppConstants.SocialComposeWindowHeight);
        }
    }
}
