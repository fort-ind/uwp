using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.UI.Input.Inking;
using Windows.UI.Input.Inking.Analysis;

namespace Fort.ind_UWP
{
    public sealed class InkRecognitionWord
    {
        public InkRecognitionWord(string text, IReadOnlyList<string> alternates)
        {
            Text = text;
            Alternates = alternates;
        }

        public string Text { get; private set; }

        public IReadOnlyList<string> Alternates { get; private set; }
    }

    public static class InkRecognition
    {
        public static async Task<IReadOnlyList<InkRecognitionWord>> RecognizeAsync(IReadOnlyList<InkStroke> strokes)
        {
            var words = new List<InkRecognitionWord>();
            if (strokes == null || strokes.Count == 0) return words;

            var analyzer = new InkAnalyzer();
            try
            {
                analyzer.AddDataForStrokes(strokes);
                foreach (var stroke in strokes)
                {
                    analyzer.SetStrokeDataKind(stroke.Id, InkAnalysisStrokeKind.Writing);
                }

                var result = await analyzer.AnalyzeAsync();
                if (result.Status != InkAnalysisStatus.Updated) return words;

                foreach (var line in analyzer.AnalysisRoot.FindNodes(InkAnalysisNodeKind.Line).OfType<InkAnalysisLine>())
                {
                    foreach (var word in line.Children.OfType<InkAnalysisInkWord>())
                    {
                        var text = (word.RecognizedText ?? "").Trim();
                        if (text.Length == 0) continue;

                        var alternates = word.TextAlternates
                                             .Select(alternate => (alternate ?? "").Trim())
                                             .Where(alternate => alternate.Length > 0 && !string.Equals(alternate, text, StringComparison.Ordinal))
                                             .Distinct(StringComparer.Ordinal)
                                             .ToList();
                        words.Add(new InkRecognitionWord(text, alternates));
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Error("InkRecognition: analysis failed", ex);
            }
            finally
            {
                analyzer.ClearDataForAllStrokes();
            }

            return words;
        }

        public static string Join(IReadOnlyList<InkRecognitionWord> words)
        {
            var builder = new StringBuilder();
            foreach (var word in words)
            {
                if (builder.Length > 0 && !StartsWithClosingPunctuation(word.Text)) builder.Append(' ');
                builder.Append(word.Text);
            }

            return builder.ToString();
        }

        public static bool StartsWithClosingPunctuation(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;

            switch (text[0])
            {
                case '.':
                case ',':
                case '!':
                case '?':
                case ';':
                case ':':
                case ')':
                case '\'':
                    return true;
                default:
                    return false;
            }
        }
    }
}
