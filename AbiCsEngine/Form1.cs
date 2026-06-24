using System;
using System.Drawing;
using System.Reflection.Metadata;
using System.Windows.Forms;

namespace AbiCsEngine
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            this.Size = new Size(1024, 768);
            this.Text = "AbiWord Ported GDI+ Core Layout Engine";

            RenderControl render = new RenderControl();
            render.Dock = DockStyle.Fill;
            this.Controls.Add(render);

            // 가상 문서 데이터 빌드
            // 명확하게 우리가 만든 Core의 Document임을 컴파일러에게 지정
            AbiCsEngine.Document doc = new AbiCsEngine.Document();

            
            Paragraph pTest = new Paragraph();
            pTest.Runs.Add(new TextRun { Text = "ABC", FontSize = 24, FontStyle = FontStyle.Bold, ForeColor = Color.AntiqueWhite });
            pTest.Runs.Add(
    new EopRun());

            pTest.Runs.Add(new TextRun { Text = "DEF", FontSize = 24, FontStyle = FontStyle.Bold, ForeColor = Color.AntiqueWhite });
            pTest.Runs.Add(
    new EopRun());

            doc.Paragraphs.Add(pTest);
            
            // 타이틀 문단
            
            Paragraph pTitle = new Paragraph();
            pTitle.Runs.Add(new TextRun { Text = "AbiWord C# 레이아웃 명세서 문서", FontSize = 24, FontStyle = FontStyle.Bold, ForeColor = Color.AntiqueWhite });
            pTitle.Runs.Add(
    new EopRun());
            doc.Paragraphs.Add(pTitle);

            Paragraph pBlank = new Paragraph();
            pBlank.Runs.Add(
    new EopRun());
            doc.Paragraphs.Add(pBlank);

            // 레이아웃 엔진이 Line Break와 Page Break를 유발하는지 검증하기 위한 대량 문단 반복 루프
            for (int i = 1; i <= 5; i++)
            {
                Paragraph p = new Paragraph();
                p.Runs.Add(new TextRun { Text = $"제 {i}조항 실시간 분석 루프: ", FontSize = 12, FontStyle = FontStyle.Bold, ForeColor = Color.Crimson });
                p.Runs.Add(new TextRun { Text = "이 엔진은 가변폭 폰트의 자간 메트릭스를 바이너리 서칭을 통해 한 자씩 추적합니다. ", FontSize = 11, FontName = "맑은 고딕" });
                p.Runs.Add(new TextRun { Text = "GDI+ Graphics Subsystem", FontSize = 11, FontStyle = FontStyle.Italic, ForeColor = Color.Blue, FontName = "Consolas" });
                p.Runs.Add(new TextRun { Text = " 내부에서 상하좌우 여백(Margins) 바운더리를 연산하다가 가로 임계점을 돌파하면 자동으로 Line Break를 유발하며, 누적된 Y 높이가 A4 하단 한계선인 1123 픽셀을 돌파하는 즉시 데이터 손실 없이 완벽하게 다음 순번의 LayoutPage 객체로 전이되어 물리적인 Page Break 처리를 완결짓습니다.", FontSize = 11, FontName = "맑은 고딕" });

                p.Runs.Add(
    new EopRun());

                doc.Paragraphs.Add(p);

            }
            
            // 엔진 가동 및 화면 바인딩
            render.Document = doc;
        }
    }
}