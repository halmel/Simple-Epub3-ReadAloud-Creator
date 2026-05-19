using System.Text.Json.Serialization;
using Epub3MediaOverlays.Core.MediaOverlayGeneration.Models;

namespace Epub3MediaOverlays.Core.MediaOverlayGeneration.Internal
{
    /// <summary>
    /// Internal class representing a transcription root from the speech-to-text output.
    /// Contains metadata about an audio file and all fragments extracted from it.
    /// </summary>
    internal class TranscriptionRoot
    {
        [JsonPropertyName("file")]
        public string File { get; set; } = string.Empty;

        [JsonPropertyName("language")]
        public string Language { get; set; } = string.Empty;

        [JsonPropertyName("language_probability")]
        public double LanguageProbability { get; set; }

        [JsonPropertyName("full_text")]
        public string FullText { get; set; } = string.Empty;

        [JsonPropertyName("length")]
        public double Length { get; set; }

        [JsonPropertyName("fragments")]
        public List<AudioFragment> Fragments { get; set; } = new();

        /// <summary>
        /// Populates metadata across all fragments after deserialization.
        /// </summary>
        public void LinkSegments()
        {
            for (int i = 0; i < Fragments.Count; i++)
            {
                Fragments[i].FileId = this.File;
                Fragments[i].FileLength = this.Length;
                Fragments[i].IndexInList = i;
            }
        }
    }
}
