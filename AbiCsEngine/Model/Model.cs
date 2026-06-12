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
        public virtual int Length => Text.Length;
    }
    public sealed class EopRun : TextRun
    {
        public EopRun()
        {
            Text = string.Empty;
        }

        public override int Length => 1;
    }


    public class Paragraph
    {
        public List<TextRun> Runs { get; set; } = new List<TextRun>();

        public int Length
        {
            get
            {
                int len = 0;

                foreach (var run in Runs)
                    len += run.Length;

                return len;
            }
        }

    }

    public class Document
    {
        public List<Paragraph> Paragraphs { get; set; } = new List<Paragraph>();
    }

    public sealed class RunPosition
    {
        public TextRun Run = null!;
        public int OffsetInRun;
        public Paragraph Paragraph = null!;
        public int RunIndex;
    }



}