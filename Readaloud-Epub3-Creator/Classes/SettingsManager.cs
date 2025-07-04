using EpubSharp;
using Microsoft.Extensions.Options;
using System.IO;
using System.Text.Json;

namespace Readaloud_Epub3_Creator
{
    public class AppSettings
    {
        // Base path where the application is running from (e.g., bin\Debug\netX.X)
        private readonly string _baseDir = AppDomain.CurrentDomain.BaseDirectory;

        // Default: Path to the Ebooks folder (relative to the solution root)
        // Can be overridden by the user at runtime (via config, UI, etc.)
        private string _ebooksPath;
        public string EbooksPath
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_ebooksPath))
                    return _ebooksPath;

                var exeDir = AppDomain.CurrentDomain.BaseDirectory;
                var ebooksPath = Path.GetFullPath(Path.Combine(exeDir, @"..\..\Ebooks"));

                if (!Directory.Exists(ebooksPath))
                    Directory.CreateDirectory(ebooksPath);

                return ebooksPath;
            }

            set => _ebooksPath = value;
        }


        // Default: Path to the Python transcriber script (relative to the solution root)
        // Can also be overridden by user input
        private string _transcriberPath;
        public string TranscriberPath
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(_transcriberPath))
                    return _transcriberPath;

                // Get the folder where the app executable is running
                var exeDir = AppDomain.CurrentDomain.BaseDirectory;

                // Try path relative to exeDir (LatestRelease sibling)
                var candidatePath = Path.GetFullPath(Path.Combine(exeDir, @"..\..\Transcriber\transcriber.py"));
                if (File.Exists(candidatePath))
                    return candidatePath;

                // Try path relative to Debug/Release build output (YourProjectFolder\bin\Debug\)
                // exeDir would be: ...\YourProjectFolder\bin\Debug\netX\
                // Go up 4 levels to MyAppFolder, then Transcriber
                candidatePath = Path.GetFullPath(Path.Combine(exeDir, @"..\..\..\..\..\Transcriber\transcriber.py"));
                if (File.Exists(candidatePath))
                    return candidatePath;

                // Fallback to the LatestRelease sibling Transcriber folder by default
                return Path.GetFullPath(Path.Combine(exeDir, @"..\Transcriber\transcriber.py"));
            }
            set => _transcriberPath = value;
        }




        public string Device { get; set; } = "cuda"; // Options: "cuda", "cpu"

        public int MaxConcurrentTranscriptions { get; set; } = 1; // Applies only when Device = "cpu"

        public bool ShowDebugOptions { get; set; } = false;

    }


    public class JsonSettingsProvider
    {
        private readonly string _settingsFile;
        private AppSettings _settings;

        public JsonSettingsProvider(IOptions<AppSettings> options)
        {
            _settingsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

            // Load from file instead of using the injected options directly
            _settings = LoadFromFile(_settingsFile);
        }


        public AppSettings Settings => _settings;

        public void Save()
        {
            var json = JsonSerializer.Serialize(_settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsFile, json);
        }

        public static AppSettings LoadFromFile(string? filePath = null)
        {
            filePath ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

            if (File.Exists(filePath))
            {
                var json = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }

            return new AppSettings(); // return default settings
        }

        // ✅ Get the settings file path (for deletion or inspection)
        public string GetSettingsFilePath()
        {
            return _settingsFile;
        }

        // ✅ Delete the settings file
        public void DeleteSettingsFile()
        {
            if (File.Exists(_settingsFile))
            {
                File.Delete(_settingsFile);
            }

            _settings = new AppSettings(); // reset in-memory settings
        }

        // ✅ Reload the settings from the file, or fall back to defaults
        public void Reload()
        {
            _settings = LoadFromFile(_settingsFile);
        }
    }


    public class EpubCoverExtractor
    {
        // Extracts the cover image from the EPUB and saves a copy next to the EPUB file.
        // Returns the full path to the saved cover image or null if no cover found.
        public static string? ExtractCoverImage(string epubFilePath)
        {
            try
            {
                EpubBook epub = EpubReader.Read(epubFilePath);

                // Cover image is usually at epub.CoverImage (byte[]), or find from Resources
                var coverImage = epub.CoverImage;

                if (coverImage == null || coverImage.Length == 0)
                    return null;

                // Determine image extension - assuming JPEG or PNG mostly
                // EPUB spec often uses JPEG for cover images, but check MIME type if available
                string ext = ".jpg"; // default fallback

                // Optionally detect format from first bytes (JPEG or PNG):
                if (coverImage.Length > 8)
                {
                    // JPEG header: FF D8 FF
                    if (coverImage[0] == 0xFF && coverImage[1] == 0xD8 && coverImage[2] == 0xFF)
                        ext = ".jpg";
                    // PNG header: 89 50 4E 47 0D 0A 1A 0A
                    else if (coverImage[0] == 0x89 && coverImage[1] == 0x50 && coverImage[2] == 0x4E)
                        ext = ".png";
                }

                // Create cover image file path next to EPUB
                string coverFileName = Path.GetFileNameWithoutExtension(epubFilePath) + "_cover" + ext;
                string coverPath = Path.Combine(Path.GetDirectoryName(epubFilePath)!, coverFileName);

                File.WriteAllBytes(coverPath, coverImage);

                return coverPath;
            }
            catch
            {
                return null;
            }
        }
    }

}








