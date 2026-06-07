using System.Linq;
using Epub3MediaOverlays.Core.MediaOverlayGeneration;
using Epub3MediaOverlays.Core.MediaOverlayGeneration.Models;

namespace Epub3MediaOverlays.Core.MediaOverlayGeneration.Internal
{
    partial class AlignmentProcessor
    {
        public class AlignmentMapper
        {
            public int StartId;
            public int EndId;
            public int ListIndex;

            public int Length => EndId - StartId;
            public int FirstWordIndex;
            public int LastWordIndex;
        }

        public List<WordGap> wordGaps = new List<WordGap>();
        public List<FragmentGap> fragmentGaps = new List<FragmentGap>();

        private void InitializeAlignmentData()
        {
            WordsMap = new AlignmentMapper[BookSegments.Count];
            for (int i = 0; i < BookSegments.Count; i++)
                WordsMap[i] = new AlignmentMapper();

            words = BuildCharArray(BookSegments, WordsMap, s => s.NormalizedWord);

            FragmentsMap = new AlignmentMapper[TranscriptSegments.Count];
            for (int i = 0; i < TranscriptSegments.Count; i++)
                FragmentsMap[i] = new AlignmentMapper();

            fragments = BuildCharArray(TranscriptSegments, FragmentsMap, s => s.NormalizedText);
            WordsSentences = BuildSentenceMapFromWords();
        }

        private static char[] BuildCharArray<T>(
            List<T> segments,
            AlignmentMapper[] map,
            Func<T, string> selector) where T : class
        {
            var result = new List<char>();
            int pos = 0;
            bool lastWasSpace = true;

            foreach (var segment in segments)
            {
                string text = selector(segment);
                int segIndex = (segment as dynamic).IndexInList;

                if (string.IsNullOrEmpty(text))
                {
                    map[segIndex] = new AlignmentMapper { StartId = pos, EndId = pos, ListIndex = segIndex };
                    continue;
                }

                if (!lastWasSpace && text[0] != '.')
                {
                    result.Add(' ');
                    pos++;
                }

                int startPos = pos;
                foreach (char ch in text)
                    result.Add(ch);

                pos += text.Length;
                map[segIndex] = new AlignmentMapper
                {
                    StartId = startPos,
                    EndId = pos,
                    ListIndex = segIndex
                };

                lastWasSpace = false;
            }

            return result.ToArray();
        }

        /// <summary>
        /// Normalizes text: lowercase letters kept, non-letters become spaces (except . and , which become .)
        /// </summary>
        public static string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            Span<char> buffer = stackalloc char[text.Length];
            int pos = 0;
            bool lastWasSpace = true;

            foreach (char c in text)
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

        private AlignmentMapper[] BuildSentenceMapFromWords()
        {
            var sentences = new List<AlignmentMapper>();
            int? sentenceStartChar = null;
            int sentenceStartWord = 0;
            int sentenceIndex = 0;

            for (int i = 0; i < WordsMap.Length; i++)
            {
                var word = WordsMap[i];

                if (sentenceStartChar == null)
                {
                    sentenceStartChar = word.StartId;
                    sentenceStartWord = i;
                }

                bool isEnd = word.EndId > word.StartId && words[word.EndId - 1] == '.';

                if (isEnd)
                {
                    sentences.Add(new AlignmentMapper
                    {
                        StartId = sentenceStartChar.Value,
                        EndId = word.EndId,
                        ListIndex = sentenceIndex++,
                        FirstWordIndex = sentenceStartWord,
                        LastWordIndex = i
                    });

                    sentenceStartChar = null;
                }
            }

            return sentences.ToArray();
        }

        private ReadOnlySpan<char> GetWordChars(int firstWordInclusive, int lastWordInclusive)
        {
            lastWordInclusive = Math.Min(lastWordInclusive, WordsMap.Length - 1);
            if (firstWordInclusive > lastWordInclusive)
                throw new ArgumentException("firstWordInclusive must be <= lastWordInclusive");

            var firstWord = WordsMap[firstWordInclusive];
            var lastWord = WordsMap[lastWordInclusive];
            return words.AsSpan(firstWord.StartId, lastWord.EndId - firstWord.StartId);
        }

