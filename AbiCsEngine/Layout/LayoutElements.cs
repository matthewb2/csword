using System.Collections.Generic;
using System.Drawing;

namespace AbiCsEngine
{
    // 하나의 물리적인 문자 조각 배치 정보
    public class LayoutRun
    {
        public string Text { get; set; } = string.Empty;
        public required TextRun StyleSource { get; set; }
        public RectangleF Bounds { get; set; } // 줄 내부에서의 로컬(혹은 글로벌) 좌표

        public int SourceStartOffset;
    }

    // 물리적으로 줄바꿈(Line Break)이 완료된 한 줄
    public class LayoutLine
    {
        public List<LayoutRun> LayoutRuns { get; set; } = new List<LayoutRun>();
        public RectangleF Bounds { get; set; } // 줄의 전체 바운딩 박스
        public float Height => Bounds.Height;
        public int StartDocPosition { get; set; }
        public int EndDocPosition { get; set; }
    }

    // 물리적으로 페이지 분할(Page Break)이 완료된 한 페이지 (A4 규격)
    public class LayoutPage
    {
        public int PageNumber { get; set; }
        public List<LayoutLine> Lines { get; set; } = new List<LayoutLine>();
        public RectangleF PageBounds { get; set; } // 캔버스 전체에서의 페이지 좌표

        public static readonly SizeF A4Dimension = new SizeF(794, 1123); // 96 DPI 기준 A4 픽셀
        public static readonly PaddingF Margins = new PaddingF(60, 60, 60, 60); // 상하좌우 여백

        public RectangleF PrintableArea => new RectangleF(
            Margins.Left,
            Margins.Top,
            A4Dimension.Width - (Margins.Left + Margins.Right),
            A4Dimension.Height - (Margins.Top + Margins.Bottom)
        );

        public LayoutPage(int pageNum, float globalTopY)
        {
            PageNumber = pageNum;
            PageBounds = new RectangleF(0, globalTopY, A4Dimension.Width, A4Dimension.Height);
        }
    }

    public struct PaddingF
    {
        public float Left, Top, Right, Bottom;
        public PaddingF(float l, float t, float r, float b) { Left = l; Top = t; Right = r; Bottom = b; }
    }
}