using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Diagnostics;

namespace AbiCsEngine
{
    public class RenderControl : UserControl
    {

        private struct CaretInfo
        {
            public LayoutLine Line;
            public int OffsetInLine;

            public float X;
            public float Y;
            public float Height;
        }

        private Document? _document;
        private List<LayoutPage> _pages = new List<LayoutPage>();
        private GdiLayout _engine = new GdiLayout();

        private int _cursorCharOffset;
        private LayoutLine? _cursorLine;
        private bool _cursorVisible;
        private System.Windows.Forms.Timer _cursorBlinkTimer;

        private TextRun? _caretRun;
        private int _caretRunOffset;

        private int _docPosition;


        private bool TryGetCaretInfo(
    int docPosition,
    out CaretInfo caret)
        {
            caret = default;

            foreach (var page in _pages)
            {
                foreach (var line in page.Lines)
                {
                    if (docPosition >= line.StartDocPosition &&
                        docPosition < line.EndDocPosition)
                    {
                        int offsetInLine =
                            docPosition - line.StartDocPosition;

                        float x =
                            GetCursorXInPage(line, offsetInLine);

                        caret = new CaretInfo
                        {
                            Line = line,
                            OffsetInLine = offsetInLine,
                            X = x,
                            Y = line.Bounds.Y,
                            Height = line.Bounds.Height
                        };

                        return true;
                    }
                }
            }

            return false;
        }

        private int GetDocPositionFromCaret(
    LayoutLine targetLine,
    int targetOffset)
        {
            return targetLine.StartDocPosition + targetOffset;
        }

        private void DumpRunInfo(
    LayoutLine line,
    int charOffset)
        {
            int remaining = charOffset;

            Debug.WriteLine(
                $"LINE OFFSET={charOffset}");

            foreach (var run in line.LayoutRuns)
            {
                Debug.WriteLine(
                    $"RUN '{run.Text}' LEN={run.Text.Length}");

                if (remaining <= run.Text.Length)
                {
                    Debug.WriteLine(
                        $"--> CARET IN RUN '{run.Text}' LOCAL={remaining}");

                    return;
                }

                remaining -= run.Text.Length;
            }
        }


        private void SyncCursorFromDocPosition()
        {
            if (TryGetCaretInfo(
    _docPosition,
    out var caret))
            {
                _cursorLine = caret.Line;
                _cursorCharOffset = caret.OffsetInLine;
            }
        }


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
                case Keys.Enter:
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
                    MoveDocPositionLeft();
                    return true;
                case Keys.Right:
                    MoveDocPositionRight();
                    return true;
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                case Keys.Enter:
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

        private bool TryFindRunPosition(
    int docPosition,
    out RunPosition pos)
        {
            pos = null!;

            if (_document == null)
                return false;

            int currentPos = 0;

            foreach (var paragraph in _document.Paragraphs)
            {
                for (int runIndex = 0;
                     runIndex < paragraph.Runs.Count;
                     runIndex++)
                {
                    var run = paragraph.Runs[runIndex];
                    int runLen = run.Length;

                    if (docPosition < currentPos + runLen)
                    {
                        pos = new RunPosition
                        {
                            Paragraph = paragraph,
                            Run = run,
                            RunIndex = runIndex,
                            OffsetInRun = docPosition - currentPos
                        };
                        return true;
                    }

                    if (docPosition == currentPos + runLen)
                    {
                        if (runIndex + 1 < paragraph.Runs.Count)
                        {
                            pos = new RunPosition
                            {
                                Paragraph = paragraph,
                                Run = paragraph.Runs[runIndex + 1],
                                RunIndex = runIndex + 1,
                                OffsetInRun = 0
                            };
                            return true;
                        }

                        pos = new RunPosition
                        {
                            Paragraph = paragraph,
                            Run = run,
                            RunIndex = runIndex,
                            OffsetInRun = runLen
                        };
                        return true;
                    }

                    currentPos += runLen;
                }

                if (docPosition == currentPos)
                {
                    if (paragraph.Runs.Count > 0)
                    {
                        var lastRun = paragraph.Runs[paragraph.Runs.Count - 1];
                        pos = new RunPosition
                        {
                            Paragraph = paragraph,
                            Run = lastRun,
                            RunIndex = paragraph.Runs.Count - 1,
                            OffsetInRun = lastRun.Length
                        };
                        return true;
                    }
                }

                currentPos += 1; // EOP
            }

            return false;
        }

