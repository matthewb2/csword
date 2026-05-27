using System;
using System.Collections.Generic;
using System.Drawing;

namespace AbiCsEngine.Core
{
    public class GdiLayoutEngine
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

            foreach (var para in doc.Paragraphs)
            {
                foreach (var run in para.Runs)
                {
                    if (string.IsNullOrEmpty(run.Text)) continue;

                    using (Font font = new Font(run.FontName, run.FontSize, run.FontStyle))
                    {
                        string text = run.Text;
                        int charIndex = 0;

                        while (charIndex < text.Length)
                        {
                            float remainingWidth = printArea.Right - localX;

                            // 현재 남은 공간에 들어갈 수 있는 글자 수 측정
                            int fitCount = MeasureFitCharacters(g, text.Substring(charIndex), font, remainingWidth);

                            if (fitCount == 0)
                            {
                                // 가로 영역이 꽉 찬 경우 줄바꿈(Line Break) 실행
                                if (currentLineBuffer.Count > 0)
                                {
                                    // 중요: currentPage 자체를 ref로 넘겨 새 페이지 교체 시 반영되도록 함
                                    FlushLine(ref currentPage, currentLineBuffer, ref localX, ref localY, ref currentLineHeight, printArea, ref currentPageNum, ref currentGlobalY, pages);
                                    printArea = currentPage.PrintableArea;
                                }
                                else
                                {
                                    fitCount = 1; // 글자 하나가 줄 너비보다 큰 예외 처리
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
                                    Bounds = new RectangleF(localX, 0, segmentSize.Width, segmentSize.Height)
                                });

                                localX += segmentSize.Width;
                                charIndex += fitCount;

                                // 글자가 가로 제한에 도달하여 남은 텍스트가 있다면 Line Break
                                if (charIndex < text.Length)
                                {
                                    FlushLine(ref currentPage, currentLineBuffer, ref localX, ref localY, ref currentLineHeight, printArea, ref currentPageNum, ref currentGlobalY, pages);
                                    printArea = currentPage.PrintableArea;
                                }
                            }
                        }
                    }
                }

                // 하나의 문단(Paragraph)이 끝날 때마다 버퍼 비우기 및 문단 여백 확보
                if (currentLineBuffer.Count > 0)
                {
                    FlushLine(ref currentPage, currentLineBuffer, ref localX, ref localY, ref currentLineHeight, printArea, ref currentPageNum, ref currentGlobalY, pages);
                    printArea = currentPage.PrintableArea;
                }

                // 문단 간의 간격을 반영 (Y축 누적)
                localY += 15f;
            }

            return pages;
        }

        // 라인 버퍼의 내용을 물리 캔버스 좌표로 확정 짓는 핵심 동기화 파이프라인
        private void FlushLine(ref LayoutPage currentPage, List<LayoutRun> buffer,
            ref float localX, ref float localY, ref float lineH, RectangleF printArea,
            ref int pageNum, ref float globalY, List<LayoutPage> pages)
        {
            // 실시간 Page Break 검증: 다음 라인을 배치할 위치가 아래 마진 한계를 넘는가?
            if (localY + lineH > printArea.Bottom)
            {
                pageNum++;
                globalY += LayoutPage.A4Dimension.Height + PageGap;

                // 새 인스턴스를 생성하고 갱신
                currentPage = new LayoutPage(pageNum, globalY);
                pages.Add(currentPage);

                // 새 페이지 규격 영역 재할당 및 상단 여백으로 좌표 강제 리셋
                printArea = currentPage.PrintableArea;
                localY = printArea.Top;
            }

            LayoutLine line = new LayoutLine();
            float lineTopGlobalY = currentPage.PageBounds.Top + localY;

            foreach (var run in buffer)
            {
                // 상대 좌표(0)에서 현재 페이지의 전역 Y축 해상도 절대 위치로 맵 오프셋 가산
                run.Bounds = new RectangleF(run.Bounds.X, lineTopGlobalY, run.Bounds.Width, lineH);
                line.LayoutRuns.Add(run);
            }

            line.Bounds = new RectangleF(printArea.Left, lineTopGlobalY, printArea.Width, lineH);
            currentPage.Lines.Add(line);

            // 중요: 다음 라인이 배치될 X, Y 제어 좌표를 완벽히 하단으로 이동 및 누적
            localX = printArea.Left;
            localY += lineH + 5f; // 행간 여백 보정 (5픽셀 줄간격 확보)
            lineH = 0;            // 줄 높이 초기화
            buffer.Clear();       // 라인 세그먼트 버퍼 비우기
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
                sf.FormatFlags |= StringFormatFlags.NoClip;
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