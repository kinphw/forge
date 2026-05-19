// 앱 상태 — 모든 탭이 공유. Python forge/ui/app.py 의 AppState 등가.

using Forge.Core;
using Forge.Core.Templates;

namespace Forge.UI;

public sealed class AppState
{
    /// <summary>현재 attach 된 한/글 세션. lazy — 사용자 액션 시점에 ensure.</summary>
    public HwpSession? Hwp { get; set; }

    /// <summary>현재 보고서 양식 spec. Tab2 가 편집하면 swap.</summary>
    public ReportSpec Spec { get; set; } = ReportSpec.Report1;

    /// <summary>
    /// 사용자가 picker 로 명시 선택한 한/글 인스턴스 moniker.
    /// ROT 에 여러 한/글이 떠 있을 때 silent first-match 사고 방지 위해 영구 저장.
    /// ensure_hwp 가 이 값 우선 사용.
    /// </summary>
    public string? PreferredMoniker { get; set; }
}
