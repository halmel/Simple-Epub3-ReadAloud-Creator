using System.Diagnostics;
using System.IO;

namespace Readaloud_Epub3_Creator
{
    public class TranscriptClass
    {
        // Root myDeserializedClass = JsonConvert.DeserializeObject<List<Root>>(myJsonResponse);

        public class Root
        {
            public string file { get; set; }
            public string language { get; set; }
            public string text { get; set; }

            public double length { get; set; }
            public List<Segment> segments { get; set; }
        }

        public class Segment
        {
            public int id { get; set; }
            public double start { get; set; }
            public double end { get; set; }
            public string text { get; set; }

            public double fileLength { get; set; }
            // New property to track the originating file
            public string fileId { get; set; }

            public int IndexInList { get; set; }
            public static void AssignListIndices(List<Segment> words)
            {
                for (int i = 0; i < words.Count; i++)
                {
                    words[i].IndexInList = i;
                }
            }

        }


        // Function to extract all segments and add fileId to each
        public static List<Segment> ExtractSegmentsWithFileId(List<Root> roots)
        {
            var result = new List<Segment>();
            foreach (var root in roots)
            {
                if (root.segments != null)
                {
                    foreach (var segment in root.segments)
                    {
                        segment.fileId = root.file;
                        segment.fileLength = root.length;
                        result.Add(segment);
                    }
                }
            }
            return result;
        }
        public static string RunTranscription(
            string venvPath,
            string scriptPath,
            string[] mp3Files,
            string device,
            string outputPath,
            int workers = 2,
            Action<int>? onProgress = null
        )
        {
            string pythonExe = Path.Combine(venvPath, "Scripts", "python.exe");

            if (!File.Exists(pythonExe))
                throw new FileNotFoundException("Python executable not found at: " + pythonExe);

            // ✅ Write MP3 file list to temp file
            string tempListPath = Path.Combine(Path.GetTempPath(), "mp3_list.txt");
            File.WriteAllLines(tempListPath, mp3Files);

            // Now your Python script should accept a list file input
            string args = $"\"{scriptPath}\" --file-list \"{tempListPath}\" --device {device} --output \"{outputPath}\" --batch-size {workers}";

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            var output = new List<string>();

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    if (e.Data.StartsWith("PROGRESS:") && int.TryParse(e.Data.Replace("PROGRESS:", ""), out int percent))
                    {
                        onProgress?.Invoke(percent);
                    }
                    else
                    {
                        output.Add(e.Data);
                    }
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    output.Add("ERR: " + e.Data);
                }
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            return string.Join(Environment.NewLine, output);
        }
// Experiment uneused
        /// <summary>
        /// Runs the Aeneas-based Python transcription alignment script on one audio/text pair using system Python.
        /// </summary>
        /// <param name="scriptPath">Path to the aeneas_transcribe.py script.</param>
        /// <param name="audioFile">Path to the audiobook audio file (e.g., MP3 or WAV).</param>
        /// <param name="textFile">Path to the text file containing the ebook content.</param>
        /// <param name="outputPath">Path where the alignment JSON will be written.</param>
        /// <param name="onProgress">Optional progress callback (0–100).</param>
        /// <returns>Combined console output (stdout + stderr) as a single string.</returns>
        public static string RunAeneasAlignment(
            string scriptPath,
            string audioFile,
            string textFile,
            string outputPath,
            Action<int>? onProgress = null
        )
        {
            // Use system Python
            string pythonExe = "python"; // Assumes system Python is in PATH

            if (!File.Exists(scriptPath))
                throw new FileNotFoundException("Aeneas script not found: " + scriptPath);

            if (!File.Exists(audioFile))
                throw new FileNotFoundException("Audio file not found: " + audioFile);

            if (!File.Exists(textFile))
                throw new FileNotFoundException("Text file not found: " + textFile);

            string args = $"\"{scriptPath}\" --audio \"{audioFile}\" --text \"{textFile}\" --output \"{outputPath}\"";

            var outputLog = new List<string>();

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    // Capture Python progress updates
                    if (e.Data.StartsWith("PROGRESS:") && int.TryParse(e.Data.Replace("PROGRESS:", ""), out int percent))
                        onProgress?.Invoke(percent);
                    else
                        outputLog.Add(e.Data);
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                    outputLog.Add("ERR: " + e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            onProgress?.Invoke(100);
            return string.Join(Environment.NewLine, outputLog);
        }





    }
}
