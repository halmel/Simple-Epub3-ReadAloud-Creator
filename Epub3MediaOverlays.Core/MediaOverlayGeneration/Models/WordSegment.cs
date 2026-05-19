using System.Text.Json.Serialization;

namespace Epub3MediaOverlays.Core.MediaOverlayGeneration.Models
{
    /// <summary>
    /// Represents a word or phrase extracted from the EPUB text content.
    /// Each word segment can be linked to zero or more audio fragments for synchronization.
    /// </summary>
    public class WordSegment
    {
        public string FileName { get; set; } = string.Empty;
        public string ParentXPath { get; set; } = string.Empty;
        public int TextNodeIndex { get; set; }

        public string Word { get; set; } = string.Empty;
        public int WordIndexInSegment { get; set; }

        public int SentenceIndex { get; set; } = -1;
        public int MaxSentenceIndex = -1;

        public List<AudioFragment> LinkedSegments { get; set; } = new();

        public int IndexInList { get; set; }

        /// <summary>
        /// Gets the normalized (lowercase) version of the word.
        /// </summary>
        public string NormalizedWord
        {
            get
            {
                // Normalize: lowercase, non-letters become spaces (except . and , → .)
                if (string.IsNullOrWhiteSpace(Word))
                    return string.Empty;

                Span<char> buffer = stackalloc char[Word.Length];
                int pos = 0;
                bool lastWasSpace = true;

                foreach (char c in Word)
                {
                    char ch = c;

                    if (ch >= 'A' && ch <= 'Z')
                        ch = (char)(ch + 32);

                    if (ch >= 'a' && ch <= 'z')
                    {
                        buffer[pos++] = ch;
                        lastWasSpace = false;
                    }
                    else if (ch == '.' || ch == ',')
                    {
                        buffer[pos++] = '.';
                        lastWasSpace = false;
                    }
                    else if (!lastWasSpace)
                    {
                        buffer[pos++] = ' ';
                        lastWasSpace = true;
                    }
                }

                return new string(buffer[..(lastWasSpace && pos > 0 ? pos - 1 : pos)]);
            }
        }

        /// <summary>
        /// Gets the character length of the normalized word.
        /// </summary>
        public int NormalizedLength => NormalizedWord.Length;

        public int NormArrayIndex { get; set; }
        public int NormIndexIndexEnd => NormArrayIndex + NormalizedLength;

        /// <summary>
        /// Assigns sequential indices to all word segments in a list.
        /// </summary>
        public static void AssignListIndices(List<WordSegment> words)
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
}
