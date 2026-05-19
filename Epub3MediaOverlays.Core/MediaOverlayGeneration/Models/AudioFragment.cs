using System.Text.Json.Serialization;
using Epub3MediaOverlays.Core.MediaOverlayGeneration.Internal;

namespace Epub3MediaOverlays.Core.MediaOverlayGeneration.Models
{
    /// <summary>
    /// Represents a time-synchronized audio fragment extracted from a transcription.
    /// Each fragment corresponds to a spoken word or phrase in the audio files.
    /// </summary>
    public class AudioFragment
    {
        [JsonPropertyName("start")]
        public double Start { get; set; }

        private double _end;
        [JsonPropertyName("end")]
        public double End
        {
            get
            {
                if (_end < Start)
                    Console.WriteLine("Test");
                return _end;
            }
            set
            {
                if (value < Start)
                    Console.WriteLine("Test");
                _end = value;
            }
        }

        [JsonPropertyName("text")]
        public string Text { get; set; } = string.Empty;

        [JsonPropertyName("file_id")]
        public string FileId { get; set; } = string.Empty;

        [JsonPropertyName("index_in_list")]
        public int IndexInList { get; set; }

        [JsonPropertyName("file_length")]
        public double FileLength { get; set; }

        [JsonIgnore]
        public string NormalizedText => AlignmentProcessor.NormalizeText(Text);

        /// <summary>
        /// Assigns sequential indices to all fragments in a list.
        /// </summary>
        public static void AssignListIndices(List<AudioFragment> frags)
        {
            for (int i = 0; i < frags.Count; i++)
            {
                frags[i].IndexInList = i;
            }
        }
    }
}