        private void DumpRunPosition(int docPosition)
        {
            if (TryFindRunPosition(docPosition, out var pos))
            {
                Debug.WriteLine(
                    $"DocPos={docPosition} " +
                    $"RunIndex={pos.RunIndex} " +
                    $"OffsetInRun={pos.OffsetInRun} " +
                    $"Text='{pos.Run.Text}'");
            }
        }



        private void DumpDocPosition()
        {
            if (TryFindRunPosition(
                _docPosition,
                out var pos))
            {
                Debug.WriteLine(
                    $"DocPos={_docPosition} " +
                    $"RunIndex={pos.RunIndex} " +
                    $"Offset={pos.OffsetInRun} " +
                    $"Text='{pos.Run.Text}'");
            }
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
            if (!TryFindRunPosition(
                _docPosition,
                out var pos))
            {
                var emptyPara = FindParagraphAtDocPos(_docPosition);
                if (emptyPara != null)
                {
                    emptyPara.Runs.Add(new TextRun { Text = ch.ToString() });
                    _docPosition++;
                    RefreshLayout();
                    SyncCursorFromDocPosition();
                }
                return;
            }

            pos.Run.Text =
                pos.Run.Text.Insert(
                    pos.OffsetInRun,
                    ch.ToString());

            NormalizeParagraph(
    pos.Paragraph);

            _docPosition++;

            RefreshLayout();

            SyncCursorFromDocPosition();
        }

        private void DeleteBackward()
        {
            if (_docPosition == 0)
                return;

            int deletePos = _docPosition - 1;

            if (!TryFindRunPosition(
                deletePos,
                out var pos))
                return;

            int paraStart = GetParagraphStartDocPos(pos.Paragraph);

            if (deletePos == paraStart + pos.Paragraph.Length - 1)
            {
                int paraIndex = _document.Paragraphs.IndexOf(pos.Paragraph);
                if (paraIndex >= 0 && paraIndex + 1 < _document.Paragraphs.Count)
                {
                    var nextPara = _document.Paragraphs[paraIndex + 1];
                    foreach (var r in nextPara.Runs)
                        pos.Paragraph.Runs.Add(r);
                    _document.Paragraphs.RemoveAt(paraIndex + 1);
                    _docPosition = deletePos;
                    NormalizeParagraph(pos.Paragraph);
                    RefreshLayout();
                    SyncCursorFromDocPosition();
                }
                return;
            }

            pos.Run.Text =
                pos.Run.Text.Remove(
                    pos.OffsetInRun,
                    1);

            _docPosition--;

            NormalizeParagraph(
    pos.Paragraph);

            RefreshLayout();

            SyncCursorFromDocPosition();
        }

        private void RemoveEmptyRun(
    TextRun run,
    Paragraph paragraph)
        {
            if (run.Text.Length == 0)
            {
                paragraph.Runs.Remove(run);
            }
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

        private void DeleteForward()
        {
            if (!TryFindRunPosition(
                _docPosition,
                out var pos))
                return;

            int paraStart = GetParagraphStartDocPos(pos.Paragraph);

            if (_docPosition == paraStart + pos.Paragraph.Length - 1)
            {
                int paraIndex = _document.Paragraphs.IndexOf(pos.Paragraph);
                if (paraIndex >= 0 && paraIndex + 1 < _document.Paragraphs.Count)
                {
                    var nextPara = _document.Paragraphs[paraIndex + 1];
                    foreach (var r in nextPara.Runs)
                        pos.Paragraph.Runs.Add(r);
                    _document.Paragraphs.RemoveAt(paraIndex + 1);
                    NormalizeParagraph(pos.Paragraph);
                    RefreshLayout();
                    SyncCursorFromDocPosition();
                }
                return;
            }

            if (pos.OffsetInRun < pos.Run.Text.Length)
            {
                pos.Run.Text =
                    pos.Run.Text.Remove(
                        pos.OffsetInRun,
                        1);
                NormalizeParagraph(
    pos.Paragraph);
            }
            else
            {
                DeleteAcrossRunBoundary(pos);
            }

            RefreshLayout();

            SyncCursorFromDocPosition();
        }

        private void DeleteAcrossRunBoundary(
    RunPosition pos)
        {
            if (pos.RunIndex + 1 >=
                pos.Paragraph.Runs.Count)
                return;

            var nextRun =
                pos.Paragraph.Runs[
                    pos.RunIndex + 1];

            if (nextRun.Text.Length == 0)
                return;

            nextRun.Text =
                nextRun.Text.Remove(0, 1);

            RemoveEmptyRun(
                nextRun,
                pos.Paragraph);
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

                Debug.WriteLine(
    $"MOUSE Offset={_cursorCharOffset}");

                _docPosition =
     GetDocPositionFromCaret(
         _cursorLine!,
         _cursorCharOffset);

                Debug.WriteLine(
                    $"MOUSE DocPos={_docPosition}");

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
                    MoveVerticalByDocPosition(-1);
                    e.Handled = true;
                    break;
                case Keys.Down:
                    MoveVerticalByDocPosition(1);
                    e.Handled = true;
                    break;
                case Keys.Home:
                    if (_cursorLine != null)
                    {
                        _docPosition = _cursorLine.StartDocPosition;
                        _cursorCharOffset = 0;
                    }
                    e.Handled = true;
                    break;
                case Keys.End:
                    if (_cursorLine != null)
                    {
                        int lineLen = GetLineCharCount(_cursorLine);
                        _docPosition = _cursorLine.StartDocPosition + lineLen;
                        _cursorCharOffset = lineLen;
                    }
                    e.Handled = true;
                    break;
                case Keys.Enter:
                    InsertParagraphBreak();
                    e.Handled = true;
                    break;
                case Keys.F5:
                    MoveDocPositionRight();
                    e.Handled = true;
                    break;
                case Keys.Back:
                    DeleteBackward();
                    e.Handled = true;
                    break;
                case Keys.Delete:
                    DeleteForward();
                    e.Handled = true;
                    break;
                default:
                    return;
            }

            _cursorVisible = true;
            this.Invalidate();
        }

