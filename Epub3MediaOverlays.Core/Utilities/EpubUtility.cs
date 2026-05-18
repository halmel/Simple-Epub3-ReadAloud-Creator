using EpubSharp;
using HtmlAgilityPack;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;
using Epub3MediaOverlays.Core.AlingnerUtil;


// Rebuild the EPUB by replacing HTML content with edited HtmlDocuments

namespace Epub3MediaOverlays.Core.Utilities
{
    public class EpubUtility
    {
        // Loads an EPUB and extracts all HTML content as HtmlDocuments
        public static Dictionary<string, HtmlDocument> LoadEpubAndExtractHtml(string epubPath)
        {
            if (!File.Exists(epubPath))
                throw new FileNotFoundException($"EPUB not found: {epubPath}");

            EpubBook book = EpubReader.Read(epubPath);
            var result = new Dictionary<string, HtmlDocument>();

            foreach (EpubTextFile htmlFile in book.Resources.Html)
            {
                var html = htmlFile.TextContent;

                var doc = new HtmlDocument
                {
                    OptionWriteEmptyNodes = true     // Keeps self-closing tags like <img />
                };

                doc.LoadHtml(html);
                result[htmlFile.FileName] = doc;
            }

            return result;
        }



        private static double ExtractTotalLengthFromSmilSeconds(string smilFilePath)
        {
            var lines = File.ReadLines(smilFilePath);
            var totalLengthLine = lines.FirstOrDefault(line => line.Contains("<!-- TotalLength:"));
            if (totalLengthLine == null) return 0;

            // Match numbers like 270067,04 or 12345.67 — use comma or dot as decimal
            var match = Regex.Match(totalLengthLine, @"<!-- TotalLength:\s*([\d\.,]+)");

            if (match.Success)
            {
                string numberStr = match.Groups[1].Value.Replace(',', '.'); // Normalize to dot for double.Parse
                if (double.TryParse(numberStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double seconds))
                {
                    return seconds;
                }
            }

            return 0;
        }


        public static void RebuildEpubWithMedia(
            string originalEpubPath,
            Dictionary<string, HtmlDocument> updatedHtmlDocs,
            List<string> smilFiles,
            List<string> audioFiles,
            string outputEpubPath)
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "epub_rebuild_" + Guid.NewGuid());
            Directory.CreateDirectory(tempDir);

