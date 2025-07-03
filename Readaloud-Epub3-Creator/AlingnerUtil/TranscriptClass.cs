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
            string pythonExe = Path.Combine(venvPath, "Scripts", "python.exe"); // Windows venv

            // If the venv doesn't exist, try to create it in the parent directory
            if (!File.Exists(pythonExe))
            {
                Console.WriteLine("Virtual environment not found. Attempting to create...");

                string parentDir = Path.GetFullPath(Path.Combine(venvPath, ".."));
                string newVenvPath = Path.Combine(parentDir, "venv");
                string newPythonExe = Path.Combine(newVenvPath, "Scripts", "python.exe");

                var createVenv = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "python", // system python
                        Arguments = $"-m venv \"{newVenvPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                createVenv.Start();
                createVenv.WaitForExit();

                if (!File.Exists(newPythonExe))
                    throw new Exception("Failed to create virtual environment.");

                venvPath = newVenvPath;
                pythonExe = newPythonExe;
            }

            // Proceed to run the Python script
            string quotedMp3s = string.Join(" ", mp3Files.Select(f => $"\"{f}\""));
            string args = $"\"{scriptPath}\" {quotedMp3s} --device {device} --output \"{outputPath}\" --workers {workers}";

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





    }
}
