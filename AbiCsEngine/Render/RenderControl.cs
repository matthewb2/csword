using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AbiCsEngine
{
    public class RenderControl : UserControl
    {
        private Document? _document;
        private List<LayoutPage> _pages = new List<LayoutPage>();
        private GdiLayout _engine = new GdiLayout();

        private DocPosition? _caret;
        private bool _cursorVisible;
        private System.Windows.Forms.Timer _cursorBlinkTimer;

        public Document? Document
        {
            get => _document;
            set
            {
                _document = value;
                LinkParentParagraphs();
                RefreshLayout();
            }
        }

        private void LinkParentParagraphs()
        {
            if (_document == null) return;
            foreach (var para in _document.Paragraphs)
                foreach (var run in para.Runs)
                    run.ParentParagraph = para;
        }

        public RenderControl()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.UserPaint |
                          ControlStyles.OptimizedDoubleBuffer |
                          ControlStyles.Selectable, true);
            this.BackColor = Color.FromArgb(200, 200, 200);
            this.AutoScroll = true;
            this.TabStop = true;
            this.Cursor = Cursors.IBeam;

            _cursorBlinkTimer = new System.Windows.Forms.Timer { Interval = 500 };
            _cursorBlinkTimer.Tick += (s, e) =>
            {
                _cursorVisible = !_cursorVisible;
                this.Invalidate();
            };
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData)
            {
                case Keys.Left:
                case Keys.Right:
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (_caret == null) return;
            if (char.IsControl(e.KeyChar)) return;
            InsertCharacter(e.KeyChar);
            e.Handled = true;
        }

        private void InsertCharacter(char ch)
        {
            if (_caret == null) return;
            var caret = _caret.Value;
            caret.Run.Text = caret.Run.Text.Insert(caret.Offset, ch.ToString());
            caret.Offset++;
            _caret = caret;
            RefreshLayout();
        }

        private void DeleteBackward()
        {
            if (_caret == null) return;
            var caret = _caret.Value;
            if (caret.Offset == 0) return;
            caret.Run.Text = caret.Run.Text.Remove(caret.Offset - 1, 1);
            caret.Offset--;
            _caret = caret;
            RefreshLayout();
        }

        public void RefreshLayout()
        {
            if (_document == null) return;

            using (Graphics g = this.CreateGraphics())
            {
                _pages = _engine.ComputeLayout(_document, g);
            }

            if (_pages.Count > 0)
            {
                var lastPage = _pages[_pages.Count - 1];
                this.AutoScrollMinSize = new Size(
                    (int)LayoutPage.A4Dimension.Width + 100,
                    (int)lastPage.PageBounds.Bottom + 40);
            }

            if (_caret == null &&
                _document != null &&
                _document.Paragraphs.Count > 0 &&
                _document.Paragraphs[0].Runs.Count > 0)
            {
                _caret = new DocPosition
                {
                    Paragraph = _document.Paragraphs[0],
                    Run = _document.Paragraphs[0].Runs[0],
                    Offset = 0
                };
            }

            this.Invalidate();
        }

        private bool TryGetCaretLayoutPosition(out LayoutLine line, out int offset)
        {
            line = null!;
            offset = 0;

            if (_caret == null) return false;
            var caret = _caret.Value;

            foreach (var page in _pages)
            {
                foreach (var layoutLine in page.Lines)
                {
                    int globalOffset = 0;

                    foreach (var run in layoutLine.LayoutRuns)
                    {
                        if (run.StyleSource == caret.Run)
                        {
                            int localOffset = caret.Offset - run.SourceStartOffset;
                            if (localOffset >= 0 && localOffset <= run.Text.Length)
                            {
                                line = layoutLine;
                                offset = globalOffset + localOffset;
                                return true;
                            }
                        }

                        globalOffset += run.Text.Length;
                    }
                }
            }

            return false;
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            _cursorBlinkTimer.Start();
            _cursorVisible = true;
            this.Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            _cursorBlinkTimer.Stop();
            _cursorVisible = false;
            this.Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            this.Focus();

            PointF canvasPt = new PointF(
                e.X - this.AutoScrollPosition.X,
                e.Y - this.AutoScrollPosition.Y);

            var pos = HitTest(canvasPt);
            if (pos != null)
            {
                _caret = pos;
                _cursorVisible = true;
                this.Invalidate();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (_caret == null)
            {
                if (_document != null &&
                    _document.Paragraphs.Count > 0 &&
                    _document.Paragraphs[0].Runs.Count > 0)
                {
                    _caret = new DocPosition
                    {
                        Paragraph = _document.Paragraphs[0],
                        Run = _document.Paragraphs[0].Runs[0],
                        Offset = 0
                    };
                    e.Handled = true;
                    this.Invalidate();
                }
                return;
            }

            switch (e.KeyCode)
            {
                case Keys.Left:
                {
                    var caret = _caret.Value;
                    if (caret.Offset > 0)
                    {
                        caret.Offset--;
                        _caret = caret;
                    }
                    else
                    {
                        var prev = GetPreviousDocumentPosition(caret);
                        if (prev != null) _caret = prev;
                    }
                    e.Handled = true;
                    break;
                }
                case Keys.Right:
                {
                    var caret = _caret.Value;
                    if (caret.Offset < caret.Run.Text.Length)
                    {
                        caret.Offset++;
                        _caret = caret;
                    }
                    else
                    {
                        var next = GetNextDocumentPosition(caret);
                        if (next != null) _caret = next;
                    }
                    e.Handled = true;
                    break;
                }
                case Keys.Up:
                {
                    if (TryGetCaretLayoutPosition(out var line, out var offset))
                    {
                        float x = GetCursorXInPage(line, offset);
                        var adj = GetAdjacentLayoutLine(line, -1);
                        if (adj != null)
                        {
                            var pos = HitTestLine(adj, x);
                            if (pos != null) _caret = pos;
                        }
                    }
                    e.Handled = true;
                    break;
                }
                case Keys.Down:
                {
                    if (TryGetCaretLayoutPosition(out var line, out var offset))
                    {
                        float x = GetCursorXInPage(line, offset);
                        var adj = GetAdjacentLayoutLine(line, 1);
                        if (adj != null)
                        {
                            var pos = HitTestLine(adj, x);
                            if (pos != null) _caret = pos;
                        }
                    }
                    e.Handled = true;
                    break;
                }
                case Keys.Home:
                {
                    if (TryGetCaretLayoutPosition(out var line, out _))
                    {
                        var pos = HitTestLine(line, 0);
                        if (pos != null) _caret = pos;
                    }
                    e.Handled = true;
                    break;
                }
                case Keys.End:
                {
                    if (TryGetCaretLayoutPosition(out var line, out _))
                    {
                        var pos = HitTestLine(line, LayoutPage.A4Dimension.Width);
                        if (pos != null) _caret = pos;
                    }
                    e.Handled = true;
                    break;
                }
                case Keys.Back:
                    DeleteBackward();
                    e.Handled = true;
                    break;
                default:
                    return;
            }

            _cursorVisible = true;
            this.Invalidate();
        }

        private DocPosition? GetPreviousDocumentPosition(DocPosition current)
        {
            if (_document == null) return null;

            var para = current.Paragraph;
            int runIndex = para.Runs.IndexOf(current.Run);

            if (runIndex > 0)
            {
                var prevRun = para.Runs[runIndex - 1];
                return new DocPosition { Paragraph = para, Run = prevRun, Offset = prevRun.Text.Length };
            }

            int paraIndex = _document.Paragraphs.IndexOf(para);
            if (paraIndex > 0)
            {
                var prevPara = _document.Paragraphs[paraIndex - 1];
                if (prevPara.Runs.Count > 0)
                {
                    var lastRun = prevPara.Runs[^1];
                    return new DocPosition { Paragraph = prevPara, Run = lastRun, Offset = lastRun.Text.Length };
                }
            }

            return null;
        }

        private DocPosition? GetNextDocumentPosition(DocPosition current)
        {
            if (_document == null) return null;

            var para = current.Paragraph;
            int runIndex = para.Runs.IndexOf(current.Run);

            if (runIndex < para.Runs.Count - 1)
            {
                var nextRun = para.Runs[runIndex + 1];
                return new DocPosition { Paragraph = para, Run = nextRun, Offset = 0 };
            }

            int paraIndex = _document.Paragraphs.IndexOf(para);
            if (paraIndex < _document.Paragraphs.Count - 1)
            {
                var nextPara = _document.Paragraphs[paraIndex + 1];
                if (nextPara.Runs.Count > 0)
                {
                    return new DocPosition { Paragraph = nextPara, Run = nextPara.Runs[0], Offset = 0 };
                }
            }

            return null;
        }

        private LayoutLine? GetAdjacentLayoutLine(LayoutLine line, int direction)
        {
            for (int pi = 0; pi < _pages.Count; pi++)
            {
                var page = _pages[pi];
                for (int li = 0; li < page.Lines.Count; li++)
                {
                    if (page.Lines[li] == line)
                    {
                        int targetLi = li + direction;
                        if (targetLi >= 0 && targetLi < page.Lines.Count)
                            return page.Lines[targetLi];

                        int targetPi = pi + direction;
                        if (targetPi >= 0 && targetPi < _pages.Count)
                        {
                            var targetPage = _pages[targetPi];
                            if (targetPage.Lines.Count > 0)
                                return direction < 0
                                    ? targetPage.Lines[^1]
                                    : targetPage.Lines[0];
                        }
                        return null;
                    }
                }
            }
            return null;
        }

        private DocPosition? HitTest(PointF canvasPt)
        {
            foreach (var page in _pages)
            {
                float pageLeft = GetPageLeft();
                float pageRight = pageLeft + page.PageBounds.Width;

                if (canvasPt.X >= pageLeft && canvasPt.X <= pageRight &&
                    canvasPt.Y >= page.PageBounds.Y - 10 &&
                    canvasPt.Y <= page.PageBounds.Y + page.PageBounds.Height + 10)
                {
                    float clickX = canvasPt.X - pageLeft;

                    foreach (var line in page.Lines)
                    {
                        float lineTop = line.Bounds.Y;
                        float lineBottom = lineTop + line.Bounds.Height;
                        float hitMargin = Math.Max(line.Bounds.Height * 0.5f, 8f);

                        if (canvasPt.Y >= lineTop - hitMargin && canvasPt.Y <= lineBottom + hitMargin)
                            return HitTestLine(line, clickX);
                    }

                    if (page.Lines.Count > 0)
                    {
                        LayoutLine closest = page.Lines[0];
                        float minDist = float.MaxValue;
                        foreach (var line in page.Lines)
                        {
                            float dist = Math.Abs(canvasPt.Y - (line.Bounds.Y + line.Bounds.Height / 2));
                            if (dist < minDist) { minDist = dist; closest = line; }
                        }
                        return HitTestLine(closest, clickX);
                    }
                }
            }
            return null;
        }

        private DocPosition? HitTestLine(LayoutLine line, float pageRelativeX)
        {
            int charOffset = GetCharOffsetFromX(line, pageRelativeX);
            return GetDocPositionFromLineOffset(line, charOffset);
        }

        private DocPosition? GetDocPositionFromLineOffset(LayoutLine line, int charOffset)
        {
            int remaining = charOffset;

            foreach (var run in line.LayoutRuns)
            {
                int runLen = run.Text.Length;

                if (remaining <= runLen)
                {
                    return new DocPosition
                    {
                        Paragraph = run.StyleSource.ParentParagraph!,
                        Run = run.StyleSource,
                        Offset = run.SourceStartOffset + remaining
                    };
                }

                remaining -= runLen;
            }

            if (line.LayoutRuns.Count > 0)
            {
                var lastRun = line.LayoutRuns[^1];
                return new DocPosition
                {
                    Paragraph = lastRun.StyleSource.ParentParagraph!,
                    Run = lastRun.StyleSource,
                    Offset = lastRun.SourceStartOffset + lastRun.Text.Length
                };
            }

            return null;
        }

        private float GetCursorXInPage(LayoutLine line, int charOffset)
        {
            if (line.LayoutRuns.Count == 0) return 0;

            float x = line.LayoutRuns[0].Bounds.X;
            int remaining = charOffset;

            foreach (var run in line.LayoutRuns)
            {
                int runLen = run.Text.Length;
                if (remaining < runLen)
                {
                    if (remaining > 0)
                    {
                        using (Graphics g = this.CreateGraphics())
                        using (Font font = new Font(run.StyleSource.FontName, run.StyleSource.FontSize, run.StyleSource.FontStyle))
                        using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
                        {
                            sf.FormatFlags |= StringFormatFlags.NoClip;
                            sf.SetMeasurableCharacterRanges(new[] { new CharacterRange(0, remaining) });
                            RectangleF rect = new RectangleF(0, 0, 99999, 99999);
                            Region[] regions = g.MeasureCharacterRanges(run.Text, font, rect, sf);
                            if (regions.Length > 0)
                                x += regions[0].GetBounds(g).Width;
                        }
                    }
                    return x;
                }
                remaining -= runLen;
                x += run.Bounds.Width;
            }
            return x;
        }

        private int GetCharOffsetFromX(LayoutLine line, float x)
        {
            if (line.LayoutRuns.Count == 0) return 0;

            float runStartX = line.LayoutRuns[0].Bounds.X;
            int globalOffset = 0;

            foreach (var run in line.LayoutRuns)
            {
                float runEndX = runStartX + run.Bounds.Width;
                int runLen = run.Text.Length;

                if (x <= runStartX)
                    return globalOffset;

                if (x < runEndX)
                {
                    using (Graphics g = this.CreateGraphics())
                    using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
                    {
                        sf.FormatFlags |= StringFormatFlags.NoClip;
                        RectangleF rect = new RectangleF(0, 0, 99999, 99999);

                        float localX = x - runStartX;
                        int charInRun = GetCharOffsetInRun(g, run, localX, sf, rect);
                        return globalOffset + charInRun;
                    }
                }

                globalOffset += runLen;
                runStartX = runEndX;
            }

            return globalOffset;
        }

        private int GetCharOffsetInRun(Graphics g, LayoutRun run, float localX, StringFormat sf, RectangleF rect)
        {
            string text = run.Text;
            if (string.IsNullOrEmpty(text) || localX <= 0) return 0;

            using (Font font = new Font(run.StyleSource.FontName, run.StyleSource.FontSize, run.StyleSource.FontStyle))
            {
                int low = 0, high = text.Length;
                while (low < high)
                {
                    int mid = (low + high + 1) / 2;
                    sf.SetMeasurableCharacterRanges(new[] { new CharacterRange(0, mid) });
                    Region[] regions = g.MeasureCharacterRanges(text, font, rect, sf);
                    float w = regions.Length > 0 ? regions[0].GetBounds(g).Width : 0;

                    if (w <= localX)
                        low = mid;
                    else
                        high = mid - 1;
                }

                if (low == text.Length) return low;

                sf.SetMeasurableCharacterRanges(new[] { new CharacterRange(0, low) });
                Region[] r1 = g.MeasureCharacterRanges(text, font, rect, sf);
                float leftEdge = r1.Length > 0 ? r1[0].GetBounds(g).Width : 0;

                sf.SetMeasurableCharacterRanges(new[] { new CharacterRange(0, low + 1) });
                Region[] r2 = g.MeasureCharacterRanges(text, font, rect, sf);
                float rightEdge = r2.Length > 0 ? r2[0].GetBounds(g).Width : 0;

                float midPoint = (leftEdge + rightEdge) / 2f;
                return localX < midPoint ? low : low + 1;
            }
        }

        private float GetPageLeft()
        {
            return Math.Max(30, (this.ClientSize.Width - LayoutPage.A4Dimension.Width) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;

            g.TranslateTransform(this.AutoScrollPosition.X, this.AutoScrollPosition.Y);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            float renderOffsetX = GetPageLeft();

            foreach (var page in _pages)
            {
                RectangleF paperRect = new RectangleF(
                    renderOffsetX, page.PageBounds.Y,
                    page.PageBounds.Width, page.PageBounds.Height);

                g.FillRectangle(Brushes.White, paperRect);

                foreach (var line in page.Lines)
                {
                    foreach (var run in line.LayoutRuns)
                    {
                        using (Font font = new Font(run.StyleSource.FontName, run.StyleSource.FontSize, run.StyleSource.FontStyle))
                        using (Brush brush = new SolidBrush(run.StyleSource.ForeColor))
                        using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
                        {
                            float targetX = run.Bounds.X + renderOffsetX;
                            g.DrawString(run.Text, font, brush, targetX, run.Bounds.Y, sf);
                        }
                    }
                }

                if (_caret != null && _cursorVisible && this.Focused &&
                    TryGetCaretLayoutPosition(out var caretLine, out var caretOffset))
                {
                    bool lineOnThisPage = false;
                    foreach (var l in page.Lines)
                    {
                        if (l == caretLine) { lineOnThisPage = true; break; }
                    }

                    if (lineOnThisPage)
                    {
                        float cx = GetCursorXInPage(caretLine, caretOffset) + renderOffsetX;
                        float cy = caretLine.Bounds.Y;
                        float ch = caretLine.Bounds.Height;
                        using (Pen pen = new Pen(Color.Black, 1.5f))
                            g.DrawLine(pen, cx, cy, cx, cy + ch);
                    }
                }

                string footerText = $"Page {page.PageNumber}";
                using (Font footerFont = new Font("Segoe UI", 9))
                {
                    SizeF size = g.MeasureString(footerText, footerFont);
                    g.DrawString(footerText, footerFont, Brushes.Gray,
                        paperRect.X + (paperRect.Width / 2) - (size.Width / 2),
                        paperRect.Bottom - 35);
                }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            this.Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _cursorBlinkTimer?.Dispose();
            base.Dispose(disposing);
        }
    }
}
