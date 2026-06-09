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

        private int _cursorCharOffset;
        private LayoutLine? _cursorLine;
        private bool _cursorVisible;
        private System.Windows.Forms.Timer _cursorBlinkTimer;

        private TextRun? _caretRun;
        private int _caretRunOffset;


        public Document? Document
        {
            get => _document;
            set { _document = value; RefreshLayout(); }
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
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                    return true;
            }
            return base.IsInputKey(keyData);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            var key = keyData & Keys.KeyCode;
            switch (key)
            {
                case Keys.Left:
                    MoveCursorLeft();
                    return true;
                case Keys.Right:
                    MoveCursorRight();
                    return true;
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                    return false;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);

            if (_cursorLine == null)
                return;

            if (char.IsControl(e.KeyChar))
                return;

            InsertCharacter(e.KeyChar);

            e.Handled = true;
        }

        private bool TryFindRunAtCaret(
    out LayoutRun layoutRun,
    out int sourceOffset)
        {
            layoutRun = null!;
            sourceOffset = 0;

            if (_cursorLine == null)
                return false;

            int remaining = _cursorCharOffset;

            foreach (var run in _cursorLine.LayoutRuns)
            {
                if (remaining <= run.Text.Length)
                {
                    layoutRun = run;

                    sourceOffset =
                        run.SourceStartOffset +
                        remaining;

                    return true;
                }

                remaining -= run.Text.Length;
            }

            return false;
        }

        private void InsertCharacter(char ch)
        {
            if (!TryFindRunAtCaret(
                out LayoutRun layoutRun,
                out int sourceOffset))
                return;

            var sourceRun = layoutRun.StyleSource;

            sourceRun.Text =
                sourceRun.Text.Insert(
                    sourceOffset,
                    ch.ToString());

            _caretRun = sourceRun;
            _caretRunOffset = sourceOffset + 1;

            RefreshLayout();
        }

        private void DeleteBackward()
        {
            if (!TryFindRunAtCaret(
                out LayoutRun layoutRun,
                out int sourceOffset))
                return;

            if (sourceOffset <= 0)
                return;

            var sourceRun = layoutRun.StyleSource;

            sourceRun.Text =
                sourceRun.Text.Remove(
                    sourceOffset - 1,
                    1);

            _caretRun = sourceRun;
            _caretRunOffset = sourceOffset - 1;

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

            if (_caretRun != null)
            {
                RestoreCaret();
            }
            else if (_pages.Count > 0 && _pages[0].Lines.Count > 0)
            {
                _cursorLine = _pages[0].Lines[0];
                _cursorCharOffset = 0;
            }

            this.Invalidate();
        }

        private void RestoreCaret()
        {
            if (_caretRun == null) return;

            foreach (var page in _pages)
            {
                foreach (var line in page.Lines)
                {
                    int globalOffset = 0;

                    foreach (var run in line.LayoutRuns)
                    {
                        if (run.StyleSource == _caretRun)
                        {
                            int localOffset =
                                _caretRunOffset -
                                run.SourceStartOffset;

                            if (localOffset >= 0 &&
                                localOffset <= run.Text.Length)
                            {
                                _cursorLine = line;
                                _cursorCharOffset =
                                    globalOffset + localOffset;

                                return;
                            }
                        }

                        globalOffset += run.Text.Length;
                    }
                }
            }
        }

        private bool IsLineStillValid(LayoutLine line)
        {
            foreach (var page in _pages)
            {
                if (page.Lines.Contains(line))
                    return true;
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

            var result = HitTest(canvasPt);
            if (result.line != null)
            {
                _cursorLine = result.line;
                _cursorCharOffset = result.charOffset;
                if (TryFindRunAtCaret(
    out LayoutRun run,
    out int sourceOffset))
                {
                    _caretRun = run.StyleSource;
                    _caretRunOffset = sourceOffset;
                }

                _cursorVisible = true;
                this.Invalidate();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (_cursorLine == null && _pages.Count > 0 && _pages[0].Lines.Count > 0)
            {
                _cursorLine = _pages[0].Lines[0];
                _cursorCharOffset = 0;
                e.Handled = true;
                this.Invalidate();
                return;
            }
            if (_cursorLine == null) return;

            switch (e.KeyCode)
            {
                case Keys.Up:
                    MoveVertical(-1);
                    e.Handled = true;
                    break;
                case Keys.Down:
                    MoveVertical(1);
                    e.Handled = true;
                    break;
                case Keys.Home:
                    _cursorCharOffset = 0;
                    e.Handled = true;
                    break;
                case Keys.End:
                    _cursorCharOffset = GetLineCharCount(_cursorLine);
                    e.Handled = true;
                    break;
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

        private void MoveCursorLeft()
        {
            if (_cursorLine == null)
            {
                if (_pages.Count > 0 && _pages[0].Lines.Count > 0)
                {
                    _cursorLine = _pages[0].Lines[0];
                    _cursorCharOffset = 0;
                    _cursorVisible = true;
                    this.Invalidate();
                }
                return;
            }

            if (_cursorCharOffset > 0)
                _cursorCharOffset--;
            else
                MoveToPreviousLine();

            _cursorVisible = true;
            this.Invalidate();
        }

        private void MoveCursorRight()
        {
            if (_cursorLine == null)
            {
                if (_pages.Count > 0 && _pages[0].Lines.Count > 0)
                {
                    _cursorLine = _pages[0].Lines[0];
                    _cursorCharOffset = 0;
                    _cursorVisible = true;
                    this.Invalidate();
                }
                return;
            }

            if (_cursorCharOffset < GetLineCharCount(_cursorLine))
                _cursorCharOffset++;
            else
                MoveToNextLine();

            _cursorVisible = true;
            this.Invalidate();
        }

        private int GetLineCharCount(LayoutLine line)
        {
            int count = 0;
            foreach (var run in line.LayoutRuns)
                count += run.Text.Length;
            return count;
        }

        private void MoveToPreviousLine()
        {
            var prev = GetAdjacentLine(-1);
            if (prev != null)
            {
                _cursorLine = prev;
                _cursorCharOffset = GetLineCharCount(prev);
            }
        }

        private void MoveToNextLine()
        {
            var next = GetAdjacentLine(1);
            if (next != null)
            {
                _cursorLine = next;
                _cursorCharOffset = 0;
            }
        }

        private LayoutLine? GetAdjacentLine(int direction)
        {
            for (int pi = 0; pi < _pages.Count; pi++)
            {
                var page = _pages[pi];
                for (int li = 0; li < page.Lines.Count; li++)
                {
                    if (page.Lines[li] == _cursorLine)
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
                                    ? targetPage.Lines[targetPage.Lines.Count - 1]
                                    : targetPage.Lines[0];
                        }
                        return null;
                    }
                }
            }
            return null;
        }

        private void MoveVertical(int direction)
        {
            if (_cursorLine == null) return;
            float targetX = GetCursorXInPage(_cursorLine, _cursorCharOffset);

            var adj = GetAdjacentLine(direction);
            if (adj != null)
            {
                _cursorLine = adj;
                _cursorCharOffset = GetCharOffsetFromX(adj, targetX);
            }
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
                            sf.FormatFlags |= StringFormatFlags.NoClip | StringFormatFlags.MeasureTrailingSpaces;
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

        private (LayoutLine? line, int charOffset) HitTest(PointF canvasPt)
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
                        {
                            int charOffset = GetCharOffsetFromX(line, clickX);
                            return (line, charOffset);
                        }
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
                        int co = GetCharOffsetFromX(closest, clickX);
                        return (closest, co);
                    }
                }
            }
            return ((LayoutLine?)null, 0);
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
                        sf.FormatFlags |= StringFormatFlags.NoClip | StringFormatFlags.MeasureTrailingSpaces;
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

                //g.FillRectangle(Brushes.Black, paperRect.X + 5, paperRect.Y + 5, paperRect.Width, paperRect.Height);
                g.FillRectangle(Brushes.White, paperRect);
                //g.DrawRectangle(Pens.DimGray, paperRect.X, paperRect.Y, paperRect.Width, paperRect.Height);

                foreach (var line in page.Lines)
                {
                    foreach (var run in line.LayoutRuns)
                    {
                        using (Font font = new Font(run.StyleSource.FontName, run.StyleSource.FontSize, run.StyleSource.FontStyle))
                        using (Brush brush = new SolidBrush(run.StyleSource.ForeColor))
                        using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
                        {
                            sf.FormatFlags |=
    StringFormatFlags.MeasureTrailingSpaces;
                            float targetX = run.Bounds.X + renderOffsetX;
                            g.DrawString(run.Text, font, brush, targetX, run.Bounds.Y, sf);
                        }
                    }
                }

                if (_cursorLine != null && _cursorVisible && this.Focused &&
                    page.Lines.Contains(_cursorLine))
                {
                    float cx = GetCursorXInPage(_cursorLine, _cursorCharOffset) + renderOffsetX;
                    float cy = _cursorLine.Bounds.Y;
                    float ch = _cursorLine.Bounds.Height;
                    using (Pen pen = new Pen(Color.Black, 1.5f))
                        g.DrawLine(pen, cx, cy, cx, cy + ch);
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