            try
            {
                // STEP 1: Extract EPUB
                ZipFile.ExtractToDirectory(originalEpubPath, tempDir);

                // STEP 2: Locate content.opf and set working root
                string opfPath = Directory.GetFiles(tempDir, "*.opf", SearchOption.AllDirectories).FirstOrDefault();
                if (opfPath == null)
                    throw new Exception("OPF file not found.");

                string contentRoot = Path.GetDirectoryName(opfPath);

                // STEP 3: Replace HTML files
                foreach (var kvp in updatedHtmlDocs)
                {
                    string fileNameOnly = Path.GetFileName(kvp.Key);
                    string htmlPath = Directory
                        .EnumerateFiles(contentRoot, "*", SearchOption.AllDirectories)
                        .FirstOrDefault(f => string.Equals(Path.GetFileName(f), fileNameOnly, StringComparison.OrdinalIgnoreCase));

                    if (htmlPath != null)
                    {
                        using (var sw = new StringWriter())
                        {
                            kvp.Value.Save(sw);
                            string updatedHtml = sw.ToString();
                            File.WriteAllText(htmlPath, updatedHtml);
                        }
                    }
                }

                // STEP 4: Prepare folders relative to content.opf
                string audioDir = Path.Combine(contentRoot, "Audio");
                string mediaOverlaysDir = Path.Combine(contentRoot, "MediaOverlays");

                Directory.CreateDirectory(audioDir);
                Directory.CreateDirectory(mediaOverlaysDir);

                foreach (var smilFile in smilFiles)
                    File.Copy(smilFile, Path.Combine(mediaOverlaysDir, Path.GetFileName(smilFile)), true);

                foreach (var audioFile in audioFiles)
                    File.Copy(audioFile, Path.Combine(audioDir, Path.GetFileName(audioFile)), true);

                // STEP 5: Patch OPF manifest
                var doc = new XmlDocument();
                doc.Load(opfPath);

                var nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("opf", "http://www.idpf.org/2007/opf");

                var manifest = doc.SelectSingleNode("//opf:manifest", nsmgr);
                if (manifest == null)
                    throw new Exception("Manifest not found in OPF.");

                var metadataNode = doc.SelectSingleNode("//opf:metadata", nsmgr);
                if (metadataNode == null)
                {
                    metadataNode = doc.CreateElement("metadata", doc.DocumentElement.NamespaceURI);
                    doc.DocumentElement.InsertBefore(metadataNode, doc.DocumentElement.FirstChild);
                }

                var smilMap = new Dictionary<string, string>();
                var smilDurations = new List<double>();

                foreach (var smilPath in smilFiles)
                {
                    string fileName = Path.GetFileName(smilPath);
                    string smilHref = $"MediaOverlays/{fileName}";

                    var smilDoc = new XmlDocument();
                    smilDoc.Load(smilPath);
                    smilDoc.DocumentElement?.SetAttribute("xmlns:epub", "http://www.idpf.org/2007/ops");

                    XmlNamespaceManager smilNsMgr = new XmlNamespaceManager(smilDoc.NameTable);
                    smilNsMgr.AddNamespace("smil", "http://www.w3.org/ns/SMIL");
                    smilNsMgr.AddNamespace("epub", "http://www.idpf.org/2007/ops");

                    var seqNode = smilDoc.SelectSingleNode("//smil:seq", smilNsMgr) as XmlElement;
                    if (seqNode == null) continue;

                    string smilId = seqNode.GetAttribute("id");
                    string textRef = seqNode.GetAttribute("epub:textref");
                    string htmlFile = Path.GetFileName(textRef);

                    smilMap[htmlFile] = smilId;
                    double durationSeconds = ExtractTotalLengthFromSmilSeconds(smilPath);
                    string smilDuration = ToEpubMetadataTime(durationSeconds);

                    var meta = doc.CreateElement("meta", doc.DocumentElement.NamespaceURI);
                    meta.SetAttribute("property", "media:duration");
                    meta.SetAttribute("refines", $"#{smilId}");
                    meta.InnerText = smilDuration;
                    metadataNode.AppendChild(meta);

                    smilDurations.Add(durationSeconds);

                    var item = doc.CreateElement("item", manifest.NamespaceURI);
                    item.SetAttribute("id", smilId);
                    item.SetAttribute("href", smilHref);
                    item.SetAttribute("media-type", "application/smil+xml");
                    manifest.AppendChild(item);
                }

                int audioIndex = 1;
                foreach (var audio in audioFiles)
                {
                    string fileName = Path.GetFileName(audio);
                    string safeId = $"a{audioIndex++}";

                    var item = doc.CreateElement("item", manifest.NamespaceURI);
                    item.SetAttribute("id", safeId);
                    item.SetAttribute("href", $"Audio/{fileName}");
                    item.SetAttribute("media-type", "audio/mpeg");
                    manifest.AppendChild(item);
                }

                var htmlItems = doc.SelectNodes("//opf:item[@media-type='application/xhtml+xml']", nsmgr);
                foreach (XmlElement item in htmlItems)
                {
                    string href = item.GetAttribute("href");
                    if (smilMap.TryGetValue(Path.GetFileName(href), out var smilId))
                    {
                        item.SetAttribute("media-overlay", smilId);
                    }
                }

                double totalDurationSeconds = smilDurations.Sum();
                string totalDuration = ToEpubMetadataTime(totalDurationSeconds);

                var totalMeta = doc.CreateElement("meta", doc.DocumentElement.NamespaceURI);
                totalMeta.SetAttribute("property", "media:duration");
                totalMeta.InnerText = totalDuration;
                metadataNode.AppendChild(totalMeta);

                doc.Save(opfPath);

                // STEP 6: Repack EPUB
                if (File.Exists(outputEpubPath))
                    File.Delete(outputEpubPath);

                using (var zip = ZipFile.Open(outputEpubPath, ZipArchiveMode.Create))
                {
                    string mimetypePath = Path.Combine(tempDir, "mimetype");
                    zip.CreateEntryFromFile(mimetypePath, "mimetype", CompressionLevel.NoCompression);

                    foreach (string file in Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories))
                    {
                        string entryName = Path.GetRelativePath(tempDir, file).Replace('\\', '/');
                        if (entryName == "mimetype") continue;

                        zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                    }
                }
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }
        }



        private static string ToEpubMetadataTime(double totalSeconds)
        {
            int hours = (int)(totalSeconds / 3600);
            int minutes = (int)((totalSeconds % 3600) / 60);
            int seconds = (int)(totalSeconds % 60);
            double fractional = totalSeconds - Math.Floor(totalSeconds);

            // Take only two decimal digits for fractional seconds (not rounded up to next full second)
            string fractionStr = ((int)(fractional * 100)).ToString("00");

            return $"{hours:00}:{minutes:00}:{seconds:00}.{fractionStr}";
        }




        //public class HtmlContainer
        //{
        //    public string FileName { get; set; } // HTML file this came from
        //    public HtmlDocument OriginalDocument { get; set; }
        //    public List<HtmlTextSegment> Segments { get; set; } = new();
        //}

        public class HtmlTextSegment
        {
            public string FileName { get; set; }
            public string ParentXPath { get; set; }  // XPath to the parent element
            public int TextNodeIndex { get; set; }   // Index of the text node within parent's child text nodes
            public string OriginalText { get; set; }
            public string EditedText { get; set; }
        }





        public static List<HtmlTextSegment> ExtractAllTextSegments(Dictionary<string, HtmlDocument> htmlDocs)
        {
            var segments = new List<HtmlTextSegment>();

            foreach (var (fileName, doc) in htmlDocs)
            {
                var textNodes = doc.DocumentNode
                    .Descendants()
                    .Where(n =>
                        n.NodeType == HtmlNodeType.Text &&
                        !string.IsNullOrWhiteSpace(n.InnerText) &&
                        n.ParentNode.Name != "script" &&
                        n.ParentNode.Name != "style")
                    .ToList();

                foreach (var textNode in textNodes)
                {
                    var parent = textNode.ParentNode;
                    var parentXPath = parent.XPath;

                    var textSiblings = parent.ChildNodes
                        .Where(n => n.NodeType == HtmlNodeType.Text)
                        .ToList();

                    int index = textSiblings.IndexOf(textNode);

                    segments.Add(new HtmlTextSegment
                    {
                        FileName = fileName,
                        ParentXPath = parentXPath,
                        TextNodeIndex = index,
                        OriginalText = textNode.InnerText,
                        EditedText = null
                    });
                }
            }

            return segments;
        }

        public static Dictionary<string, HtmlDocument> RebuildHtmlFromSegments(
            Dictionary<string, HtmlDocument> originalDocs,
            List<HtmlTextSegment> segments)
        {
            var updatedDocs = originalDocs.ToDictionary(
                kvp => kvp.Key,
                kvp =>
                {
                    var newDoc = new HtmlDocument();
                    newDoc.LoadHtml(kvp.Value.DocumentNode.OuterHtml);
                    return newDoc;
                });

            foreach (var segment in segments)
            {
                if (!updatedDocs.TryGetValue(segment.FileName, out var doc))
                    continue;

                var parentNode = doc.DocumentNode.SelectSingleNode(segment.ParentXPath);
                if (parentNode == null)
                    continue;

                int textIndex = -1;
                for (int i = 0, count = 0; i < parentNode.ChildNodes.Count; i++)
                {
                    var child = parentNode.ChildNodes[i];
                    if (child.NodeType == HtmlNodeType.Text)
                    {
                        if (count == segment.TextNodeIndex)
                        {
                            child.InnerHtml = segment.EditedText ?? segment.OriginalText;
                            break;
                        }
                        count++;
                    }
                }
            }

            return updatedDocs;
        }
        public static Dictionary<string, HtmlDocument> CloneHtmlDocuments(Dictionary<string, HtmlDocument> originalDocs)
        {
            return originalDocs.ToDictionary(
                kvp => kvp.Key,
                kvp =>
                {
                    var newDoc = new HtmlDocument();
                    newDoc.LoadHtml(kvp.Value.DocumentNode.OuterHtml); // Clone by value (safe if not re-used)
                    return newDoc;
                });
        }
        public static void ApplyTextSegmentsToHtmlDocuments(
            Dictionary<string, HtmlDocument> htmlDocs,
            List<HtmlTextSegment> segments)
        {
            foreach (var segment in segments)
            {
                if (!htmlDocs.TryGetValue(segment.FileName, out HtmlDocument doc))
                    continue;

                // Ensure XPath is valid
                string parentXPath = segment.ParentXPath;
                if (string.IsNullOrWhiteSpace(parentXPath))
                    continue;

                // Sanitize bad xpaths like "/#document"
                if (parentXPath.StartsWith("/#document"))
                {
                    parentXPath = parentXPath.Replace("/#document", "");
                    if (string.IsNullOrEmpty(parentXPath))
                        parentXPath = "/"; // fallback to root
                }

                var parentNode = doc.DocumentNode.SelectSingleNode(parentXPath);
                if (parentNode == null)
                    continue;

                int textIndex = -1;
                for (int i = 0, count = 0; i < parentNode.ChildNodes.Count; i++)
                {
                    var child = parentNode.ChildNodes[i];
                    if (child.NodeType == HtmlNodeType.Text)
                    {
                        if (count == segment.TextNodeIndex)
                        {
                            // Modify the actual DOM node directly
                            child.InnerHtml = segment.EditedText;
                            break;
                        }
                        count++;
                    }
                }
            }
        }







        public class WordSegment
        {
            public string FileName { get; set; }
            public string ParentXPath { get; set; }
            public int TextNodeIndex { get; set; }

            public string Word { get; set; }
            public int WordIndexInSegment { get; set; }


            public int SentenceIndex { get; set; } = -1;

            public int MaxSentanceIndex = -1;

            public List<TranscriptClass.Fragment> LinkedSegments { get; set; } = new();

            public int IndexInList { get; set; }
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
            public string NormalizedWord => AlingnerNew.NormalizeText(Word);

            public int NormalizedLength => NormalizedWord.Length;

            public int NormArrayIndex { get; set; }
            public int NormIndexIndexEnd { get { return NormArrayIndex + NormalizedLength; } }


        }


        public static void NormalizeSegmentsToFullMp3Length(List<WordSegment> words)
        {
            // Find all unique fileIds across all linked segments
            var allSegments = words.SelectMany(w => w.LinkedSegments).ToList();
            var fileGroups = allSegments.GroupBy(s => s.FileId);

            foreach (var fileGroup in fileGroups)
            {
                var segments = fileGroup.OrderBy(s => s.Start).ToList();
                if (segments.Count == 0)
                    continue;

                var firstSeg = segments.First();
                var lastSeg = segments.Last();

                // 🔹 Normalize first and last
                double fileLength = lastSeg.FileLength;
                firstSeg.Start = 0;
                lastSeg.End = fileLength;

                // 🔹 Update all references in WordSegments that point to these
                foreach (var w in words)
                {
                    for (int i = 0; i < w.LinkedSegments.Count; i++)
                    {
                        var seg = w.LinkedSegments[i];
                        if (seg.FileId == fileGroup.Key)
                        {
                            // If matches first segment id → enforce new start
                            if (seg.IndexInList == firstSeg.IndexInList)
                                seg.Start = 0;
                            // If matches last segment id → enforce new end
                            if (seg.IndexInList == lastSeg.IndexInList)
                                seg.End = fileLength;
                        }
                    }
                }
            }
        }












        public static void SaveWordSegments(List<WordSegment> words, string path)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            string json = JsonSerializer.Serialize(words, options);
            File.WriteAllText(path, json);
        }

        public static List<WordSegment> LoadWordSegments(string path)
        {
            if (!File.Exists(path)) return null;

            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return new List<WordSegment>();

            return JsonSerializer.Deserialize<List<WordSegment>>(json);
        }








        public static List<WordSegment> SplitTextSegmentsIntoWords(List<HtmlTextSegment> segments)
        {
            var wordSegments = new List<WordSegment>();
            var wordRegex = new Regex(@"(\w+|\s+|[^\w\s]+)", RegexOptions.Compiled);

            foreach (var segment in segments)
            {
                string text = segment.EditedText ?? segment.OriginalText;
                var matches = wordRegex.Matches(text);

                for (int i = 0; i < matches.Count; i++)
                {
                    wordSegments.Add(new WordSegment
                    {
                        FileName = segment.FileName,
                        ParentXPath = segment.ParentXPath,
                        TextNodeIndex = segment.TextNodeIndex,
                        Word = matches[i].Value,
                        WordIndexInSegment = i,
                    });
                }
            }

            return wordSegments;
        }
        public static List<HtmlTextSegment> RecombineWordsIntoTextSegments(List<WordSegment> words)
        {
            var grouped = words
                .GroupBy(w => new { w.FileName, w.ParentXPath, w.TextNodeIndex })
                .Select(g =>
                {
                    string fullText = string.Concat(g.OrderBy(w => w.WordIndexInSegment).Select(w => w.Word));
                    return new HtmlTextSegment
                    {
                        FileName = g.Key.FileName,
                        ParentXPath = g.Key.ParentXPath,
                        TextNodeIndex = g.Key.TextNodeIndex,
                        OriginalText = null, // we're rebuilding, can leave null or cache
                        EditedText = fullText
                    };
                });

            return grouped.ToList();
        }

        //public class WordSegmentCollection
        //{
        //    private readonly List<WordSegment> _segments;

        //    // Stores the mapping of character indices to WordSegment indices
        //    private List<(int startCharIndex, int endCharIndex, int wordIndex)> _charIndexMap;

        //    public WordSegmentCollection(List<WordSegment> segments)
        //    {
        //        _segments = segments ?? throw new ArgumentNullException(nameof(segments));
        //        WordSegment.AssignListIndices(_segments);
        //    }

        //    public string GetSubSequenceString(int startIndex, int length, out List<int> wordCharStartIndices)
        //    {
        //        if (startIndex < 0 || startIndex >= _segments.Count || length <= 0)
        //            throw new ArgumentOutOfRangeException();

        //        int endIndex = Math.Min(startIndex + length, _segments.Count);
        //        _charIndexMap = new List<(int, int, int)>();
        //        wordCharStartIndices = new List<int>();

        //        var sb = new System.Text.StringBuilder();
        //        int currentCharIndex = 0;

        //        for (int i = startIndex; i < endIndex; i++)
        //        {
        //            var word = _segments[i].Word;
        //            wordCharStartIndices.Add(currentCharIndex);
        //            sb.Append(word);
        //            _charIndexMap.Add((currentCharIndex, currentCharIndex + word.Length - 1, i));
        //            currentCharIndex += word.Length;
        //        }

        //        return sb.ToString();
        //    }

        //    public int GetWordIndexContainingChar(int charIndex)
        //    {
        //        if (_charIndexMap == null || !_charIndexMap.Any())
        //            throw new InvalidOperationException("Call GetSubSequenceString first to initialize index mapping.");

        //        foreach (var (start, end, wordIndex) in _charIndexMap)
        //        {
        //            if (charIndex >= start && charIndex <= end)
        //                return wordIndex;
        //        }

        //        throw new ArgumentOutOfRangeException(nameof(charIndex), "Character index is out of bounds.");
        //    }
        //}







    }
}


