using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace AbiCsEngine.Core
{
    public class AbiWordViewer : UserControl
    {
        private Document _document;
        private List<LayoutPage> _pages = new List<LayoutPage>();
        private GdiLayoutEngine _engine = new GdiLayoutEngine();

        public Document Document
        {
            get => _document;
            set { _document = value; RefreshLayout(); }
        }

        public AbiWordViewer()
        {
            this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                          ControlStyles.UserPaint |
                          ControlStyles.OptimizedDoubleBuffer, true);
            this.BackColor = Color.FromArgb(55, 55, 55); // 다크 회색 워드 스페이스 백그라운드
            this.AutoScroll = true;
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
                // 총 스크롤 영역을 물리 페이지의 총 누적 해상도 높이만큼 확장
                this.AutoScrollMinSize = new Size((int)LayoutPage.A4Dimension.Width + 100, (int)lastPage.PageBounds.Bottom + 40);
            }
            this.Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;

            // 핵심: WinForms 스크롤바 이동값 좌표계 변환 처리
            g.TranslateTransform(this.AutoScrollPosition.X, this.AutoScrollPosition.Y);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            // 데스크톱 해상도 대응 중앙 정렬 X 좌표 오프셋 계산
            float renderOffsetX = Math.Max(30, (this.ClientSize.Width - LayoutPage.A4Dimension.Width) / 2);

            foreach (var page in _pages)
            {
                // 1. 페이지 그림자 및 도화지 그리기
                RectangleF paperRect = new RectangleF(renderOffsetX, page.PageBounds.Y, page.PageBounds.Width, page.PageBounds.Height);

                g.FillRectangle(Brushes.Black, paperRect.X + 5, paperRect.Y + 5, paperRect.Width, paperRect.Height); // 그림자
                g.FillRectangle(Brushes.White, paperRect); // 흰색 A4 용지 표면
                g.DrawRectangle(Pens.DimGray, paperRect.X, paperRect.Y, paperRect.Width, paperRect.Height); // 테두리

                // 2. 페이지 내부의 줄바꿈 완료된 라인 순회 그리기
                foreach (var line in page.Lines)
                {
                    foreach (var run in line.LayoutRuns)
                    {
                        using (Font font = new Font(run.StyleSource.FontName, run.StyleSource.FontSize, run.StyleSource.FontStyle))
                        using (Brush brush = new SolidBrush(run.StyleSource.ForeColor))
                        using (StringFormat sf = new StringFormat(StringFormat.GenericTypographic))
                        {
                            // 변경 코드: 전체 중앙정렬 오프셋(renderOffsetX)만 더해줌으로써 설정된 양쪽 여백이 대칭으로 유지됨
                            float targetX = run.Bounds.X + renderOffsetX;
                            g.DrawString(run.Text, font, brush, targetX, run.Bounds.Y, sf);
                        }
                    }
                }

                // 3. 페이지 하단 번호 인디케이터 장식
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
    }
}