// 소제목 (가./나./[1]/[2]).
// ★ SSOT — 레이아웃 코드는 ForgeTemplates.금감원페이지소제목. 이 클래스는 thin wrapper.
//   양식삽입 #3 (금감원페이지소제목) 과 동일 함수 호출 (단, spec 주입).

using Forge.Core.Templates;

namespace Forge.Core.Renderers;

public sealed class SubsectionRenderer : ElementRenderer
{
    public SubsectionRenderer(dynamic hwp, ReportSpec spec) : base(hwp, spec) { }

    public void Render(string marker, string title)
    {
        // skipLeadingBreak: dispatcher 가 이미 EmitBlankPara 로 spacer 줄 emit — 중복 방지.
        ForgeTemplates.금감원페이지소제목(Hwp, marker, title, Spec, skipLeadingBreak: true);
    }
}
