namespace Epub3MediaOverlays.Core.MediaOverlayGeneration.Models
{
    /// <summary>
    /// Represents a text segment extracted from an HTML element.
    /// Each segment corresponds to a single text node within an HTML parent element.
    /// </summary>
    public class HtmlTextSegment
    {
        public string FileName { get; set; }
        public string ParentXPath { get; set; }
        public int TextNodeIndex { get; set; }
        public string OriginalText { get; set; }
        public string EditedText { get; set; }
    }
}
