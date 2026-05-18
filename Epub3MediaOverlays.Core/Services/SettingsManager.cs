using EpubSharp;
using Microsoft.Extensions.Options;
using Epub3MediaOverlays.Core.AlingnerUtil;
using System.IO;
using System.Text.Json;

namespace Epub3MediaOverlays.Core
{
    [AttributeUsage(AttributeTargets.Property)]
    public class SettingDisplayAttribute : Attribute
    {
        public string Header { get; set; }
        public string Description { get; set; }
        public string Group { get; set; }
        public bool IsAdvanced { get; set; }
        public bool IsFolderPicker { get; set; }

        public SettingDisplayAttribute(string header, string desc = "", string group = "General", bool isAdvanced = false, bool isFolder = false)
        {
            Header = header;
            Description = desc;
            Group = isAdvanced ? "Advanced" : group; // Forces advanced settings to their own tab
            IsAdvanced = isAdvanced;
            IsFolderPicker = isFolder;
        }
    }
    public class AppSettings
    {
        [SettingDisplay("Ebooks Folder", "Where your EPUB files are stored", group: "Paths", isFolder: true)]
        public string EbooksPath { get; set; } = "";

        [SettingDisplay("Transcriber Path", "Path to the Python transcriber script", group: "Paths", isFolder: true)]
        public string TranscriberPath { get; set; } = "";

        [SettingDisplay("Compute Device", "Use 'cuda' for NVIDIA GPUs or 'cpu' for processors", group: "Hardware")]
        public string Device { get; set; } = "cuda";

        // This will automatically show up in the "Advanced" tab
        [SettingDisplay("Max Concurrent", "Number of simultaneous tasks (CPU only)", isAdvanced: true)]
        public int MaxConcurrentTranscriptions { get; set; } = 1;

        // Aligner Configuration - stored as a nested object
        public AlingnerConfiguration AlingnerConfig { get; set; } = new AlingnerConfiguration();
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









