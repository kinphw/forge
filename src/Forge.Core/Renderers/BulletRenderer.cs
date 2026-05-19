// 본문 글머리 (□ ○ - ·).
//
// tool2 매핑:
//   - L1·L2 = `금감원페이지` 본문 14468-14498, 14506-14538 (인라인)
//   - L3·L4 = tool2 본문에는 없음. Forge 자체 정의.
//
// 마커 뒤 `(...)` prefix(개요/요약 등) 가 있으면 그 부분만 Spec.BulletSummaryFont
// (default `HY울릉도M`) 로 강조. L1~L4 공통. 변환 시점에 SSOT 가 override 가능.

using Forge.Core.Templates;
using static Forge.Core.Renderers.Primitives;

namespace Forge.Core.Renderers;

public sealed class BulletRenderer : ElementRenderer
{
    public BulletRenderer(dynamic hwp, ReportSpec spec) : base(hwp, spec) { }

    /// <summary>본문 글머리 1~4단계 렌더링.</summary>
    /// <param name="level">1=□, 2=○, 3=-, 4=·</param>
    /// <param name="body">본문 텍스트 (`(...)` prefix 제외한 나머지).</param>
    /// <param name="summary">마커 뒤 `(...)` 안 텍스트. null 이면 일반 본문만.</param>
    public void Render(int level, string body, string? summary = null)
    {
        if (level < 1 || level > Spec.Bullets.Count)
            throw new ArgumentOutOfRangeException(nameof(level),
                $"Invalid bullet level: {level} (must be 1~{Spec.Bullets.Count})");

        var bs = Spec.Bullets[level - 1];

        // 폰트·크기·줄간격·내어쓰기
        SetFont(Hwp, bs.Font, bs.SizePt, bold: bs.Bold);
        SetLineSpacing(Hwp, bs.LineSpacing);
        SetIndent(Hwp, bs.IndentPt);
        AlignPara(Hwp, Align.Justify);

        // 글머리 앞 공백 + 글리프 + 뒤 공백
        InsertFixedSpace(Hwp, bs.FixedPre);
        InsertText(Hwp, bs.OutGlyph);
        InsertFixedSpace(Hwp, bs.FixedPost);

        // 마커 뒤 `(...)` 강조 — Spec.BulletSummaryFont. L1~L4 공통.
        // crop size 는 해당 레벨 SizePt 그대로.
        if (!string.IsNullOrEmpty(summary))
        {
            SetFont(Hwp, Spec.BulletSummaryFont, bs.SizePt, bold: bs.Bold);
            InsertText(Hwp, $"({summary}) ");
            SetFont(Hwp, bs.Font, bs.SizePt, bold: bs.Bold);  // 본문 폰트 복귀
        }

        InsertText(Hwp, body);
        BreakPara(Hwp);
    }
}
