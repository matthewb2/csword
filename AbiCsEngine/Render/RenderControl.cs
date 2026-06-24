using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.ConstrainedExecution;
using System.Windows.Forms;

namespace AbiCsEngine
{
    public class RenderControl : UserControl
    {


        private Document? _document;
        private List<LayoutPage> _pages = new List<LayoutPage>();
        private GdiLayout _engine = new GdiLayout();

        private bool _cursorVisible;
        private System.Windows.Forms.Timer _cursorBlinkTimer;

        private TextRun? _caretRun;
        private int _caretRunOffset;

        private readonly List<LayoutLine> _allLines = new();
        private int _caretLineIndex;
        private int _docPosition = -1;
        private int DocPosRaw => _docPosition + 1;

        private void RebuildAllLines()
        {
            _allLines.Clear();

            foreach (var page in _pages)
            {
                _allLines.AddRange(page.Lines);
            }

            DumpAllLines();

        }

        private void DumpAllLines()
        {
            Debug.WriteLine("===== _allLines =====");

            for (int i = 0; i < _allLines.Count; i++)
            {
                var line = _allLines[i];

                Debug.WriteLine(
                    $"Line[{i}] Start={line.StartDocPosition} " +
                    $"End={line.EndDocPosition} " +
                    $"Runs={line.LayoutRuns.Count}");

                for (int ri = 0; ri < line.LayoutRuns.Count; ri++)
                {
                    var run = line.LayoutRuns[ri];

                    Debug.WriteLine(
                        $"  Run[{ri}] Text='{run.Text}' " +
                        $"Width={run.Bounds.Width} " +
                        $"Source={run.StyleSource.GetType().Name}");
                }
            }

            Debug.WriteLine("=====================");
        }

        private static int GetRunLogicalLength(
    LayoutRun run)
        {
            return run.StyleSource is EopRun
                ? 1
                : run.Text.Length;
        }


        private bool TryGetCaretInfo(out float x, out float y, out float height)
        {
            x = 0;
            y = 0;
            height = 0;

            if (_caretLineIndex < 0 ||
                _caretLineIndex >= _allLines.Count)
                return false;

            LayoutLine line = _allLines[_caretLineIndex];

            int offsetInLine =
                DocPosRaw - line.StartDocPosition;

            int lineLength =
    GetLineCharCount(line);

            offsetInLine = Math.Max(
                0,
                Math.Min(offsetInLine, lineLength));

            x = GetCursorXInPage(line, offsetInLine);
            y = line.Bounds.Top;
            height = line.Bounds.Height;

            return true;
        }

        private int GetDocPositionFromCaret(
    LayoutLine targetLine,
    int targetOffset)
        {
            return targetLine.StartDocPosition + targetOffset - 1;
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

                    int runLen =
                        run is EopRun
                            ? 1
                            : run.Text.Length;

                    // 현재 Run 내부
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

                    currentPos += runLen;
                }
            }