        private void MoveDocPositionRight()
        {
            _docPosition++;

            SyncCursorFromDocPosition();

            Debug.WriteLine("===== BEFORE =====");
            DumpRunInfo(_cursorLine!, _cursorCharOffset);

            ValidateDocPosition();
        }

        private void DumpRuns(
    Paragraph paragraph)
        {
            Debug.WriteLine(
                "----- RUNS -----");

            for (int i = 0;
                 i < paragraph.Runs.Count;
                 i++)
            {
                var run = paragraph.Runs[i];

                Debug.WriteLine(
                    $"[{i}] '{run.Text}' " +
                    $"{run.FontName} " +
                    $"{run.FontSize} " +
                    $"{run.FontStyle}");
            }
        }

        private void MoveDocPositionLeft()
        {
            _docPosition--;

            SyncCursorFromDocPosition();

            Debug.WriteLine("===== BEFORE =====");
            DumpRunInfo(_cursorLine!, _cursorCharOffset);

            ValidateDocPosition();
        }

        private int GetLineCharCount(LayoutLine line)
        {
            int count = 0;
            foreach (var run in line.LayoutRuns)
                count += run.Text.Length;
            return count;
        }

        private LayoutLine? GetAdjacentLine(
    LayoutLine currentLine,
    int direction)
        {
            for (int pi = 0; pi < _pages.Count; pi++)
            {
                var page = _pages[pi];

                for (int li = 0; li < page.Lines.Count; li++)
                {
                    if (page.Lines[li] != currentLine)
                        continue;

                    int targetLi = li + direction;

                    if (targetLi >= 0 &&
                        targetLi < page.Lines.Count)
                    {
                        return page.Lines[targetLi];
                    }

                    int targetPi = pi + direction;

                    if (targetPi >= 0 &&
                        targetPi < _pages.Count)
                    {
                        var targetPage =
                            _pages[targetPi];

                        if (targetPage.Lines.Count > 0)
                        {
                            return direction < 0
                                ? targetPage.Lines[
                                    targetPage.Lines.Count - 1]
                                : targetPage.Lines[0];
                        }
                    }

                    return null;
                }
            }

            return null;
        }

