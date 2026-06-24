using System;
using System.Collections.Generic;
using System.Drawing;

namespace AbiCsEngine
{
    public class GdiLayout
    {
        private const float PageGap = 30f; // 페이지 간 물리 여백

        public List<LayoutPage> ComputeLayout(Document doc, Graphics g)
        {
            List<LayoutPage> pages = new List<LayoutPage>();

            int currentPageNum = 1;
            float currentGlobalY = PageGap;

            LayoutPage currentPage = new LayoutPage(currentPageNum, currentGlobalY);
            pages.Add(currentPage);

            RectangleF printArea = currentPage.PrintableArea;
            float localX = printArea.Left;
            float localY = printArea.Top;

            float currentLineHeight = 0;
            List<LayoutRun> currentLineBuffer = new List<LayoutRun>();
            int docPos = 0;
            int lineStartDocPos = 0;

            foreach (var para in doc.Paragraphs)
            {

                bool hasEopRun = false;

                foreach (var run in para.Runs)
                {
                    if (run is EopRun)
                    {
                        hasEopRun = true;

                        currentLineHeight =
                            Math.Max(currentLineHeight, 18f);

                        currentLineBuffer.Add(
                            new LayoutRun
                            {
                                Text = "",
                                StyleSource = run,
                                Bounds = new RectangleF(
                                    localX,
                                    0,
                                    0,
                                    currentLineHeight),
                                SourceStartOffset = 0
                            });

                        docPos++;

                        FlushLine(
                            ref currentPage,
                            currentLineBuffer,
                            ref localX,
                            ref localY,
                            ref currentLineHeight,
                            printArea,
                            ref currentPageNum,
                            ref currentGlobalY,
                            pages,
                            lineStartDocPos,
                            docPos - 1);

                        printArea = currentPage.PrintableArea;
                        lineStartDocPos = docPos;
                        localX = printArea.Left;

                        continue;
                    }

                    if (string.IsNullOrEmpty(run.Text)) continue;

                    using (Font font = new Font(run.FontName, run.FontSize, run.FontStyle))
                    {
                        string text = run.Text;
                        int charIndex = 0;

                        while (charIndex < text.Length)
                        {
                            float remainingWidth = printArea.Right - localX;

                            int fitCount = MeasureFitCharacters(g, text.Substring(charIndex), font, remainingWidth);

                            if (fitCount == 0)
                            {
                                if (currentLineBuffer.Count > 0)
                                {
                                    FlushLine(ref currentPage, currentLineBuffer,
                                        ref localX, ref localY, ref currentLineHeight,
                                        printArea, ref currentPageNum, ref currentGlobalY,
                                        pages, lineStartDocPos, docPos);
                                    printArea = currentPage.PrintableArea;
                                    lineStartDocPos = docPos;
                                }
                                else
                                {
                                    fitCount = 1;
                                }
                            }

                            if (fitCount > 0)
                            {
                                string fitText = text.Substring(charIndex, fitCount);

                                SizeF segmentSize = MeasureExactString(g, fitText, font);

                                currentLineHeight = Math.Max(currentLineHeight, segmentSize.Height);

                                currentLineBuffer.Add(new LayoutRun
                                {
                                    Text = fitText,
                                    StyleSource = run,
                                    Bounds = new RectangleF(localX, 0, segmentSize.Width, segmentSize.Height),
                                    SourceStartOffset = charIndex
                                });

                                docPos += fitCount;
                                localX += segmentSize.Width;
                                charIndex += fitCount;

                                if (charIndex < text.Length)
                                {
                                    FlushLine(ref currentPage, currentLineBuffer,
                                        ref localX, ref localY, ref currentLineHeight,
                                        printArea, ref currentPageNum, ref currentGlobalY,
                                        pages, lineStartDocPos, docPos);
                                    printArea = currentPage.PrintableArea;
                                    lineStartDocPos = docPos;
                                }
                            }
                        }
                    }
                }

                if (!hasEopRun && currentLineBuffer.Count > 0)
                {
                    currentLineHeight =
                        Math.Max(currentLineHeight, 18f);

                    currentLineBuffer.Add(
                        new LayoutRun
                        {
                            Text = "",
                            StyleSource = new EopRun(),
                            Bounds = new RectangleF(
                                localX,
                                0,
                                0,
                                currentLineHeight),
                            SourceStartOffset = 0
                        });

                    docPos++;

                    FlushLine(
                        ref currentPage,
                        currentLineBuffer,
                        ref localX,
                        ref localY,
                        ref currentLineHeight,
                        printArea,
                        ref currentPageNum,
                        ref currentGlobalY,
                        pages,
                        lineStartDocPos,
                        docPos - 1);

                    printArea = currentPage.PrintableArea;
                    lineStartDocPos = docPos;
                    localX = printArea.Left;
                }

            }

            return pages;
        }

