using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using static Readaloud_Epub3_Creator.Alingner;

namespace Readaloud_Epub3_Creator
{
    public partial class LogViewerWindow : Window
    {
        public LogViewerWindow(string logFilePath)
        {
            InitializeComponent();

            var rawLogs = LoadLogEntries(logFilePath);
            var filteredLogs = FilterLogs(rawLogs);

            DataContext = filteredLogs;
        }

        private List<LogEntry> LoadLogEntries(string path)
        {
            var json = File.ReadAllText(path);
            return JsonConvert.DeserializeObject<List<LogEntry>>(json) ?? new();
        }

        private List<LogEntry> FilterLogs(List<LogEntry> logs)
        {
            var importantIndexes = new HashSet<int>();
            int skippedCount = 0;

            for (int i = 0; i < logs.Count; i++)
            {
                var level = logs[i].Level;
                if (level == LogLevel.Red || level == LogLevel.Yellow)
                {
                    importantIndexes.Add(i);

                    if (i > 0 && logs[i - 1].Level == LogLevel.Green)
                        importantIndexes.Add(i - 1);
                    if (i < logs.Count - 1 && logs[i + 1].Level == LogLevel.Green)
                        importantIndexes.Add(i + 1);
                }
            }

            for (int i = 0; i < logs.Count; i++)
            {
                if (logs[i].Level == LogLevel.Green && !importantIndexes.Contains(i))
                    skippedCount++;
            }

            var finalLogs = logs
                .Where((_, index) => importantIndexes.Contains(index))
                .OrderBy(e => e.SegmentIndex)
                .ToList();

            if (skippedCount > 0)
            {
                finalLogs.Insert(0, new LogEntry
                {
                    Message = $"Hidden success logs: {skippedCount}",
                    IsSystemMessage = true
                });
            }

            return finalLogs;
        }


    }
}