        private void MoveVerticalByDocPosition(
    int direction)
        {
            if (!TryGetCaretInfo(
                _docPosition,
                out var caret))
            {
                return;
            }

            LayoutLine? targetLine =
                GetAdjacentLine(
                    caret.Line,
                    direction);

            if (targetLine == null)
                return;

            int targetOffset =
                GetCharOffsetFromX(
                    targetLine,
                    caret.X);

            _docPosition =
                GetDocPositionFromCaret(
                    targetLine,
                    targetOffset);

            SyncCursorFromDocPosition();

            _cursorVisible = true;

            Invalidate();
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

                        if (canvasPt.Y >= lineTop && canvasPt.Y <= lineBottom)
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

        private void ValidateDocPosition()
        {
            int docPos = GetDocPositionFromCaret(
        _cursorLine!,
        _cursorCharOffset);

            if (TryGetCaretInfo(
    _docPosition,
    out var caret))
            {
                Debug.WriteLine(
                    $"DocPos={_docPosition} " +
                    $"Offset={caret.OffsetInLine}");
            }

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
                    float cx =
                        GetCursorXInPage(_cursorLine, _cursorCharOffset) + renderOffsetX;

                    using (Pen pen =
                        new Pen(Color.Black, 1.5f))
                    {
                        g.DrawLine(
                            pen,
                            cx,
                            _cursorLine.Bounds.Y,
                            cx,
                            _cursorLine.Bounds.Y + _cursorLine.Height);
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

        private bool IsSameStyle(
    TextRun a,
    TextRun b)
        {
            return
                a.FontName == b.FontName &&
                a.FontSize == b.FontSize &&
                a.FontStyle == b.FontStyle &&
                a.ForeColor == b.ForeColor;
        }

        private void MergeAdjacentRuns(
    Paragraph paragraph)
        {
            int i = 0;

            while (i < paragraph.Runs.Count - 1)
            {
                var current =
                    paragraph.Runs[i];

                var next =
                    paragraph.Runs[i + 1];

                if (IsSameStyle(
                    current,
                    next))
                {
                    current.Text += next.Text;

                    paragraph.Runs.RemoveAt(
                        i + 1);

                    continue;
                }

                i++;
            }
        }

        private int GetParagraphStartDocPos(Paragraph target)
        {
            int pos = 0;
            foreach (var para in _document.Paragraphs)
            {
                if (para == target) return pos;
                pos += para.Length;
            }
            return -1;
        }

        private Paragraph? FindParagraphAtDocPos(int docPos)
        {
            int currentPos = 0;
            foreach (var para in _document.Paragraphs)
            {
                if (docPos >= currentPos && docPos < currentPos + para.Length)
                    return para;
                currentPos += para.Length;
            }
            return null;
        }

        private void InsertParagraphBreak()
        {
            if (!TryFindRunPosition(_docPosition, out var pos))
                return;

            Paragraph current = pos.Paragraph;
            Paragraph newPara = new Paragraph();
            int splitRunIndex = pos.RunIndex;

            if (pos.OffsetInRun == 0)
            {
                for (int i = splitRunIndex; i < current.Runs.Count; i++)
                    newPara.Runs.Add(current.Runs[i]);
                current.Runs.RemoveRange(splitRunIndex, current.Runs.Count - splitRunIndex);
            }
            else if (pos.OffsetInRun < pos.Run.Text.Length)
            {
                TextRun run = current.Runs[splitRunIndex];
                string rightText = run.Text.Substring(pos.OffsetInRun);
                run.Text = run.Text.Substring(0, pos.OffsetInRun);

                TextRun newRun = new TextRun
                {
                    Text = rightText,
                    FontName = run.FontName,
                    FontSize = run.FontSize,
                    FontStyle = run.FontStyle,
                    ForeColor = run.ForeColor
                };
                newPara.Runs.Add(newRun);

                for (int i = splitRunIndex + 1; i < current.Runs.Count; i++)
                    newPara.Runs.Add(current.Runs[i]);
                current.Runs.RemoveRange(splitRunIndex + 1, current.Runs.Count - splitRunIndex - 1);
            }
            else
            {
                for (int i = splitRunIndex + 1; i < current.Runs.Count; i++)
                    newPara.Runs.Add(current.Runs[i]);
                current.Runs.RemoveRange(splitRunIndex + 1, current.Runs.Count - splitRunIndex - 1);
            }

            current.Runs.Add(new EopRun());

            int paraIndex = _document.Paragraphs.IndexOf(current);
            _document.Paragraphs.Insert(paraIndex + 1, newPara);

            _docPosition++;
            RefreshLayout();
            SyncCursorFromDocPosition();
        }

        private void NormalizeParagraph(
    Paragraph paragraph)
        {
            for (int i = paragraph.Runs.Count - 1;
                 i >= 0;
                 i--)
            {
                if (paragraph.Runs[i].Length == 0)
                {
                    paragraph.Runs.RemoveAt(i);
                }
            }

            MergeAdjacentRuns(paragraph);

        }



    }
}