            return false;
        }


        private void InsertCharacter(char ch)
        {
            if (!TryFindRunPosition(
                DocPosRaw,
                out var pos))
            {
                var emptyPara = FindParagraphAtDocPos(DocPosRaw);
                if (emptyPara != null)
                {
                    emptyPara.Runs.Add(new TextRun { Text = ch.ToString() });
                    _docPosition++;
                    RefreshLayout();
                    UpdateCaretLine();
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

            UpdateCaretLine();
        }

        private void DeleteBackward()
        {
            if (_document == null)
                return;

            if (_docPosition == -1)
                return;

            int deletePos = DocPosRaw - 1;

            if (!TryFindRunPosition(
                deletePos,
                out var pos))
                return;

            // =========================
            // EOP 삭제 → 문단 병합
            // =========================
            if (pos.Run is EopRun)
            {
                int paraIndex =
                    _document.Paragraphs.IndexOf(
                        pos.Paragraph);

                if (paraIndex < 0)
                    return;

                if (paraIndex + 1 >=
                    _document.Paragraphs.Count)
                    return;

                Paragraph nextPara =
                    _document.Paragraphs[
                        paraIndex + 1];

                // 현재 문단의 EOP 제거
                pos.Paragraph.Runs.RemoveAt(
                    pos.RunIndex);

                // 다음 문단 내용 이동
                foreach (var run in nextPara.Runs)
                {
                    pos.Paragraph.Runs.Add(run);
                }

                _document.Paragraphs.RemoveAt(
                    paraIndex + 1);

                NormalizeParagraph(
                    pos.Paragraph);

                _docPosition--;

                RefreshLayout();
                UpdateCaretLine();

                return;
            }

            // =========================
            // 일반 문자 삭제
            // =========================

            if (pos.Run is TextRun textRun)
            {
                textRun.Text =
                    textRun.Text.Remove(
                        pos.OffsetInRun,
                        1);

                RemoveEmptyRun(
                    textRun,
                    pos.Paragraph);

                NormalizeParagraph(
                    pos.Paragraph);

                _docPosition--;

                RefreshLayout();
                UpdateCaretLine();
            }
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



            RebuildAllLines();
            this.Invalidate();
        }

        private void DeleteForward()
        {
            if (_document == null)
                return;

            if (_docPosition == -1)
                return;

            if (!TryFindRunPosition(
                DocPosRaw,
                out var pos))
                return;

            // =========================
            // EOP 삭제 → 문단 병합
            // =========================
            if (pos.Run is EopRun)
            {
                int paraIndex =
                    _document.Paragraphs.IndexOf(
                        pos.Paragraph);

                if (paraIndex < 0)
                    return;

                if (paraIndex + 1 >=
                    _document.Paragraphs.Count)
                    return;

                Paragraph nextPara =
                    _document.Paragraphs[
                        paraIndex + 1];

                pos.Paragraph.Runs.RemoveAt(
                    pos.RunIndex);

                foreach (var run in nextPara.Runs)
                {
                    pos.Paragraph.Runs.Add(run);
                }

                _document.Paragraphs.RemoveAt(
                    paraIndex + 1);

                NormalizeParagraph(
                    pos.Paragraph);

                RefreshLayout();
                UpdateCaretLine();

                return;
            }

            // =========================
            // 일반 문자 삭제
            // =========================

            if (pos.Run is TextRun textRun)
            {
                if (pos.OffsetInRun >=
                    textRun.Text.Length)
                {
                    return;
                }

                textRun.Text =
                    textRun.Text.Remove(
                        pos.OffsetInRun,
                        1);

                RemoveEmptyRun(
                    textRun,
                    pos.Paragraph);

                NormalizeParagraph(
                    pos.Paragraph);

                RefreshLayout();
                UpdateCaretLine();
            }
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
                _docPosition =
     GetDocPositionFromCaret(
         result.line!,
         result.charOffset);

                UpdateCaretLine();

                Debug.WriteLine(
                    $"MOUSE DocPos={_docPosition}");

                this.Invalidate();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            // 초기 커서 위치 보정
            if (_allLines.Count > 0 && _caretLineIndex < 0)
            {
                _caretLineIndex = 0;
                e.Handled = true;
                Invalidate();
                return;
            }

            if (_allLines.Count == 0)
                return;

            if (_caretLineIndex < 0 || _caretLineIndex >= _allLines.Count)
                return;

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
                    {
                        LayoutLine line = _allLines[_caretLineIndex];
                        _docPosition = line.StartDocPosition;
                        e.Handled = true;
                        break;
                    }

                case Keys.End:
                    {
                        LayoutLine line = _allLines[_caretLineIndex];
                        int lineLen = GetLineCharCount(line);
                        _docPosition = line.StartDocPosition + lineLen;
                        e.Handled = true;
                        break;
                    }

                case Keys.Enter:
                    InsertParagraphBreak();
                    Debug.WriteLine(
                        $"After Enter DocPos={_docPosition}");
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
            Invalidate();
        }

        private void MoveDocPositionRight()
        {
            _docPosition++;
            Debug.WriteLine($"docPosition: {_docPosition}");

            UpdateCaretLine();

            
        }

        private void MoveDocPositionLeft()
        {
            _docPosition--;

            UpdateCaretLine();

        }

        private int GetLineCharCount(LayoutLine line)
        {
            int count = 0;

            foreach (var run in line.LayoutRuns)
                count += GetRunLogicalLength(run);

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
                out float caretX,
                out _,
                out _))
            {
                return;
            }

            if (_caretLineIndex < 0 ||
                _caretLineIndex >= _allLines.Count)
                return;

            LayoutLine? targetLine =
                GetAdjacentLine(
                    _allLines[_caretLineIndex],
                    direction);

            if (targetLine == null)
                return;

            int targetOffset =
                GetCharOffsetFromX(
                    targetLine,
                    caretX);

            _docPosition =
                GetDocPositionFromCaret(
                    targetLine,
                    targetOffset);

            int targetIndex = _allLines.IndexOf(targetLine);
            if (targetIndex >= 0)
                _caretLineIndex = targetIndex;
                        
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
                int runLen =
    GetRunLogicalLength(run);
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
                if (remaining == 0)
                    return x;
                if (run.StyleSource is EopRun)
                {
                    return x + run.Bounds.Width;
                }
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
                int runLen =
    GetRunLogicalLength(run);

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
            // 라인 오른쪽 여백을 클릭했을 때 텍스트 끝으로 이동
            if (line.LayoutRuns.Count > 0)
            {
                LayoutRun lastRun = line.LayoutRuns[^1];

                if (lastRun.StyleSource is EopRun)
                {
                    return Math.Max(0, globalOffset - 1);
                }
            }

            return globalOffset;
        }


        private int GetCharOffsetInRun(Graphics g, LayoutRun run, float localX, StringFormat sf, RectangleF rect)
        {
            string text = run.Text;

            if (run.StyleSource is EopRun)
            {
                return localX <= 0 ? 0 : 1;
            }

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

        float GetLineTop(List<LayoutLine> lines, int index)
        {
            if (index == 0)
                return lines[0].Bounds.Y;

            return lines[index - 1].Bounds.Bottom;
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

                for (int i = 0; i < page.Lines.Count; i++)
                {
                    var line = page.Lines[i];
                    
                    float x = renderOffsetX + page.PrintableArea.Left;
                    float y = GetLineTop(page.Lines, i); // ⭐ 핵심
                    float lineHeight = Math.Max(line.Bounds.Height, 18f);


                    foreach (var run in line.LayoutRuns)
                    {
                        if (run.StyleSource is EopRun)
                            continue;

                        using (Font font = new Font(
                            run.StyleSource.FontName,
                            run.StyleSource.FontSize,
                            run.StyleSource.FontStyle))
                        using (Brush brush =
                            new SolidBrush(run.StyleSource.ForeColor))
                        using (StringFormat sf =
                            new StringFormat(
                                StringFormat.GenericTypographic))
                        {
                            sf.FormatFlags |=
                                StringFormatFlags.MeasureTrailingSpaces;

                            float targetX =
                                run.Bounds.X + renderOffsetX;

                            g.DrawString(
                                run.Text,
                                font,
                                brush,
                                targetX,
                                run.Bounds.Y,
                                sf);
                        }
                    }


                    // =========================
                    // 문단 부호 (EOP 표시)
                    // =========================

                    bool showPilcrow =
    line.LayoutRuns.Exists(r => r.StyleSource is EopRun);

                    if (showPilcrow)
                    {
                        LayoutRun fontRun = line.LayoutRuns[^1];

                        for (int ri = line.LayoutRuns.Count - 1; ri >= 0; ri--)
                        {
                            if (line.LayoutRuns[ri].StyleSource is not EopRun)
                            {
                                fontRun = line.LayoutRuns[ri];
                                break;
                            }
                        }

                        using (Font pilcrowFont = new Font(
                            fontRun.StyleSource.FontName,
                            fontRun.StyleSource.FontSize,
                            fontRun.StyleSource.FontStyle))
                        {
                            float pilcrowX =
                                GetCursorXInPage(line, GetLineCharCount(line)) + renderOffsetX;

                            g.DrawString(
                                "\u00B6",
                                pilcrowFont,
                                Brushes.LightGray,
                                pilcrowX,
                                line.Bounds.Y);
                        }
                    }
                }

                // =========================
                // 4. 커서
                // =========================
                if (_cursorVisible && Focused)
                {
                    if (TryGetCaretInfo(out float cx,
                                        out float cy,
                                        out float ch))
                    {
                        using var pen = new Pen(Color.Black, 1);

                        g.DrawLine(
                            pen,
                            cx + renderOffsetX,
                            cy,
                            cx + renderOffsetX,
                            cy + ch);
                    }
                }

                // =========================
                // 5. 페이지 번호
                // =========================
                string footerText = $"Page {page.PageNumber}";
                using (Font footerFont = new Font("Segoe UI", 9))
                {
                    SizeF size = g.MeasureString(footerText, footerFont);

                    g.DrawString(
                        footerText,
                        footerFont,
                        Brushes.Gray,
                        paperRect.X + (paperRect.Width / 2) - (size.Width / 2),
                        paperRect.Bottom - 35);
                }
            }
        }

        private void UpdateCaretLine()
        {
            if (_allLines.Count == 0)
            {
                _caretLineIndex = -1;
                return;
            }

            if (_docPosition == -1)
            {
                _caretLineIndex = 0;
                return;
            }


            var cur = _allLines[_caretLineIndex];
                        


            // 현재 라인 유지 시도
            if (_caretLineIndex >= 0 &&
                _caretLineIndex < _allLines.Count)
            {
                var current = _allLines[_caretLineIndex];

                Debug.WriteLine(
   $"start='{current.StartDocPosition}' " +
   $"end={current.EndDocPosition} " +
   $"OffsetInRun=");


                if (_docPosition >= current.StartDocPosition &&
                    _docPosition < current.EndDocPosition)
                {
                    return;
                }
            }

            // 전체 탐색
            for (int i = 0; i < _allLines.Count; i++)
            {
                var line = _allLines[i];

                if (_docPosition >= line.StartDocPosition &&
                    _docPosition < line.EndDocPosition)
                {
                    _caretLineIndex = i;
                    return;
                }
            }

            if (_allLines.Count > 0 &&
    _docPosition == _allLines[^1].EndDocPosition)
            {
                _caretLineIndex = _allLines.Count - 1;
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
            if (!TryFindRunPosition(
                DocPosRaw,
                out var pos))
            {
                return;
            }

            Paragraph currentPara =
                pos.Paragraph;

            Paragraph newPara =
                new Paragraph();

            int paraIndex =
                _document.Paragraphs.IndexOf(
                    currentPara);
            if (pos.Run is EopRun)
            {
                newPara = new Paragraph();
                NormalizeParagraph(newPara);

                paraIndex =
                    _document.Paragraphs.IndexOf(pos.Paragraph);

                _document.Paragraphs.Insert(
                    paraIndex + 1,
                    newPara);

                _docPosition++;

                RefreshLayout();
                UpdateCaretLine();
                return;
            }


            // 1. 커서 위치 이후 내용을 새 문단으로 이동
            if (pos.Run is TextRun textRun)
            {
                int offset = Math.Max(
    0,
    Math.Min(
        pos.OffsetInRun,
        textRun.Text.Length));

                Debug.WriteLine(
    $"RunText='{textRun.Text}' " +
    $"TextLength={textRun.Text.Length} " +
    $"OffsetInRun={pos.OffsetInRun}");

                string left =
                    textRun.Text.Substring(
                        0,
                        pos.OffsetInRun);

                string right =
                    textRun.Text.Substring(
                        pos.OffsetInRun);

                textRun.Text = left;

                if (textRun.Text.Length == 0)
                {
                    currentPara.Runs.Remove(textRun);
                }


                if (right.Length > 0)
                {
                    newPara.Runs.Add(
                        new TextRun
                        {
                            Text = right,
                            FontName = textRun.FontName,
                            FontSize = textRun.FontSize,
                            FontStyle = textRun.FontStyle,
                            ForeColor = textRun.ForeColor
                        });
                }

                for (int i = pos.RunIndex + 1;
                     i < currentPara.Runs.Count;
                     i++)
                {
                    newPara.Runs.Add(
                        currentPara.Runs[i]);
                }

                currentPara.Runs.RemoveRange(
                    pos.RunIndex + 1,
                    currentPara.Runs.Count -
                    (pos.RunIndex + 1));
            }

            // 2. EOP 보장
            NormalizeParagraph(currentPara);
            NormalizeParagraph(newPara);

            // 3. 새 문단 삽입
            _document.Paragraphs.Insert(
                paraIndex + 1,
                newPara);

            // 4. Enter = EOP 1개 소비
            _docPosition++;

            RefreshLayout();

            UpdateCaretLine();

            Invalidate();
        }

        private void NormalizeParagraph(
    Paragraph paragraph)
        {
            for (int i = paragraph.Runs.Count - 1; i >= 0; i--)
            {
                if (paragraph.Runs[i] is EopRun)
                    continue;

                if (paragraph.Runs[i].Length == 0)
                    paragraph.Runs.RemoveAt(i);
            }

            MergeAdjacentRuns(paragraph);

            bool hasEop =
                paragraph.Runs.Count > 0 &&
                paragraph.Runs[^1] is EopRun;

            if (!hasEop)
                paragraph.Runs.Add(new EopRun());
        }

    }
}