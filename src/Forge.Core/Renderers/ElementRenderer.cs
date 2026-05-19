// 모든 렌더러의 추상 베이스. Python 원본 forge/renderers/base.py 1:1.
//
// 각 렌더러:
//   - 한/글 COM (dynamic) 인스턴스와 ReportSpec 보관
//   - Render(...) 으로 현재 커서 위치에 요소 1개 시각 렌더링
//   - 다른 렌더러 호출 안 함 (조합은 HwpxWriter dispatcher 책임)
//
// Render 시그니처는 렌더러마다 다른 매개변수 — 가상 메서드로 통일하지 않고
// 각 구체 클래스가 자기 형태로 노출. dispatcher 가 Node.Type 으로 분기해 호출.

using Forge.Core.Templates;

namespace Forge.Core.Renderers;

public abstract class ElementRenderer
{
    protected readonly dynamic Hwp;
    protected readonly ReportSpec Spec;

    protected ElementRenderer(dynamic hwp, ReportSpec spec)
    {
        Hwp = hwp;
        Spec = spec;
    }
}
