using System;
using System.Collections.Generic;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Automation;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Documents;

namespace Fort.ind_UWP
{
    public sealed partial class SocialCodeBlock : UserControl
    {
        private const string CopyGlyphText = "";

        private const string CopiedGlyphText = "";

        private static readonly TimeSpan CopiedDuration = TimeSpan.FromMilliseconds(1500);

        private readonly List<TokenRun> _tokenRuns = new List<TokenRun>();

        private string _code = "";

        private string _copyLabel;

        private DispatcherTimer _copiedTimer;

        public SocialCodeBlock()
        {
            this.InitializeComponent();
            Unloaded += OnUnloaded;
        }

        public void Load(MfmSegment segment, bool dark)
        {
            _code = segment.Text ?? "";

            var label = segment.Language ?? "";
            LanguageText.Text = label;
            LanguageText.Visibility = label.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            AutomationProperties.SetName(this, label.Length > 0
                                               ? LocalizedStrings.Format("SocialCodeBlockNameFormat", label)
                                               : LocalizedStrings.Get("SocialCodeBlockName"));

            CodeText.Inlines.Clear();
            _tokenRuns.Clear();

            var text = segment.DisplayText ?? _code;
            var position = 0;
            if (segment.CodeTokens != null)
            {
                foreach (var token in segment.CodeTokens)
                {
                    AppendSpan(text, position, token.Start - position, CodeTokenKind.Plain, dark);
                    AppendSpan(text, token.Start, token.Length, token.Kind, dark);
                    position = token.Start + token.Length;
                }
            }
            AppendSpan(text, position, text.Length - position, CodeTokenKind.Plain, dark);
        }

        public void Recolor(bool dark)
        {
            foreach (var item in _tokenRuns)
            {
                CodePalette.Paint(item.Run, CodePalette.For(item.Kind, dark));
            }
        }

        private void AppendSpan(string text, int start, int length, CodeTokenKind kind, bool dark)
        {
            var cursor = Math.Max(0, start);
            var end = Math.Min(text.Length, start + length);
            while (cursor < end)
            {
                var lineEnd = text.IndexOf('\n', cursor, end - cursor);
                var stop = lineEnd < 0 ? end : lineEnd;
                if (stop > cursor) AppendRun(text.Substring(cursor, stop - cursor), kind, dark);
                if (lineEnd < 0) break;

                CodeText.Inlines.Add(new LineBreak());
                cursor = lineEnd + 1;
            }
        }

        private void AppendRun(string piece, CodeTokenKind kind, bool dark)
        {
            var run = new Run { Text = piece };
            if (kind != CodeTokenKind.Plain)
            {
                var brush = CodePalette.For(kind, dark);
                if (brush != null) run.Foreground = brush;
                _tokenRuns.Add(new TokenRun(run, kind));
            }
            CodeText.Inlines.Add(run);
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var package = new DataPackage();
                package.SetText(_code);
                Clipboard.SetContent(package);
                ShowCopied();
            }
            catch (Exception ex)
            {
                AppLog.Error("SocialCodeBlock: copying the code failed", ex);
            }
        }

        private void ShowCopied()
        {
            if (_copyLabel == null) _copyLabel = AutomationProperties.GetName(CopyButton);

            var copied = LocalizedStrings.Get("SocialCodeCopied");
            CopyGlyph.Glyph = CopiedGlyphText;
            AutomationProperties.SetName(CopyButton, copied);
            ToolTipService.SetToolTip(CopyButton, copied);
            AutomationHelper.AnnounceStatus(CopyButton, copied, "SocialCodeCopied");

            if (_copiedTimer == null)
            {
                _copiedTimer = new DispatcherTimer { Interval = CopiedDuration };
                _copiedTimer.Tick += OnCopiedTimerTick;
            }
            _copiedTimer.Stop();
            _copiedTimer.Start();
        }

        private void OnCopiedTimerTick(object sender, object e)
        {
            _copiedTimer.Stop();
            RestoreCopyButton();
        }

        private void RestoreCopyButton()
        {
            CopyGlyph.Glyph = CopyGlyphText;
            if (_copyLabel == null) return;

            AutomationProperties.SetName(CopyButton, _copyLabel);
            ToolTipService.SetToolTip(CopyButton, _copyLabel);
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_copiedTimer == null) return;

            _copiedTimer.Stop();
            RestoreCopyButton();
        }

        private sealed class TokenRun
        {
            public TokenRun(Run run, CodeTokenKind kind)
            {
                Run = run;
                Kind = kind;
            }

            public Run Run { get; }

            public CodeTokenKind Kind { get; }
        }
    }
}
