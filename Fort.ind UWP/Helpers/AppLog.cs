using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Storage;
using Windows.System.Profile;

namespace Fort.ind_UWP
{
    public static class AppLog
    {
        private const string FolderName = "logs";

        private const string FileName = "app.log";

        private const string PreviousFileName = "app.previous.log";

        private const long MaxFileBytes = 256 * 1024;

        private const int MaxPendingChars = 64 * 1024;

        private static readonly object s_pendingGate = new object();

        private static readonly object s_fileGate = new object();

        private static readonly StringBuilder s_pending = new StringBuilder();

        private static bool s_flushScheduled;

        private static int s_dropped;

        private static string s_folder;

        public static void Error(string message, Exception ex)
        {
            Debug.WriteLine(ex == null ? message : message + " - " + Describe(ex));
            Queue("ERROR", message, ex == null ? null : ex.ToString());
        }

        public static void Warning(string message)
        {
            Debug.WriteLine(message);
            Queue("WARN", message, null);
        }

        public static void Crash(string message, Exception ex)
        {
            Debug.WriteLine(ex == null ? message : message + " - " + Describe(ex));
            Queue("CRASH", message, ex == null ? null : ex.ToString());
            Flush();
        }

        public static string Describe(Exception ex)
        {
            var text = new StringBuilder();
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (text.Length > 0) text.Append(" ---> ");

                text.Append(current.GetType().Name)
                    .Append(" (0x")
                    .Append(current.HResult.ToString("X8", CultureInfo.InvariantCulture))
                    .Append("): ")
                    .Append(current.Message);
            }

            return text.ToString();
        }

        public static Task<string> ReadDiagnosticsAsync()
        {
            return Task.Run(() =>
            {
                Flush();

                var text = new StringBuilder(Header());
                lock (s_fileGate)
                {
                    var before = text.Length;
                    AppendFile(text, PreviousFileName);
                    AppendFile(text, FileName);
                    if (text.Length == before) text.Append("No errors have been recorded.\r\n");
                }

                return text.ToString();
            });
        }

        public static void Flush()
        {
            lock (s_fileGate)
            {
                string text;
                int dropped;
                lock (s_pendingGate)
                {
                    text = s_pending.ToString();
                    s_pending.Clear();
                    dropped = s_dropped;
                    s_dropped = 0;
                    s_flushScheduled = false;
                }

                if (dropped > 0)
                {
                    text += string.Format(CultureInfo.InvariantCulture, "{0} WARN {1} more entries were dropped while the log was busy\r\n",
                                          Timestamp(), dropped);
                }

                if (text.Length == 0) return;

                try
                {
                    var folder = Folder();
                    if (folder == null) return;

                    Directory.CreateDirectory(folder);
                    var path = Path.Combine(folder, FileName);
                    var current = new FileInfo(path);
                    if (current.Exists && current.Length > MaxFileBytes)
                    {
                        var previous = Path.Combine(folder, PreviousFileName);
                        if (File.Exists(previous)) File.Delete(previous);
                        File.Move(path, previous);
                    }

                    File.AppendAllText(path, text, Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("AppLog: could not write the log - " + Describe(ex));
                }
            }
        }

        private static void Queue(string level, string message, string detail)
        {
            var entry = new StringBuilder();
            entry.Append(Timestamp()).Append(' ').Append(level).Append(' ').Append(message).Append("\r\n");
            if (!string.IsNullOrEmpty(detail))
            {
                entry.Append("    ").Append(detail.Replace("\r\n", "\n").Replace("\n", "\r\n    ")).Append("\r\n");
            }

            bool schedule;
            lock (s_pendingGate)
            {
                if (s_pending.Length + entry.Length > MaxPendingChars)
                {
                    s_dropped++;
                    return;
                }

                s_pending.Append(entry);
                schedule = !s_flushScheduled;
                s_flushScheduled = true;
            }

            if (schedule) _ = Task.Run(() => Flush());
        }

        private static string Timestamp()
        {
            return DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture);
        }

        private static string Folder()
        {
            if (s_folder != null) return s_folder;

            try
            {
                s_folder = Path.Combine(ApplicationData.Current.LocalFolder.Path, FolderName);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("AppLog: no local folder - " + Describe(ex));
            }

            return s_folder;
        }

        private static void AppendFile(StringBuilder text, string name)
        {
            try
            {
                var folder = Folder();
                if (folder == null) return;

                var path = Path.Combine(folder, name);
                if (File.Exists(path)) text.Append(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (Exception ex)
            {
                text.Append("Could not read ").Append(name).Append(": ").Append(Describe(ex)).Append("\r\n");
            }
        }

        private static string Header()
        {
            var text = new StringBuilder();
            text.Append("fort.desktop ").Append(AppConstants.AppVersionDisplay).Append("\r\n");

            try
            {
                var version = ulong.Parse(AnalyticsInfo.VersionInfo.DeviceFamilyVersion, CultureInfo.InvariantCulture);
                text.Append(AnalyticsInfo.VersionInfo.DeviceFamily)
                    .Append(' ')
                    .Append((version >> 48) & 0xFFFF).Append('.')
                    .Append((version >> 32) & 0xFFFF).Append('.')
                    .Append((version >> 16) & 0xFFFF).Append('.')
                    .Append(version & 0xFFFF)
                    .Append(", ")
                    .Append(Package.Current.Id.Architecture)
                    .Append("\r\n");
            }
            catch (Exception ex)
            {
                text.Append("Windows version unavailable: ").Append(Describe(ex)).Append("\r\n");
            }

            text.Append("Copied ").Append(Timestamp()).Append("\r\n\r\n");
            return text.ToString();
        }
    }
}