        private ReadOnlySpan<char> GetFragmentChars(int firstFragmentInclusive, int lastFragmentInclusive)
        {
            if (firstFragmentInclusive > lastFragmentInclusive)
                throw new ArgumentException("firstFragmentInclusive must be <= lastFragmentInclusive");

            var firstFragment = FragmentsMap[firstFragmentInclusive];
            var lastFragment = FragmentsMap[lastFragmentInclusive];
            return fragments.AsSpan(firstFragment.StartId, lastFragment.EndId - firstFragment.StartId);
        }

        private int GetCountToReachLength(AlignmentMapper[] map, int startIndex, int targetCharLength)
        {
            if (map == null || startIndex < 0 || startIndex >= map.Length - 1)
                return 0;

            int accumulatedLength = 0;
            int count = 0;

            for (int i = startIndex; i < map.Length - 1; i++)
            {
                accumulatedLength += map[i].Length;
                count++;

                if (accumulatedLength >= targetCharLength)
                    break;
            }

            return count;
        }

        public int GetFragmentCountForLength(int startFragmentId, int targetLength = -1)
            => GetCountToReachLength(FragmentsMap, startFragmentId, targetLength > 0 ? targetLength : Config.DefaultSegmentLength);

        public int GetWordCountForLength(int startWordId, int targetLength = -1)
            => GetCountToReachLength(WordsMap, startWordId, targetLength > 0 ? targetLength : Config.DefaultSegmentLength);

        public int GetSentenceCountForLength(int startSentenceId, int targetLength = -1)
            => GetCountToReachLength(WordsSentences, startSentenceId, targetLength > 0 ? targetLength : Config.DefaultSegmentLength);

        public int GetSafeExpansionLength(int desiredLength, int wordStartChar, int wordSearchEnd)
        {
            int maxAvailable = wordSearchEnd - wordStartChar;
            return desiredLength > maxAvailable ? maxAvailable : desiredLength;
        }

        public void AddAndMergeWordGap(int start, int end)
        {
            wordGaps.Add(new WordGap { StartWordIndex = start, EndWordIndex = end });
            wordGaps = wordGaps.OrderBy(g => g.StartWordIndex).ToList();

            for (int i = 0; i < wordGaps.Count - 1; i++)
            {
                if (wordGaps[i].EndWordIndex >= wordGaps[i + 1].StartWordIndex - 1)
                {
                    wordGaps[i].EndWordIndex = Math.Max(wordGaps[i].EndWordIndex, wordGaps[i + 1].EndWordIndex);
                    wordGaps.RemoveAt(i + 1);
                    i--;
                }
            }
        }

        public void AddAndMergeFragmentGap(int start, int end)
        {
            fragmentGaps.Add(new FragmentGap { StartFragmentIndex = start, EndFragmentIndex = end });
            fragmentGaps = fragmentGaps.OrderBy(g => g.StartFragmentIndex).ToList();

            for (int i = 0; i < fragmentGaps.Count - 1; i++)
            {
                if (fragmentGaps[i].EndFragmentIndex >= fragmentGaps[i + 1].StartFragmentIndex - 1)
                {
                    fragmentGaps[i].EndFragmentIndex = Math.Max(fragmentGaps[i].EndFragmentIndex, fragmentGaps[i + 1].EndFragmentIndex);
                    fragmentGaps.RemoveAt(i + 1);
                    i--;
                }
            }
        }

        public void ApplyMatch(int i, int wordIndex, int wordCount)
        {
            var matchedFragment = TranscriptSegments[i];
            for (int j = wordIndex; j < wordCount + wordIndex; j++)
                BookSegments[j].LinkedSegments.Add(matchedFragment);
        }

        private void SaveAlignmentResults()
        {
            EpubProcessor.SaveWordSegments(BookSegments, WordPath);
        }
    }
}
