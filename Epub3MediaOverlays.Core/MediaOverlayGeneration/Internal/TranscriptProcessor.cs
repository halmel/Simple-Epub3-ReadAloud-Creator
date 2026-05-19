using System.Diagnostics;
using System.IO;
using Epub3MediaOverlays.Core.MediaOverlayGeneration.Models;

namespace Epub3MediaOverlays.Core.MediaOverlayGeneration.Internal
{
    /// <summary>
    /// Internal processor for speech-to-text transcriptions.
    /// Handles transcript deserialization and audio fragment extraction.
    /// </summary>
    internal static class TranscriptProcessor
    {
        /// <summary>
        /// Extracts all audio fragments from transcription roots and tags them with metadata.
        /// </summary>
        public static List<AudioFragment> ExtractSegmentsWithFileId(List<TranscriptionRoot> roots)
        {
            var result = new List<AudioFragment>();
            foreach (var root in roots)
            {
                if (root.Fragments != null)
                {
                    foreach (var segment in root.Fragments)
                    {
                        segment.FileId = root.File;
                        segment.FileLength = root.Length;
                        result.Add(segment);
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Runs the Python transcription script to generate speech-to-text output.
        /// </summary>
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

            DataReceivedEventHandler handler = (sender, e) =>
            {
                if (string.IsNullOrEmpty(e.Data)) return;

                onLiveOutput?.Invoke(e.Data);

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

    /// <summary>
    /// Interface for transcription scripts (CUDA, CPU, etc.).
    /// </summary>
    public interface ITranscriptionScript
    {
        string ScriptName { get; }
        string[] Mp3Files { get; set; }
        string OutputPath { get; set; }
        string GetArguments();
        string TranscriptPath { get; set; }
    }

    /// <summary>
    /// Supported Faster-Whisper model sizes.
    /// </summary>
    public enum FasterWhisperModel
    {
        Tiny,
        Base,
        Small,
        Medium,
        Large
    }

    /// <summary>
    /// CUDA-accelerated Faster-Whisper transcription script implementation.
    /// </summary>
    public class CUDAFasterWhisperScript : ITranscriptionScript
    {
        public string ScriptName => @"faster_whisper_CUDA.py";
        public string TranscriptPath { get; set; }
        public string[] Mp3Files { get; set; }
        public string OutputPath { get; set; }
        public FasterWhisperModel Model { get; set; }

        public CUDAFasterWhisperScript(string transcriptPath, FasterWhisperModel model = FasterWhisperModel.Tiny)
        {
            TranscriptPath = transcriptPath;
            Model = model;
        }

        public string GetArguments()
        {
            if (Mp3Files == null)
                throw new Exception("Mp3Files cannot be null.");
            if (OutputPath == null)
                throw new Exception("OutputPath cannot be null.");

            string tempListPath = Path.Combine(Path.GetTempPath(), "mp3_list.txt");
            File.WriteAllLines(tempListPath, Mp3Files);

            return $"\"{Path.Combine(TranscriptPath, ScriptName)}\" --file-list \"{tempListPath}\" --output \"{OutputPath}\" --model \"{Model.ToString().ToLower()}\"";
        }
    }

    /// <summary>
    /// CPU-based Faster-Whisper transcription script implementation.
    /// </summary>
    public class CPUFasterWhisperScript : ITranscriptionScript
    {
        public string ScriptName => @"faster_whisper_CPU.py";
        public string TranscriptPath { get; set; }
        public string[] Mp3Files { get; set; }
        public string OutputPath { get; set; }
        public int Workers { get; set; } = 2;
        public int BatchSize { get; set; }
        public FasterWhisperModel Model { get; set; }

        public CPUFasterWhisperScript(string transcriptPath, int workers = 2, FasterWhisperModel model = FasterWhisperModel.Tiny, int batchSize = 8)
        {
            TranscriptPath = transcriptPath;
            Workers = workers;
            Model = model;
            BatchSize = batchSize;
        }

        public string GetArguments()
        {
            if (Mp3Files == null)
                throw new Exception("Mp3Files cannot be null.");
            if (OutputPath == null)
                throw new Exception("OutputPath cannot be null.");

            string tempListPath = Path.Combine(Path.GetTempPath(), "mp3_list.txt");
            File.WriteAllLines(tempListPath, Mp3Files);

            return $"\"{Path.Combine(TranscriptPath, ScriptName)}\" --file-list \"{tempListPath}\" --output \"{OutputPath}\" --workers {Workers} --model \"{Model.ToString().ToLower()}\" --batch-size \"{BatchSize}\"";
        }
    }
}
