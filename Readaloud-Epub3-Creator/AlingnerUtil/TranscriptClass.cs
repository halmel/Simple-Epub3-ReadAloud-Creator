using System.Diagnostics;
using System.IO;
using System.Text.Json.Serialization;
using static Readaloud_Epub3_Creator.AlingnerUtil.AlingnerNew;
using static Readaloud_Epub3_Creator.EpubUtility;

namespace Readaloud_Epub3_Creator
{
    public class TranscriptClass
    {
        // Root myDeserializedClass = JsonConvert.DeserializeObject<List<Root>>(myJsonResponse);

        public class Root
        {
            [JsonPropertyName("file")]
            public string File { get; set; } = string.Empty;

            [JsonPropertyName("language")]
            public string Language { get; set; } = string.Empty;

            [JsonPropertyName("language_probability")]
            public double LanguageProbability { get; set; }

            [JsonPropertyName("full_text")] // Matches the new Python key
            public string FullText { get; set; } = string.Empty;

            [JsonPropertyName("length")]
            public double Length { get; set; }

            [JsonPropertyName("fragments")] // Matches the new Python key
            public List<Fragment> fragments { get; set; } = new();

            /// <summary>
            /// Helper to populate metadata across all fragments after deserialization
            /// </summary>
            public void LinkSegments()
            {
                for (int i = 0; i < fragments.Count; i++)
                {
                    fragments[i].FileId = this.File;
                    fragments[i].FileLength = this.Length;
                    fragments[i].IndexInList = i;
                }
            }
        }

        public class Fragment
        {
            [JsonPropertyName("start")]
            public double Start { get; set; }

            [JsonPropertyName("end")]
            public double End { get; set; }

            [JsonPropertyName("text")]
            public string Text { get; set; } = string.Empty;

            // --- Local helper properties (Not in JSON) ---

            [JsonIgnore] // Prevents errors if you ever serialize this back to JSON
            public double FileLength { get; set; }

            [JsonIgnore]
            public string FileId { get; set; } = string.Empty;

            [JsonIgnore]
            public int IndexInList { get; set; }

            [JsonIgnore]
            public string NormalizedText => NormalizeText(Text);

            [JsonIgnore]

            public int NormalizedLength => NormalizedText.Length;

            [JsonIgnore]
            public int NormArrayIndex { get; set; }
            [JsonIgnore]
            public int NormIndexIndexEnd { get { return NormArrayIndex + NormalizedLength; } }


            public static void AssignListIndices(List<Fragment> words)
            {
                int normIndex = 0;
                for (int i = 0; i < words.Count; i++)
                {
                    words[i].IndexInList = i;
                    words[i].NormArrayIndex = normIndex;
                    normIndex += words[i].NormalizedLength;
                }
            }
        }

        // Function to extract all segments and add fileId to each
        public static List<Fragment> ExtractSegmentsWithFileId(List<Root> roots)
        {
            var result = new List<Fragment>();
            foreach (var root in roots)
            {
                if (root.fragments != null)
                {
                    foreach (var segment in root.fragments)
                    {
                        segment.FileId = root.File;
                        segment.FileLength = root.Length;
                        result.Add(segment);
                    }
                }
            }
            return result;
        }

        public interface ITranscriptionScript
        {
            string ScriptName { get; }

            public string[]? Mp3Files { get; set; }
            public string? OutputPath { get; set; }

            string GetArguments();

            string TranscriptPath { get; set; }
        }
        
        public enum FasterWhisperModel
        {
            Tiny,
            Base,
            Small,
            Medium,
            Large
        }
        public class CUDAFasterWhisperScript : ITranscriptionScript
        {
            public string ScriptName => @"faster_whisper_CUDA.py";
            public string TranscriptPath { get; set; }
            public string[]? Mp3Files { get; set; }
            public string? OutputPath { get; set; }

            public CUDAFasterWhisperScript(string transcriptPath, FasterWhisperModel model =FasterWhisperModel.Tiny)
            {
                TranscriptPath = transcriptPath;
                Model = model;

            }
            // Customizable properties

            public FasterWhisperModel Model { get; set; }

            public string GetArguments()
            {
                if (Mp3Files == null)
                {
                    throw new Exception("Mp3Files cannot be null.");
                }
                if (OutputPath == null)
                {
                    throw new Exception("OutputPath cannot be null.");
                }
                string tempListPath = Path.Combine(Path.GetTempPath(), "mp3_list.txt");
                File.WriteAllLines(tempListPath, Mp3Files);

                return $"\"{Path.Combine(TranscriptPath, ScriptName)}\" --file-list \"{tempListPath}\" --output \"{OutputPath}\" --model \"{Model.ToString().ToLower()}\"";
            }
        }
        public class CPUFasterWhisperScript : ITranscriptionScript
        {
            public string ScriptName => @"faster_whisper_CPU.py";
            public string TranscriptPath { get; set; }

            public string[]? Mp3Files { get; set; }
            public string? OutputPath { get; set; }


            // Customizable properties
            public int Workers { get; set; } = 2;

            public int BatchSize { get; set; }
            public FasterWhisperModel Model { get; set; }


            public CPUFasterWhisperScript(string transcriptPath, int Workers =2, FasterWhisperModel model = FasterWhisperModel.Tiny, int bachSize = 8)
            {
                TranscriptPath = transcriptPath;
                this.Workers = Workers;
                Model = model;
                BatchSize = bachSize;

            }
            public string GetArguments()
            {
                if (Mp3Files == null)
                {
                    throw new Exception("Mp3Files cannot be null.");
                }
                if (OutputPath == null) { 
                throw new Exception("OutputPath cannot be null.");
                }
                string tempListPath = Path.Combine(Path.GetTempPath(), "mp3_list.txt");
                File.WriteAllLines(tempListPath, Mp3Files);

                return $"\"{Path.Combine(TranscriptPath,ScriptName)}\" --file-list \"{tempListPath}\" --output \"{OutputPath}\" --workers {Workers} --model \"{Model.ToString().ToLower()}\" --bach-size \"{BatchSize}\"";
            }
        }
        public static string RunTranscription(
            string venvPath,
            ITranscriptionScript script,
            Action<string>? onLiveOutput = null)
        {
            string pythonExe = Path.Combine(venvPath, "Scripts", "python.exe");

            if (!File.Exists(pythonExe))
                throw new FileNotFoundException("Python executable not found at: " + pythonExe);

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = script.GetArguments(),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            var fullOutput = new List<string>();

            // Shared handler for both StdOut and StdErr
            DataReceivedEventHandler handler = (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;

                // 2. Pass back live console output
                onLiveOutput?.Invoke(e.Data);

                // 3. Keep track of full log for the return string
                lock (fullOutput) { fullOutput.Add(e.Data); }
            };

            process.OutputDataReceived += handler;
            process.ErrorDataReceived += handler;

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.WaitForExit();

            return string.Join(Environment.NewLine, fullOutput);
        }
    }
}
