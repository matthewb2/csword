using System.Collections.Generic;
using System.Drawing;

namespace AbiCsEngine
{
    public class TextRun
    {
        public string Text { get; set; } = string.Empty;
        public string FontName { get; set; } = "맑은 고딕";
        public float FontSize { get; set; } = 11f;
        public FontStyle FontStyle { get; set; } = FontStyle.Regular;
        public Color ForeColor { get; set; } = Color.Black;
        public Paragraph? ParentParagraph { get; set; }
    }

    public class Paragraph
    {
        public List<TextRun> Runs { get; set; } = new List<TextRun>();
    }

    public class Document
    {
        public List<Paragraph> Paragraphs { get; set; } = new List<Paragraph>();
    }

    public struct DocPosition
    {
        public Paragraph Paragraph;
        public TextRun Run;
        public int Offset;
    }
}