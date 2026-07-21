// 결론 화살표 박스 (=>).
// 시각 기본값 = 양식삽입 #10 "화살표박스 (회색)" — 연회색(#F2F2F2) 배경 + 얇은 검정
// 실선 테두리 1×1 표 안에 ➡ + 본문. (2026-07-16 사용자 요청으로 기존 민트 점선박스
// `금감원페이지점선박스` 스타일에서 교체. 색·마커·테두리는 모두 ReportSpec 으로 조정 가능
// — 민트 점선으로 되돌리려면 Bg/Dotted/Marker/BorderWidth 만 바꾸면 됨.)

using Forge.Core.Templates;
using static Forge.Core.Renderers.Primitives;

namespace Forge.Core.Renderers;

public sealed class ConclusionRenderer : ElementRenderer
{
    public ConclusionRenderer(dynamic hwp, ReportSpec spec) : base(hwp, spec) { }

    /// <param name="body">=> 다음에 오는 결론 텍스트. 마커(기본 ➡)는 자동 prepend.</param>
    /// <returns>
    /// 생성된 박스 셀의 list-id (실패 시 -1).
    /// ★ 디스패처(DispatchNodes)가 이 셀만 골라 방금 쓴 자리에서 (들·자·들) 정렬하기 위한
    ///   식별자 — 박스 셀은 별도 list 라 본문 순회에 안 잡히고, 그렇다고 모든 셀을 돌면
    ///   가운데정렬인 GFM 표까지 건드린다. 그래서 "이 결론박스" 를 명시적으로 넘긴다.
    /// </returns>
    public int Render(string body)
    {
        var s = Spec;

        if (!IsAtLineStart(Hwp)) BreakPara(Hwp);
        // ★ 2026-06-02: body 단락은 Justify. 기존엔 Right 였으나 진단 로그상
        //   Right-aligned 단락에서 CloseEx 후 MoveLineDown 이 no-op → 캐럿이 박스
        //   앞에 잔류 → 후속 BreakPara 가 박스 위 분할 → 박스가 끝으로 밀리는
        //   cascade. 본문 폭(165mm) 대비 표 폭(159.5mm) 라 시각 차이 5.5mm 미미.
        AlignPara(Hwp, Align.Justify);

        // 1×1 표 — 폭 208 - 여백 (양식삽입 #10 화살표박스와 동일 기준), 세로는 spec.
        double usableWidth = 208.0 - (s.Margins.Left + s.Margins.Right);
        MakeTable(Hwp, new[] { usableWidth }, new[] { s.ConclusionBoxHeightMm });

        // 외곽 테두리 + 배경 (기본 = 얇은 검정 실선 + 연회색 #F2F2F2)
        var borderType = s.ConclusionBorderDotted ? BorderType.Dotted : BorderType.Solid;
        SetTableBorderType(Hwp, borderType, borderType, borderType, borderType);
        SetTableBorderThickness(Hwp,
            s.ConclusionBorderWidth, s.ConclusionBorderWidth,
            s.ConclusionBorderWidth, s.ConclusionBorderWidth);
        SetTableBorderColor(Hwp, s.ConclusionBorderRgb);
        SetTableBg(Hwp, s.ConclusionBgRgb);

        // 본문 (휴먼명조 15pt, 마커 + 본문). 마커는 spec (기본 ➡).
        SetFont(Hwp, s.ConclusionFont, s.ConclusionSizePt, bold: false);
        InsertText(Hwp, $"{s.ConclusionMarker} {body}");

        // ★ 표 탈출 전에 캡쳐 — 여기서만 캐럿이 박스 셀 안에 있다.
        int cellList = -1;
        try { cellList = Linter.Range.GetCaretPos((object)Hwp).List; } catch { /* 실패 시 -1 */ }

        ExitTableAndJustify(Hwp);
        return cellList;
    }
}
