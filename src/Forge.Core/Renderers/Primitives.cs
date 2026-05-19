// 공통 COM 헬퍼.
//
// Python 원본 forge/renderers/primitives.py 의 1:1 포팅.
//
// tool2 의 `한컴라이브러리.기본한컴` 411 메서드 중 자주 쓰는 30~40개를 함수로
// 재구현. 모두 ComHelpers.SetParam 5단계 패턴 또는 Run() 단순 호출의 wrapper.
//
// 설계 원칙:
//   - 함수 1개 = COM 액션 1~2개. 묶음 호출 안 함 (조합은 렌더러 책임)
//   - 매개변수는 hwp 객체 + 의미 있는 값 (mm, pt, RGB 분리)
//   - 단위 변환은 함수 안에서 처리 (호출자는 mm/pt 단위로만 생각)
//
// ★ MCP 권위 (CLAUDE.md §3.2, §5):
//   - 셀/표 테두리 모든 작업 = 'CellBorderFill' 단일 액션
//   - 표 배경색 = 'CellFill' 별도 액션 (HParameterSet.HCellBorderFill.FillAttr 직접)
//   - 휴먼명조 폰트 = 7면 HFT (FontType=2) — tool2 line 1014-1034 권위
//   - 일반 폰트 = 7면 TTF (FontType=1) — tool2 line 919-939 권위
//   - 표탈출 = CloseEx + MoveDown + CloseEx (tool2 line 913-918)
//   - 전체 셀 선택 = TableCellBlock + Extend×2 (tool2 line 14321-14322)

using System.Text.RegularExpressions;
using Forge.Core;
using Forge.Interop.HwpObject;  // PIA — IDHwpParameterArray / IDHwpParameterSet typed access

namespace Forge.Core.Renderers;

public enum Align { Left, Center, Right, Justify }
public enum BorderType { None = 0, Solid = 1, Dotted = 3 }
public enum BorderSide { Top, Bottom, Left, Right }

public static class Primitives
{
    // 인라인 Bold 토큰 — `__X__` (markdown-spec v1.4).
    // 비탐욕 매칭 — `__a__b__` 의 경우 `a` 만 bold, `b` 는 plain.
    private static readonly Regex BoldTokenRegex = new(@"__(.+?)__", RegexOptions.Compiled);

    // ────────────────────────────────────────────────────────────────────
    // 단위 변환 (ComHelpers 와 동일 — 렌더러가 한 곳에서 import 가능하게 중계)
    // ────────────────────────────────────────────────────────────────────
    public static int MmToHwp(dynamic hwp, double mm) => ComHelpers.MmToHwp(hwp, mm);
    public static int PtToHwp(dynamic hwp, double pt) => ComHelpers.PtToHwp(hwp, pt);
    public static int Rgb(dynamic hwp, int r, int g, int b) => ComHelpers.Rgb(hwp, r, g, b);
    public static int Rgb(dynamic hwp, Rgb rgb) => ComHelpers.Rgb(hwp, rgb.R, rgb.G, rgb.B);

    // ────────────────────────────────────────────────────────────────────
    // 단순 액션 실행
    // ────────────────────────────────────────────────────────────────────
    public static void Run(dynamic hwp, string action) => hwp.HAction.Run(action);
    public static void BreakPara(dynamic hwp) => hwp.HAction.Run("BreakPara");
    public static void MoveRight(dynamic hwp) => hwp.HAction.Run("MoveRight");

    /// <summary>고정폭 공백 N회 삽입.</summary>
    public static void InsertFixedSpace(dynamic hwp, int count = 1)
    {
        for (int i = 0; i < count; i++)
            hwp.HAction.Run("InsertFixedWidthSpace");
    }