        private void FlushLine(ref LayoutPage currentPage, List<LayoutRun> buffer,
            ref float localX, ref float localY, ref float lineH, RectangleF printArea,
            ref int pageNum, ref float globalY, List<LayoutPage> pages,
            int lineStartDocPos, int lineEndDocPos)
        {
            if (lineH <= 0)
            {
                lineH = 18f;
            }

            if (localY + lineH > printArea.Bottom)
            {
                pageNum++;
                globalY += LayoutPage.A4Dimension.Height + PageGap;
                currentPage = new LayoutPage(pageNum, globalY);
                pages.Add(currentPage);
                printArea = currentPage.PrintableArea;
                localY = printArea.Top;
            }

            LayoutLine line = new LayoutLine();
            float lineTopGlobalY = currentPage.PageBounds.Top + localY;
            line.StartDocPosition = lineStartDocPos;
            line.EndDocPosition = lineEndDocPos;

            foreach (var run in buffer)
            {
                run.Bounds = new RectangleF(run.Bounds.X, lineTopGlobalY, run.Bounds.Width, lineH);
                line.LayoutRuns.Add(run);
            }

            line.Bounds = new RectangleF(printArea.Left, lineTopGlobalY, printArea.Width, lineH);
            currentPage.Lines.Add(line);

            localX = printArea.Left;
            localY += lineH + 5f;
            lineH = 0;
            buffer.Clear();
        }

        private int MeasureFitCharacters(Graphics g, string text, Font font, float availableWidth)
        {
            if (availableWidth <= 0) return 0;

            int low = 0;
            int high = text.Length;
            int answer = 0;

            while (low <= high)
            {
                int mid = (low + high) / 2;
                if (mid == 0) { low = 1; continue; }

                string test = text.Substring(0, mid);
                SizeF size = MeasureExactString(g, test, font);

                if (size.Width <= availableWidth)
                {
                    answer = mid;
                    low = mid + 1;
                }
                else
                {
                    high = mid - 1;
                }
            }
            return answer;
        }

        private SizeF MeasureExactString(Graphics g, string text, Font font)
        {
            if (string.IsNullOrEmpty(text)) return new SizeF(0, 0);

            using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.FormatFlags |= StringFormatFlags.NoClip | StringFormatFlags.MeasureTrailingSpaces;
                CharacterRange[] ranges = { new CharacterRange(0, text.Length) };
                sf.SetMeasurableCharacterRanges(ranges);

                RectangleF rect = new RectangleF(0, 0, 99999, 99999);
                Region[] regions = g.MeasureCharacterRanges(text, font, rect, sf);

                if (regions.Length > 0)
                {
                    RectangleF bounds = regions[0].GetBounds(g);
                    return new SizeF(bounds.Width, bounds.Height);
                }
            }
            return TextRenderer.MeasureText(g, text, font, new Size(99999, 99999), TextFormatFlags.NoPadding);
        }
    }
}