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

            int currentPos = 0;

            foreach (var page in _pages)
            {
                foreach (var line in page.Lines)
                {
                    int lineLength =
                        GetLineCharCount(line);

                    if (docPosition <= currentPos + lineLength)
                    {
                        int offsetInLine =
                            docPosition - currentPos;

                        float x =
                            GetCursorXInPage(
                                line,
                                offsetInLine);

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

                    currentPos += lineLength;
                }
            }

            return false;
        }

        private int GetDocPositionFromCaret(
    LayoutLine targetLine,
    int targetOffset)
        {
            int docPos = 0;

            foreach (var page in _pages)
            {
                foreach (var line in page.Lines)
                {
                    if (line == targetLine)
                    {
                        return docPos + targetOffset;
                    }

                    docPos += GetLineCharCount(line);
                }
            }

            return docPos;
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
                Debug.WriteLine(
                    $"DocPos={_docPosition} " +
                    $"Offset={caret.OffsetInLine}");
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
                    int runLength = run.Text.Length;

                    //
                    // Run 내부
                    //
                    if (docPosition < currentPos + runLength)
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

                    //
                    // Run 끝 위치
                    //
                    if (docPosition == currentPos + runLength)
                    {
                        //
                        // 다음 Run 시작으로 귀속
                        //
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

                        //
                        // 문단 마지막 Run 끝
                        //
                        pos = new RunPosition
                        {
                            Paragraph = paragraph,
                            Run = run,
                            RunIndex = runIndex,
                            OffsetInRun = runLength
                        };

                        return true;
                    }

                    currentPos += runLength;
                }
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
                return;

            pos.Run.Text =
                pos.Run.Text.Insert(
                    pos.OffsetInRun,
                    ch.ToString());

            NormalizeParagraph(
    pos.Paragraph);

            _docPosition++;

            RefreshLayout();

            SyncCursorFromDocPosition();

            DumpDocPosition();
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

            pos.Run.Text =
                pos.Run.Text.Remove(
                    pos.OffsetInRun,
                    1);

            _docPosition--;

            NormalizeParagraph(
    pos.Paragraph);


            RefreshLayout();

            SyncCursorFromDocPosition();

            DumpDocPosition();
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

            //
            // Run 내부 삭제
            //
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

            DumpDocPosition();
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
                    _cursorCharOffset = 0;
                    e.Handled = true;
                    break;
                case Keys.End:
                    _cursorCharOffset = GetLineCharCount(_cursorLine);
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

        private bool ValidateCaretFromDocPosition(
    int docPosition,
    out LayoutLine line,
    out int charOffset)
        {
            line = null!;
            charOffset = 0;

            int currentPos = 0;

            foreach (var page in _pages)
            {
                foreach (var layoutLine in page.Lines)
                {
                    int lineLength = GetLineCharCount(layoutLine);

                    if (docPosition <= currentPos + lineLength)
                    {
                        line = layoutLine;
                        charOffset = docPosition - currentPos;
                        return true;
                    }

                    currentPos += lineLength;
                }
            }

            return false;
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
                    if (TryGetCaretInfo(
    _docPosition,
    out var caret))
                    {
                        float cx =
                            caret.X + renderOffsetX;

                        using (Pen pen =
                            new Pen(Color.Black, 1.5f))
                        {
                            g.DrawLine(
                                pen,
                                cx,
                                caret.Y,
                                cx,
                                caret.Y + caret.Height);
                        }
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

        private void NormalizeParagraph(
    Paragraph paragraph)
        {
            //
            // 빈 Run 제거
            //
            for (int i = paragraph.Runs.Count - 1;
                 i >= 0;
                 i--)
            {
                if (paragraph.Runs[i].Text.Length == 0)
                {
                    paragraph.Runs.RemoveAt(i);
                }
            }

            //
            // 동일 스타일 병합
            //
            MergeAdjacentRuns(paragraph);

            DumpRuns(paragraph);

        }



    }
}