    // ────────────────────────────────────────────────────────────────────
    // 텍스트 삽입 — 인라인 `__X__` Bold 토큰 자동 처리
    // ────────────────────────────────────────────────────────────────────
    public static void InsertText(dynamic hwp, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        // split 결과: parts[0,2,4,...] = plain, parts[1,3,5,...] = bold
        var parts = BoldTokenRegex.Split(text);
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part.Length == 0) continue;
            if (i % 2 == 0)
            {
                ComHelpers.SetParam(hwp, "InsertText", new Dictionary<string, object> { ["Text"] = part });
            }
            else
            {
                hwp.HAction.Run("CharShapeBold");
                ComHelpers.SetParam(hwp, "InsertText", new Dictionary<string, object> { ["Text"] = part });
                hwp.HAction.Run("CharShapeBold");
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // 정렬
    // ────────────────────────────────────────────────────────────────────
    public static void AlignPara(dynamic hwp, Align mode)
    {
        var action = mode switch
        {
            Align.Left    => "ParagraphShapeAlignLeft",
            Align.Center  => "ParagraphShapeAlignCenter",
            Align.Right   => "ParagraphShapeAlignRight",
            Align.Justify => "ParagraphShapeAlignJustify",
            _ => throw new ArgumentOutOfRangeException(nameof(mode)),
        };
        hwp.HAction.Run(action);
    }

    public static void AlignPara(dynamic hwp, string mode) => AlignPara(hwp, mode switch
    {
        "left" => Align.Left,
        "center" => Align.Center,
        "right" => Align.Right,
        "justify" => Align.Justify,
        _ => throw new ArgumentException($"unknown align: {mode}"),
    });

    // ────────────────────────────────────────────────────────────────────
    // 글자 모양
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 휴먼명조 보고서 폰트 — tool2 권위 spec 정확 복제.
    /// 출처: tool2 한컴라이브러리_decompiled.py:1014-1034.
    /// 7 언어면 별 face name + FontType=2 (HFT 강제). Height·Bold 는 별도 Execute.
    /// </summary>
    public static void SetFontHumanmyongjo(dynamic hwp, double sizePt, bool bold = false)
    {
        ComHelpers.SetParam(hwp, "CharShape", new Dictionary<string, object>
        {
            ["FaceNameHangul"]   = "휴먼명조",   ["FontTypeHangul"]   = 2,
            ["FaceNameUser"]     = "명조",       ["FontTypeUser"]     = 2,
            ["FaceNameSymbol"]   = "한양신명조", ["FontTypeSymbol"]   = 2,
            ["FaceNameOther"]    = "한양신명조", ["FontTypeOther"]    = 2,
            ["FaceNameJapanese"] = "한양신명조", ["FontTypeJapanese"] = 2,
            ["FaceNameHanja"]    = "한양신명조", ["FontTypeHanja"]    = 2,
            ["FaceNameLatin"]    = "HCI Poppy",  ["FontTypeLatin"]    = 2,
        });
        ComHelpers.SetParam(hwp, "CharShape", new Dictionary<string, object>
        {
            ["Height"] = (int)(sizePt * 100),
        });
        if (bold) hwp.HAction.Run("CharShapeBold");
    }

    /// <summary>
    /// 폰트·크기 일괄 적용 (tool2 `폰트()` + `글자크기()` 분리 패턴).
    /// font == "휴먼명조" 면 SetFontHumanmyongjo (HFT) 로 dispatch.
    /// 일반 폰트는 7면 TTF (FontType=1). Height 는 별도 Execute.
    /// Bold 는 selection 보존 위해 별도 CharShapeBold Run.
    /// </summary>
    public static void SetFont(dynamic hwp, string font, double sizePt, bool bold = false)
    {
        if (font == "휴먼명조") { SetFontHumanmyongjo(hwp, sizePt, bold); return; }
        ComHelpers.SetParam(hwp, "CharShape", new Dictionary<string, object>
        {
            ["FaceNameUser"]     = font, ["FontTypeUser"]     = 1,
            ["FaceNameSymbol"]   = font, ["FontTypeSymbol"]   = 1,
            ["FaceNameOther"]    = font, ["FontTypeOther"]    = 1,
            ["FaceNameJapanese"] = font, ["FontTypeJapanese"] = 1,
            ["FaceNameHanja"]    = font, ["FontTypeHanja"]    = 1,
            ["FaceNameLatin"]    = font, ["FontTypeLatin"]    = 1,
            ["FaceNameHangul"]   = font, ["FontTypeHangul"]   = 1,
        });
        ComHelpers.SetParam(hwp, "CharShape", new Dictionary<string, object>
        {
            ["Height"] = (int)(sizePt * 100),
        });
        if (bold) hwp.HAction.Run("CharShapeBold");
    }

    /// <summary>폰트 크기만 (Height). 다른 CharShape 항목은 보존.</summary>
    public static void SetFontSize(dynamic hwp, double pt) =>
        ComHelpers.SetParam(hwp, "CharShape", new Dictionary<string, object>
        {
            ["Height"] = (int)(pt * 100),
        });

    /// <summary>폰트 face 만 변경 (크기 보존).</summary>
    public static void SetFontFace(dynamic hwp, string font)
    {
        if (font == "휴먼명조")
        {
            ComHelpers.SetParam(hwp, "CharShape", new Dictionary<string, object>
            {
                ["FaceNameHangul"]   = "휴먼명조",   ["FontTypeHangul"]   = 2,
                ["FaceNameUser"]     = "명조",       ["FontTypeUser"]     = 2,
                ["FaceNameSymbol"]   = "한양신명조", ["FontTypeSymbol"]   = 2,
                ["FaceNameOther"]    = "한양신명조", ["FontTypeOther"]    = 2,
                ["FaceNameJapanese"] = "한양신명조", ["FontTypeJapanese"] = 2,
                ["FaceNameHanja"]    = "한양신명조", ["FontTypeHanja"]    = 2,
                ["FaceNameLatin"]    = "HCI Poppy",  ["FontTypeLatin"]    = 2,
            });
            return;
        }
        ComHelpers.SetParam(hwp, "CharShape", new Dictionary<string, object>
        {
            ["FaceNameUser"]     = font, ["FontTypeUser"]     = 1,
            ["FaceNameSymbol"]   = font, ["FontTypeSymbol"]   = 1,
            ["FaceNameOther"]    = font, ["FontTypeOther"]    = 1,
            ["FaceNameJapanese"] = font, ["FontTypeJapanese"] = 1,
            ["FaceNameHanja"]    = font, ["FontTypeHanja"]    = 1,
            ["FaceNameLatin"]    = font, ["FontTypeLatin"]    = 1,
            ["FaceNameHangul"]   = font, ["FontTypeHangul"]   = 1,
        });
    }

    private static readonly string[] CharShapeFaceKeys =
    {
        "FaceNameHangul", "FontTypeHangul",
        "FaceNameLatin",  "FontTypeLatin",
        "FaceNameUser",   "FontTypeUser",
        "FaceNameSymbol", "FontTypeSymbol",
        "FaceNameOther",  "FontTypeOther",
        "FaceNameJapanese", "FontTypeJapanese",
        "FaceNameHanja",  "FontTypeHanja",
    };

    /// <summary>현재 typing attr 의 7면 face/type 백업.</summary>
    public static Dictionary<string, object> GetTypingFaceState(dynamic hwp)
    {
        var cs = hwp.HParameterSet.HCharShape;
        hwp.HAction.GetDefault("CharShape", cs.HSet);
        var snapshot = new Dictionary<string, object>();
        foreach (var k in CharShapeFaceKeys)
        {
            object? v;
            try { v = ((object)cs).GetType().InvokeMember(k,
                System.Reflection.BindingFlags.GetProperty, null, cs, null); }
            catch { v = null; }
            if (v is null) continue;
            snapshot[k] = k.StartsWith("FontType") ? Convert.ToInt32(v) : v.ToString() ?? "";
        }
        return snapshot;
    }

    /// <summary>using 블록 동안만 typing attr 의 폰트 face 변경. 종료 시 원복.</summary>
    public static IDisposable TempFontFace(dynamic hwp, string font)
    {
        var backup = GetTypingFaceState(hwp);
        SetFontFace(hwp, font);
        return new FaceRestorer(hwp, backup);
    }

    private sealed class FaceRestorer : IDisposable
    {
        private readonly dynamic _hwp;
        private readonly Dictionary<string, object> _backup;
        public FaceRestorer(dynamic hwp, Dictionary<string, object> backup) { _hwp = hwp; _backup = backup; }
        public void Dispose()
        {
            if (_backup.Count > 0)
                ComHelpers.SetParam(_hwp, "CharShape", _backup);
        }
    }

    /// <summary>글자색 (CharShape.TextColor).</summary>
    public static void SetTextColor(dynamic hwp, int r, int g, int b) =>
        ComHelpers.SetParam(hwp, "CharShape", new Dictionary<string, object>
        {
            ["TextColor"] = ComHelpers.Rgb(hwp, r, g, b),
        });

    public static void SetTextColor(dynamic hwp, Rgb rgb) => SetTextColor(hwp, rgb.R, rgb.G, rgb.B);

    public static void CharBoldOn(dynamic hwp) => hwp.HAction.Run("CharShapeBold");
    public static void CharNormal(dynamic hwp) => hwp.HAction.Run("CharShapeNormal");

    // ────────────────────────────────────────────────────────────────────
    // 문단 모양
    // ────────────────────────────────────────────────────────────────────

    public static void SetIndent(dynamic hwp, double pt) =>
        ComHelpers.SetParam(hwp, "ParagraphShape", new Dictionary<string, object>
        {
            ["Indentation"] = ComHelpers.PtToHwp(hwp, pt),
        });

    public static void SetLineSpacing(dynamic hwp, int pct) =>
        ComHelpers.SetParam(hwp, "ParagraphShape", new Dictionary<string, object>
        {
            ["LineSpacingType"] = 0,
            ["LineSpacing"]     = pct,
        });

    public static void SetParaMargin(dynamic hwp, double leftPt, double rightPt) =>
        ComHelpers.SetParam(hwp, "ParagraphShape", new Dictionary<string, object>
        {
            ["LeftMargin"]  = ComHelpers.PtToHwp(hwp, leftPt),
            ["RightMargin"] = ComHelpers.PtToHwp(hwp, rightPt),
        });

    // ────────────────────────────────────────────────────────────────────
    // 페이지 설정
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 문서 6방향 여백 적용.
    ///
    /// PageSetup ParameterSet 의 PageDef 는 중첩 PIT_SET 이라 SetItem 점 경로 불가.
    /// nested attribute 직접 대입 — tool2 `문서여백` 패턴.
    /// </summary>
    public static void SetPageMargins(
        dynamic hwp,
        double leftMm, double rightMm,
        double topMm, double bottomMm,
        double headerMm, double footerMm)
    {
        hwp.HAction.GetDefault("PageSetup", hwp.HParameterSet.HSecDef.HSet);
        var pageDef = hwp.HParameterSet.HSecDef.PageDef;
        pageDef.LeftMargin   = ComHelpers.MmToHwp(hwp, leftMm);
        pageDef.RightMargin  = ComHelpers.MmToHwp(hwp, rightMm);
        pageDef.TopMargin    = ComHelpers.MmToHwp(hwp, topMm);
        pageDef.BottomMargin = ComHelpers.MmToHwp(hwp, bottomMm);
        pageDef.HeaderLen    = ComHelpers.MmToHwp(hwp, headerMm);
        pageDef.FooterLen    = ComHelpers.MmToHwp(hwp, footerMm);
        hwp.HParameterSet.HSecDef.HSet.SetItem("ApplyTo", 3);  // 3 = 문서 전체
        hwp.HAction.Execute("PageSetup", hwp.HParameterSet.HSecDef.HSet);
    }

    // ────────────────────────────────────────────────────────────────────
    // 표
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 표 생성. tool2 `표만들기(가로크기, 세로크기)` 등가.
    /// 생성 후 첫 셀에 커서 위치.
    ///
    /// ★ PIA reflection + IDispatch.Invoke 직접 호출 (2026-05-19 결정):
    ///   - 한컴 IDispatch 가 PIA 의 typed interface IID (IDHwpParameterSet 등) 안 응답
    ///     → PIA cast E_NOINTERFACE
    ///   - 한컴 ITypeInfo.GetIDsOfNames 는 NotImplementedException
    ///   - 한컴 IDispatch.GetIDsOfNames 는 일부 멤버 (SetItem 등) 안 노출
    ///   해결: PIA reflection 으로 [DispId] attribute 추출 → IDispatch.Invoke(dispid, ...)
    ///   직접 호출. pywin32 의 EnsureDispatch 와 동일한 효과를 typelib 없이 PIA 로.
    /// </summary>
    public static void MakeTable(dynamic hwp, IList<double> colsMm, IList<double> rowsMm)
    {
        hwp.HAction.GetDefault("TableCreate", hwp.HParameterSet.HTableCreation.HSet);

        // PS 의 simple property 들은 dynamic chain 으로 OK (W1 PoC 입증)
        hwp.HParameterSet.HTableCreation.Rows = rowsMm.Count;
        hwp.HParameterSet.HTableCreation.Cols = colsMm.Count;
        hwp.HParameterSet.HTableCreation.WidthType = 2;
        hwp.HParameterSet.HTableCreation.HeightType = 1;

        // Sub-COM dispatch — ITypeInfo dispatch + Python tool2 패턴 (CreateItemArray
        // 반환값 무시 → T.ColWidth getter 로 ParameterArray 재접근).
        // 이유: 한컴 IDispatch.Invoke 가 CreateItemArray 의 LPDISPATCH 반환을 typelib
        // early-bound 에서만 채움. late-bound (우리) 에서는 결과 null.
        // Python pywin32 EnsureDispatch 도 마찬가지 — Python 코드도 반환값 무시하고
        // T.ColWidth 재접근 (forge/renderers/primitives.py 의 make_table 패턴).
        object T = hwp.HParameterSet.HTableCreation;

        TypelibDispatch.InvokeMethodViaTypeInfo(T, "CreateItemArray", "ColWidth", colsMm.Count);
        var colWidth = TypelibDispatch.GetPropertyViaTypeInfo(T, "ColWidth")
            ?? throw new InvalidOperationException("T.ColWidth getter null");
        for (int i = 0; i < colsMm.Count; i++)
            TypelibDispatch.SetIndexedItemViaTypeInfo(
                colWidth, "Item", i, ComHelpers.MmToHwp(hwp, colsMm[i]));

        TypelibDispatch.InvokeMethodViaTypeInfo(T, "CreateItemArray", "RowHeight", rowsMm.Count);
        var rowHeight = TypelibDispatch.GetPropertyViaTypeInfo(T, "RowHeight")
            ?? throw new InvalidOperationException("T.RowHeight getter null");
        for (int i = 0; i < rowsMm.Count; i++)
            TypelibDispatch.SetIndexedItemViaTypeInfo(
                rowHeight, "Item", i, ComHelpers.MmToHwp(hwp, rowsMm[i]));

        // ★ TreatAsChar = 1 — 글자처럼 취급 (2026-05-19 한컴 디벨로퍼 권위):
        //   표가 inline char 로 처리됨. 캐럿이 표 직후 자연 흐름 → nested + modal
        //   corruption 둘 다 회피. 한컴 개발자 CreateTable 함수의 결정적 마지막 step.
        //   기존 dynamic chain set 이 fail 했던 패턴 — 한 번 더 시도.
        hwp.HParameterSet.HTableCreation.TableProperties.TreatAsChar = 1;

        // TableProperties.TreatAsChar — sub-PS property set 일단 skip (default 로 표 생성됨)

        hwp.HAction.Execute("TableCreate", hwp.HParameterSet.HTableCreation.HSet);
    }

    /// <summary>현재 표의 셀 4방향 여백을 0으로 + ShapeType=3, ShapeCellSize=0.</summary>
    public static void SetCellMarginZero(dynamic hwp) =>
        ComHelpers.SetParam(hwp, "TablePropertyDialog", new Dictionary<string, object>
        {
            ["CellMarginBottom"] = ComHelpers.MmToHwp(hwp, 0.0),
            ["CellMarginTop"]    = ComHelpers.MmToHwp(hwp, 0.0),
            ["CellMarginRight"]  = ComHelpers.MmToHwp(hwp, 0.0),
            ["CellMarginLeft"]   = ComHelpers.MmToHwp(hwp, 0.0),
            ["ShapeType"]        = 3,
            ["ShapeCellSize"]    = 0,
        });

    /// <summary>표 외부 4방향 여백을 0으로. tool2 `표밖여백제로`.</summary>
    public static void SetTableOutsideMarginZero(dynamic hwp) =>
        ComHelpers.SetParam(hwp, "TablePropertyDialog", new Dictionary<string, object>
        {
            ["OutsideMarginTop"]    = ComHelpers.MmToHwp(hwp, 0.0),
            ["OutsideMarginBottom"] = ComHelpers.MmToHwp(hwp, 0.0),
            ["OutsideMarginLeft"]   = ComHelpers.MmToHwp(hwp, 0.0),
            ["OutsideMarginRight"]  = ComHelpers.MmToHwp(hwp, 0.0),
        });

    /// <summary>현재 표 외곽 4방향 테두리 종류 (CellBorderFill).</summary>
    public static void SetTableBorderType(dynamic hwp,
        BorderType top, BorderType bottom, BorderType left, BorderType right) =>
        ComHelpers.SetParam(hwp, "CellBorderFill", new Dictionary<string, object>
        {
            ["BorderTypeTop"]    = (int)top,
            ["BorderTypeBottom"] = (int)bottom,
            ["BorderTypeLeft"]   = (int)left,
            ["BorderTypeRight"]  = (int)right,
        });

    /// <summary>현재 표 외곽 4방향 테두리 굵기.</summary>
    public static void SetTableBorderThickness(dynamic hwp, int top, int bottom, int left, int right) =>
        ComHelpers.SetParam(hwp, "CellBorderFill", new Dictionary<string, object>
        {
            ["BorderWidthTop"]    = top,
            ["BorderWidthBottom"] = bottom,
            ["BorderWidthLeft"]   = left,
            ["BorderWidthRight"]  = right,
        });

    /// <summary>
    /// 현재 표 외곽 테두리 색.
    /// ★ 주의: 한/글 COM API 좌측 항목명이 'BorderCorlorLeft' (Color 의 오타).
    /// </summary>
    public static void SetTableBorderColor(dynamic hwp, int r, int g, int b)
    {
        var color = ComHelpers.Rgb(hwp, r, g, b);
        ComHelpers.SetParam(hwp, "CellBorderFill", new Dictionary<string, object>
        {
            ["BorderColorTop"]    = color,
            ["BorderColorBottom"] = color,
            ["BorderColorRight"]  = color,
            ["BorderCorlorLeft"]  = color,  // sic — 한컴 API 자체 오타
        });
    }
    public static void SetTableBorderColor(dynamic hwp, Rgb rgb) => SetTableBorderColor(hwp, rgb.R, rgb.G, rgb.B);

    public static void SetTableInnerLineType(dynamic hwp, BorderType horizontal, BorderType vertical) =>
        ComHelpers.SetParam(hwp, "CellBorderFill", new Dictionary<string, object>
        {
            ["TypeHorz"] = (int)horizontal,
            ["TypeVert"] = (int)vertical,
        });

    public static void SetTableInnerLineThickness(dynamic hwp, int horizontal, int vertical) =>
        ComHelpers.SetParam(hwp, "CellBorderFill", new Dictionary<string, object>
        {
            ["WidthHorz"] = horizontal,
            ["WidthVert"] = vertical,
        });

    public static void SetTableInnerLineColor(dynamic hwp, int r, int g, int b)
    {
        var color = ComHelpers.Rgb(hwp, r, g, b);
        ComHelpers.SetParam(hwp, "CellBorderFill", new Dictionary<string, object>
        {
            ["ColorHorz"] = color,
            ["ColorVert"] = color,
        });
    }
    public static void SetTableInnerLineColor(dynamic hwp, Rgb rgb) => SetTableInnerLineColor(hwp, rgb.R, rgb.G, rgb.B);

    /// <summary>
    /// 현재 셀의 배경색 — tool2 `표배경색` 1:1.
    /// `CellBorderFill`(테두리) 과 별개의 `CellFill` 액션 사용.
    /// 5단계 패턴이 아니라 HParameterSet.HCellBorderFill.FillAttr 직접 대입.
    /// </summary>
    public static void SetTableBg(dynamic hwp, int r, int g, int b)
    {
        var color = hwp.RGBColor(r, g, b);
        hwp.HAction.GetDefault("CellFill", hwp.HParameterSet.HCellBorderFill.HSet);
        var F = hwp.HParameterSet.HCellBorderFill.FillAttr;
        F.type = hwp.BrushType("NullBrush|WinBrush");
        F.WinBrushFaceColor  = color;
        F.WinBrushHatchColor = color;
        F.WinBrushFaceStyle  = hwp.HatchStyle("None");
        F.WindowsBrush = 1;
        hwp.HAction.Execute("CellFill", hwp.HParameterSet.HCellBorderFill.HSet);
    }
    public static void SetTableBg(dynamic hwp, Rgb rgb) => SetTableBg(hwp, rgb.R, rgb.G, rgb.B);

    /// <summary>단일 변에만 실선 적용. tool2 `표테두리단일선('상', 1, 1)` 등가.</summary>
    public static void SetTableBorderSingleLine(dynamic hwp, BorderSide side, int width, int lineType = 1)
    {
        var suffix = side switch
        {
            BorderSide.Top    => "Top",
            BorderSide.Bottom => "Bottom",
            BorderSide.Left   => "Left",
            BorderSide.Right  => "Right",
            _ => throw new ArgumentOutOfRangeException(nameof(side)),
        };
        ComHelpers.SetParam(hwp, "CellBorderFill", new Dictionary<string, object>
        {
            [$"BorderWidth{suffix}"] = width,
            [$"BorderType{suffix}"]  = lineType,
        });
    }

    /// <summary>현재 표 안에서 오른쪽으로 N 셀 이동. tool2 `표오른쪽`.</summary>
    public static void MoveTableRight(dynamic hwp, int count = 1)
    {
        for (int i = 0; i < count; i++)
            hwp.HAction.Run("TableRightCellAppend");
    }

    /// <summary>
    /// 표 밖으로 캐럿 탈출 — tool2 `표탈출` (line 913-918) 1:1.
    /// CloseEx + MoveDown + CloseEx (3 액션). 다행/단일셀 표 모두 안전.
    /// </summary>
    public static void EscapeTable(dynamic hwp)
    {
        hwp.HAction.Run("CloseEx");
        hwp.HAction.Run("MoveDown");
        hwp.HAction.Run("CloseEx");
    }

    /// <summary>
    /// 표 탈출 + 다음 단락 양쪽정렬. 모든 박스형 렌더러 공통 마무리.
    ///
    /// ★ 한컴 2018 검증 (2026-05-19):
    ///   - Python tool2 패턴 `MoveRight + BreakPara` 가 일부 단일셀 표에서 nested
    ///     문제 (캐럿이 셀 안 머묾) 발생할 수 있으나 동작.
    ///   - 대안 `CloseEx + MoveDown + CloseEx` (다행 표용) 를 단일셀에 적용 시 한컴이
    ///     후속 MakeTable 거부 (RPC_E_SERVERFAULT) — 더 큰 부작용.
    ///   결론: Python 패턴 유지. nested 표 fine-tune 은 별도 sub-task.
    /// </summary>
    public static void ExitTableAndJustify(dynamic hwp)
    {
        // ★ 한컴 디벨로퍼 권위 패턴 (2026-05-19):
        //   CloseEx (표 빠져나오기) + MoveLineDown (한 줄 아래 = 표 다음).
        //   TreatAsChar = 1 와 함께 사용 시 nested + modal corruption 회피.
        Run(hwp, "CloseEx");
        Run(hwp, "MoveLineDown");
        AlignPara(hwp, Align.Justify);
    }

    /// <summary>
    /// 현재 캐럿이 위치한 표의 모든 셀 블록 선택. 한/글 F5×3 등가.
    /// tool2 `금감원페이지소제목` (line 14321-14322) 권위 패턴.
    /// 호출 후 Cancel 로 해제 시 캐럿은 표 시작 셀.
    /// </summary>
    public static void SelectAllCells(dynamic hwp)
    {
        hwp.HAction.Run("TableCellBlock");
        hwp.HAction.Run("TableCellBlockExtend");
        hwp.HAction.Run("TableCellBlockExtend");
    }

    // ────────────────────────────────────────────────────────────────────
    // 위치
    // ────────────────────────────────────────────────────────────────────

    public static object GetCurrentPos(dynamic hwp) => hwp.GetPosBySet();
    public static void SetCurrentPos(dynamic hwp, object pos) => hwp.SetPosBySet(pos);

    public static bool IsAtLineStart(dynamic hwp)
    {
        try
        {
            var pos = hwp.GetPosBySet();
            return ((int)pos.Item("Pos")) == 0;
        }
        catch
        {
            return true;
        }
    }

    // ────────────────────────────────────────────────────────────────────
    // tool2 레거시 helper — 디컴파일 메서드 1:1 (templates_tab 등에서 호출)
    // ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// 현재 셀의 세로 정렬. tool2 `셀세로정렬` (line 475-479) 1:1.
    /// direction: 0=위, 1=가운데, 2=아래.
    /// 'CellVerticalAlign' 액션이 HWP API 에 없어 'TablePropertyDialog' +
    /// HShapeObject.ShapeTableCell.VertAlign 직접 대입.
    /// </summary>
    public static void SetCellVerticalAlign(dynamic hwp, int direction = 0)
    {
        try
        {
            hwp.HAction.GetDefault("TablePropertyDialog", hwp.HParameterSet.HShapeObject.HSet);
            hwp.HParameterSet.HShapeObject.ShapeTableCell.VertAlign = direction;
            hwp.HAction.Execute("TablePropertyDialog", hwp.HParameterSet.HShapeObject.HSet);
        }
        catch { /* 셀 외 위치에서 호출 가능 — 무시 */ }
    }

    /// <summary>글자 간격 (자간). tool2 `글자간격(value)` — % 단위 정수.</summary>
    public static void SetKerning(dynamic hwp, int valuePct) =>
        ComHelpers.SetParam(hwp, "CharShape", new Dictionary<string, object>
        {
            ["SpacingHangul"]   = valuePct,
            ["SpacingLatin"]    = valuePct,
            ["SpacingHanja"]    = valuePct,
            ["SpacingJapanese"] = valuePct,
            ["SpacingUser"]     = valuePct,
            ["SpacingSymbol"]   = valuePct,
            ["SpacingOther"]    = valuePct,
        });

    /// <summary>글자 음영색. color=0xFFFFFFFF 면 음영 제거.</summary>
    public static void SetTextShade(dynamic hwp, int color) =>
        ComHelpers.SetParam(hwp, "CharShape", new Dictionary<string, object> { ["ShadeColor"] = color });

    /// <summary>
    /// 페이지 좌+우 여백 합 (mm). tool2 `문단여백측정` (line 294-300) 1:1.
    /// 표 폭 계산용 — `205 - 측정값` 식으로 A4 사용가능 가로폭 산출.
    /// </summary>
    public static double MeasurePageMarginMm(dynamic hwp)
    {
        try
        {
            var action = hwp.CreateAction("PageSetup");
            var pset = action.CreateSet();
            action.GetDefault(pset);
            var pageDef = pset.Item("PageDef");
            int left  = (int)(pageDef.Item("LeftMargin")  ?? 0);
            int right = (int)(pageDef.Item("RightMargin") ?? 0);
            double perMm = (double)hwp.MiliToHwpUnit(1.0);
            if (perMm == 0) perMm = 283.4;  // tool2 fallback
            return Math.Round((left + right) / perMm, 1);
        }
        catch
        {
            return 0.0;
        }
    }

    /// <summary>문단 위 간격 (pt). tool2 `문단위(pt)` 등가.</summary>
    public static void SetParagraphAbovePt(dynamic hwp, double pt) =>
        ComHelpers.SetParam(hwp, "ParagraphShape", new Dictionary<string, object>
        {
            ["PageBreakBefore"] = 0,
            ["PagePadding"]     = (int)(pt * 100),
        });

    public static void InsertPageNumber(dynamic hwp)
    {
        try { hwp.HAction.Run("InsertPageNum"); }
        catch { /* 머리/꼬리말 외 호출 시 실패 가능 — 무시 */ }
    }

    public static void SetPageRenumber(dynamic hwp, int n = 1)
    {
        try
        {
            hwp.HAction.GetDefault("AutoChangeNumber", hwp.HParameterSet.HAutoNum.HSet);
            var N = hwp.HParameterSet.HAutoNum;
            N.Type = 0;       // 0 = page
            N.NewNumber = n;
            hwp.HAction.Execute("AutoChangeNumber", hwp.HParameterSet.HAutoNum.HSet);
        }
        catch { /* 무시 */ }
    }

    /// <summary>머리말 영역 진입. tool2 `머릿말()` — 머리말 편집 모드.</summary>
    public static void InsertHeader(dynamic hwp)
    {
        try { hwp.HAction.Run("HeaderFooterMake"); }
        catch
        {
            try { hwp.HAction.Run("HeaderFooterModify"); }
            catch { /* 무시 */ }
        }
    }

    /// <summary>탭 점선 설정. tool2 `탭점선설정(position)`.</summary>
    public static void SetDottedTab(dynamic hwp, int position)
    {
        try
        {
            hwp.HAction.GetDefault("ParagraphShape", hwp.HParameterSet.HParaShape.HSet);
            var ps = hwp.HParameterSet.HParaShape;
            ps.CreateItemArray("Tab", 1);
            var tab = ps.Tab;
            // Python 원본: tab.SetItem(0, {"Pos": int(position), "Leader": 1, "Type": 0})
            // 한/글 COM 의 nested ParameterSet — dict 로 set 가능.
            tab.SetItem(0, new Dictionary<string, object>
            {
                ["Pos"] = position, ["Leader"] = 1, ["Type"] = 0,
            });
            hwp.HAction.Execute("ParagraphShape", hwp.HParameterSet.HParaShape.HSet);
        }
        catch { /* 무시 */ }
    }

    /// <summary>배경처럼 셀 안에 사진 삽입. tool2 `사진넣기배경(이미지)` 등가.</summary>
    public static void InsertImageBg(dynamic hwp, string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath)) return;
        try { hwp.InsertPicture(imagePath, true, 1, 0, 0, 0, 1, 0, 0); }
        catch { /* 무시 */ }
    }

    /// <summary>글자 스타일 0 (기본) 적용.</summary>
    public static void CharStyleNormal(dynamic hwp)
    {
        try { ComHelpers.SetParam(hwp, "CharShape", new Dictionary<string, object> { ["StyleType"] = 0 }); }
        catch { /* 무시 */ }
    }

    /// <summary>자간 0 초기화. tool2 `자간헌터(0)` 등가.</summary>
    public static void ResetKerningZero(dynamic hwp) => SetKerning(hwp, 0);
}
