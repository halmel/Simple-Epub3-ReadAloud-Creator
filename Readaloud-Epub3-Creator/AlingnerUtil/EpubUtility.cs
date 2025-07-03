using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using HtmlAgilityPack;
using EpubSharp;
using System.Text.RegularExpressions;
using System.Text.Json;
using static Readaloud_Epub3_Creator.TranscriptClass;
using System.Text;
using System.Globalization;
using System.IO.Compression;
using System.Xml;


// Rebuild the EPUB by replacing HTML content with edited HtmlDocuments

namespace Readaloud_Epub3_Creator 
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

                // STEP 2: Replace HTML files
                foreach (var kvp in updatedHtmlDocs)
                {
                    string htmlPath = Directory.GetFiles(tempDir, kvp.Key, SearchOption.AllDirectories).FirstOrDefault();
                    if (htmlPath != null)
                    {
                        using (var sw = new StringWriter())
                        {
                            kvp.Value.Save(sw); // Forces rebuild of the HTML tree
                            string forcedUpdatedHtml = sw.ToString(); // Contains the latest DOM changes
                            File.WriteAllText(htmlPath, forcedUpdatedHtml); // Write fully-updated HTML
                        }
                    }
                }


                // STEP 3: Prepare folders at EPUB root
                string audioDir = Path.Combine(tempDir, "Audio");
                string mediaOverlaysDir = Path.Combine(tempDir, "MediaOverlays");

                if (!Directory.Exists(audioDir))
                    Directory.CreateDirectory(audioDir);

                if (!Directory.Exists(mediaOverlaysDir))
                    Directory.CreateDirectory(mediaOverlaysDir);

                // Copy files into their folders
                foreach (var smilFile in smilFiles)
                    File.Copy(smilFile, Path.Combine(mediaOverlaysDir, Path.GetFileName(smilFile)), true);

                foreach (var audioFile in audioFiles)
                    File.Copy(audioFile, Path.Combine(audioDir, Path.GetFileName(audioFile)), true);

                // STEP 4: Patch OPF manifest
                string opfPath = Directory.GetFiles(tempDir, "*.opf", SearchOption.AllDirectories).FirstOrDefault();
                if (opfPath == null)
                    throw new Exception("OPF file not found.");

                var doc = new XmlDocument();
                doc.Load(opfPath);

                var nsmgr = new XmlNamespaceManager(doc.NameTable);
                nsmgr.AddNamespace("opf", "http://www.idpf.org/2007/opf");

                var manifest = doc.SelectSingleNode("//opf:manifest", nsmgr);
                if (manifest == null)
                    throw new Exception("Manifest not found in OPF.");

                // STEP 4b: Add <meta> duration elements inside <metadata> in OPF
                var metadataNode = doc.SelectSingleNode("//opf:metadata", nsmgr);
                if (metadataNode == null)
                {
                    // create <metadata> if missing
                    metadataNode = doc.CreateElement("metadata", doc.DocumentElement.NamespaceURI);
                    doc.DocumentElement.InsertBefore(metadataNode, doc.DocumentElement.FirstChild);
                }

                // Prepare mapping SMIL file -> id and durations for metadata
                var smilMap = new Dictionary<string, string>(); // HTML href => SMIL id
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
                    string smilDuration = ToEpubMetadataTime(durationSeconds);  // Reuse your own formatter

                    var meta = doc.CreateElement("meta", doc.DocumentElement.NamespaceURI);
                    meta.SetAttribute("property", "media:duration");
                    meta.SetAttribute("refines", $"#{smilId}");
                    meta.InnerText = smilDuration;
                    metadataNode.AppendChild(meta);

                    smilDurations.Add( durationSeconds);

                    // Add <item> for SMIL file in manifest
                    XmlElement item = doc.CreateElement("item", manifest.NamespaceURI);
                    item.SetAttribute("id", smilId);
                    item.SetAttribute("href", smilHref);
                    item.SetAttribute("media-type", "application/smil+xml");
                    manifest.AppendChild(item);
                }

                // 2. Add audio files to manifest
                int audioIndex = 1;
                foreach (var audio in audioFiles)
                {
                    string fileName = Path.GetFileName(audio);
                    string safeId = $"a{audioIndex++}"; // Sequential and starts with a letter

                    XmlElement item = doc.CreateElement("item", manifest.NamespaceURI);
                    item.SetAttribute("id", safeId);
                    item.SetAttribute("href", $"Audio/{fileName}");
                    item.SetAttribute("media-type", "audio/mpeg");
                    manifest.AppendChild(item);
                }


                // 3. Link SMIL overlays to HTML items using mapping
                var htmlItems = doc.SelectNodes("//opf:item[@media-type='application/xhtml+xml']", nsmgr);
                foreach (XmlElement item in htmlItems)
                {
                    string href = item.GetAttribute("href");
                    if (smilMap.TryGetValue(href, out var smilId))
                    {
                        item.SetAttribute("media-overlay", smilId);
                    }
                }




                double totalDurationSeconds = smilDurations.Sum(); // List<double>
                string totalDuration = ToEpubMetadataTime(totalDurationSeconds);

                var totalMeta = doc.CreateElement("meta", doc.DocumentElement.NamespaceURI);
                totalMeta.SetAttribute("property", "media:duration");
                totalMeta.InnerText = totalDuration;
                metadataNode.AppendChild(totalMeta);


                doc.Save(opfPath);




                doc.Save(opfPath);


                // STEP 5: Repack EPUB properly
                if (File.Exists(outputEpubPath))
                    File.Delete(outputEpubPath);


                using (var zip = ZipFile.Open(outputEpubPath, ZipArchiveMode.Create))
                {
                    // mimetype first, no compression
                    string mimetypePath = Path.Combine(tempDir, "mimetype");
                    zip.CreateEntryFromFile(mimetypePath, "mimetype", CompressionLevel.NoCompression);

                    // Add the rest of files
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




        public class HtmlContainer
        {
            public string FileName { get; set; } // HTML file this came from
            public HtmlDocument OriginalDocument { get; set; }
            public List<HtmlTextSegment> Segments { get; set; } = new();
        }

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

                HtmlNode oldDoc = doc.DocumentNode.Clone();
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

            public List<Segment> LinkedSegments { get; set; } = new();

            public int IndexInList { get; set; }
            public static void AssignListIndices(List<WordSegment> words)
            {
                for (int i = 0; i < words.Count; i++)
                {
                    words[i].IndexInList = i;
                }
            }
        }


        public class SmilPar
        {
            public string Id { get; set; }
            public List<int> SentenceIndices { get; set; } = new();
            public string SegmentFileName { get; set; }
            public double ClipBegin { get; set; }
            public double ClipEnd { get; set; }

            public string ToXml()
            {
                var sb = new StringBuilder();
                sb.AppendLine($"  <par id=\"{Id}\">");

                foreach (var idx in SentenceIndices)
                {
                    sb.AppendLine($"    <text src=\"../{SegmentFileName}#id-{Id}-SentanceIndex-{idx}\"/>");
                }

                sb.AppendLine($"    <audio src=\"../Audio/{SegmentFileName}\" clipBegin=\"{ClipBegin:F3}s\" clipEnd=\"{ClipEnd:F3}s\"/>");
                sb.AppendLine($"  </par>");

                return sb.ToString();
            }
        }
      
        public static void GenerateSplitSmilFilesGroupedByFile(List<WordSegment> words,
             string outputDirectory = "output")
        {
            string audioFolder = "../Audio/";
            Directory.CreateDirectory(outputDirectory);

            // 🧹 Clean up old SMIL files before generating new ones
            foreach (var oldSmilFile in Directory.GetFiles(outputDirectory, "overlay_*.smil"))
            {
                try
                {
                    File.Delete(oldSmilFile);
                    Console.WriteLine($"[CLEAN] Deleted old SMIL: {oldSmilFile}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[WARN] Failed to delete {oldSmilFile}: {ex.Message}");
                }
            }








            var groupedByFile = words
                .Where(w => w.LinkedSegments?.Count > 0)
                .GroupBy(w => w.FileName)
                .ToList();

            int parGlobalCounter = 0;

            foreach (var fileGroup in groupedByFile)
            {
                string fileName = fileGroup.Key;
                string htmlRef = $"../{fileName}";
                string smilFileName = Path.Combine(outputDirectory, $"overlay_{Path.GetFileNameWithoutExtension(fileName)}.smil");

                var smilContent = new StringBuilder();

                // Calculate total length for this file by summing all segment lengths
                double totalLengthSeconds = 0;

                var groupedByFileId = fileGroup
                    .Where(w => w.LinkedSegments != null && w.LinkedSegments.Count > 0)
                    .GroupBy(w => w.LinkedSegments[0].fileId);

                foreach (var group in groupedByFileId)
                {
                    var allSegments = group
                        .SelectMany(w => w.LinkedSegments)
                        .Where(s => s.fileId == group.Key)
                        .ToList();

                    var firstSegment = allSegments.OrderBy(s => s.start).First();
                    var lastSegment = allSegments.OrderByDescending(s => s.end).First();

                    totalLengthSeconds += lastSegment.end - firstSegment.start;
                }


                // Add the total length comment before the <smil> tag
                smilContent.AppendLine($"<!-- TotalLength: {totalLengthSeconds} seconds -->");

                // Start SMIL file
                smilContent.AppendLine(@"<smil xmlns=""http://www.w3.org/ns/SMIL"" xmlns:epub=""http://www.idpf.org/2007/ops"" version=""3.0"">");
                smilContent.AppendLine("  <body>");
                smilContent.AppendLine($"    <seq id=\"id_overlay_{Path.GetFileNameWithoutExtension(fileName)}\" epub:textref=\"{htmlRef}\" epub:type=\"chapter\">");

                var bySegmentSet = fileGroup
                    .GroupBy(w => string.Join(";", w.LinkedSegments.Select(s => $"{s.fileId}_{s.id}")))
                    .ToList();

                double PreviusEndTime = 0;
                double FileEnd = fileGroup.ToList().First(x => x.LinkedSegments.Count > 0).LinkedSegments[0].fileLength;

                foreach (var segmentGroup in bySegmentSet)
                {
                    var wordsWithSameSegments = segmentGroup.ToList();
                    List<IGrouping<int, WordSegment>> sentenceGroups = wordsWithSameSegments
                        .GroupBy(w => w.SentenceIndex)
                        .OrderBy(g => g.Key)
                        .ToList();

                    var allSegments = wordsWithSameSegments.SelectMany(w => w.LinkedSegments).Distinct().ToList();
                    var firstSeg = allSegments.OrderBy(s => s.IndexInList).First();
                    var lastSeg = allSegments.OrderBy(s => s.IndexInList).Last();

                    string audioSrc = $"{audioFolder}{firstSeg.fileId}";
                    double clipBegin = PreviusEndTime;

                    double clipEnd = lastSeg.end;
                    if (segmentGroup == bySegmentSet.Last())
                    {
                        clipEnd = FileEnd;
                    }

                    double difference = lastSeg.end - clipBegin;
                    double[] doubles = new double[sentenceGroups.Count];

                    if (sentenceGroups[0].Key != -1)
                    {
                        doubles = CalculateDifferences(sentenceGroups, difference);
                    }
                    for (int i = 0; i < sentenceGroups.Count; i++)
                    {
                        var group = sentenceGroups[i];
                        if (group.Key == -1)
                        {
                            string textRef = $"../{fileName}#id-sentence{parGlobalCounter}";

                            smilContent.AppendLine($"      <par id=\"sentence{parGlobalCounter}\">");
                            smilContent.AppendLine($"        <text src=\"{textRef}\"/>");
                            smilContent.AppendLine($"        <audio src=\"{audioSrc}\" clipBegin=\"{ToSmilTime(clipBegin)}\" clipEnd=\"{ToSmilTime(clipEnd)}\"/>");
                            smilContent.AppendLine("      </par>");
                        }
                        else
                        {
                            string textRef = $"../{fileName}#id-sentence{parGlobalCounter}-{group.Key}";

                            clipEnd = clipBegin + doubles[i];
                            smilContent.AppendLine($"      <par id=\"sentence{parGlobalCounter}\">");
                            smilContent.AppendLine($"        <text src=\"{textRef}\"/>");
                            smilContent.AppendLine($"        <audio src=\"{audioSrc}\" clipBegin=\"{ToSmilTime(clipBegin)}\" clipEnd=\"{ToSmilTime(clipEnd)}\"/>");
                            smilContent.AppendLine("      </par>");
                            clipBegin = clipEnd;
                        }
                    }
                    PreviusEndTime = clipEnd;
                    parGlobalCounter++;
                }

                // End SMIL file
                smilContent.AppendLine("    </seq>");
                smilContent.AppendLine("  </body>");
                smilContent.AppendLine("</smil>");

                File.WriteAllText(smilFileName, smilContent.ToString());
                Console.WriteLine($"[OK] Generated SMIL: {smilFileName}");
            }

            Console.WriteLine($"[DONE] SMIL generation complete: {groupedByFile.Count} file(s), {parGlobalCounter} <par> blocks total.");
        }



        public static double[] CalculateDifferences(List<IGrouping<int, WordSegment>> sentenceGroups, double difference)
        {
            // Total length of all words combined
            int totalLength = sentenceGroups
                .SelectMany(g => g)
                .Sum(ws => ws.Word.Length);

            int count = sentenceGroups.Count;
            double[] results = new double[count];

            if (totalLength == 0)
            {
                // If total length is zero, just divide difference equally
                double equalValue = difference / count;
                for (int i = 0; i < count; i++)
                {
                    results[i] = equalValue;
                }
                return results;
            }

            // Calculate the length sum for each sentence group
            double[] groupLengths = new double[count];
            for (int i = 0; i < count; i++)
            {
                groupLengths[i] = sentenceGroups[i].Sum(ws => ws.Word.Length);
            }

            // Sum of all groupLengths should equal totalLength, but just in case
            double sumGroupLengths = groupLengths.Sum();

            // Calculate results proportional to groupLengths, scaled by 'difference'
            for (int i = 0; i < count; i++)
            {
                results[i] = groupLengths[i] / sumGroupLengths * difference;
            }

            // Due to floating point precision, sum(results) might be slightly off difference.
            // Adjust the last element to fix the sum exactly.
            double currentSum = results.Sum();
            double correction = difference - currentSum;
            results[count - 1] += correction;

            return results;
        }





        public static string ToSmilTime(double timeInSeconds)
        {
            // Formats time using comma as decimal separator for SMIL 3.0 compatibility
            return timeInSeconds.ToString("0.###", new CultureInfo("fr-FR")) + "s";
        }








        public static void SaveWordSegments(List<WordSegment> words, string path)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(path, JsonSerializer.Serialize(words, options));
        }


        public static List<WordSegment> LoadWordSegments(string path)
        {
            if (!File.Exists(path))
                return null;

            return JsonSerializer.Deserialize<List<WordSegment>>(File.ReadAllText(path));
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

        public class WordSegmentCollection
        {
            private readonly List<WordSegment> _segments;

            // Stores the mapping of character indices to WordSegment indices
            private List<(int startCharIndex, int endCharIndex, int wordIndex)> _charIndexMap;

            public WordSegmentCollection(List<WordSegment> segments)
            {
                _segments = segments ?? throw new ArgumentNullException(nameof(segments));
                WordSegment.AssignListIndices(_segments);
            }

            public string GetSubSequenceString(int startIndex, int length, out List<int> wordCharStartIndices)
            {
                if (startIndex < 0 || startIndex >= _segments.Count || length <= 0)
                    throw new ArgumentOutOfRangeException();

                int endIndex = Math.Min(startIndex + length, _segments.Count);
                _charIndexMap = new List<(int, int, int)>();
                wordCharStartIndices = new List<int>();

                var sb = new System.Text.StringBuilder();
                int currentCharIndex = 0;

                for (int i = startIndex; i < endIndex; i++)
                {
                    var word = _segments[i].Word;
                    wordCharStartIndices.Add(currentCharIndex);
                    sb.Append(word);
                    _charIndexMap.Add((currentCharIndex, currentCharIndex + word.Length - 1, i));
                    currentCharIndex += word.Length;
                }

                return sb.ToString();
            }

            public int GetWordIndexContainingChar(int charIndex)
            {
                if (_charIndexMap == null || !_charIndexMap.Any())
                    throw new InvalidOperationException("Call GetSubSequenceString first to initialize index mapping.");

                foreach (var (start, end, wordIndex) in _charIndexMap)
                {
                    if (charIndex >= start && charIndex <= end)
                        return wordIndex;
                }

                throw new ArgumentOutOfRangeException(nameof(charIndex), "Character index is out of bounds.");
            }
        }







    }
}
