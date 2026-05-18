"""표 폭 진단 — tool2 `용인빨파제목`(line 8534) 의 권위 패턴 + TableCreation
의 WidthValue 명시 vs 미명시 시각 폭 비교.

전제: 한/글 ROT 인스턴스 1개 떠 있음 (사용자가 작업 중이라도 새 문서로 진행).
"""
import sys, os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from forge.hwp_session import list_hwp_instances, attach_to_instance, init_com_for_thread
from forge.renderers import primitives as p
from forge.com_helpers import set_param

init_com_for_thread()
sess = attach_to_instance(list_hwp_instances()[0])
hwp = sess.hwp
per_mm = hwp.MiliToHwpUnit(1.0)

SENT = [25.0, 46.67, 46.67, 46.67]
ROWS_MM = [8.4, 8.4]


def measure_4_cells(label):
    """첫 셀에 캐럿이 있다고 가정, 4셀 폭 측정 후 합 반환."""
    widths = []
    for i in range(len(SENT)):
        so = hwp.HParameterSet.HShapeObject
        hwp.HAction.GetDefault("TablePropertyDialog", so.HSet)
        w_mm = so.ShapeTableCell.Width / per_mm
        widths.append(w_mm)
        if i < len(SENT) - 1:
            hwp.HAction.Run("TableRightCellAppend")
    total = sum(widths)
    print(f"  [{label}] cells={[f'{w:.2f}' for w in widths]} sum={total:.2f}mm  본문대비{total-170:+.2f}")
    return total, widths


def goto_first_cell():
    hwp.HAction.Run("TableColPageUp")
    hwp.HAction.Run("TableColBegin")


def make_new_doc():
    hwp.HAction.Run("FileNew")
    p.set_page_margins(hwp, left_mm=20, right_mm=20, top_mm=10, bottom_mm=10, header_mm=10, footer_mm=10)


def make_table_default():
    """현재 우리 primitives.make_table 그대로 — WidthValue 명시 안 함."""
    hwp.HAction.GetDefault("TableCreate", hwp.HParameterSet.HTableCreation.HSet)
    T = hwp.HParameterSet.HTableCreation
    T.Rows = len(ROWS_MM)
    T.Cols = len(SENT)
    T.WidthType = 2
    T.HeightType = 1
    T.CreateItemArray("ColWidth", len(SENT))
    for i, w in enumerate(SENT):
        T.ColWidth.SetItem(i, hwp.MiliToHwpUnit(w))
    T.CreateItemArray("RowHeight", len(ROWS_MM))
    for i, h in enumerate(ROWS_MM):
        T.RowHeight.SetItem(i, hwp.MiliToHwpUnit(h))
    T.TableProperties.TreatAsChar = 1
    hwp.HAction.Execute("TableCreate", hwp.HParameterSet.HTableCreation.HSet)


def make_table_with_width_value():
    """WidthValue / HeightValue 명시. 한/글에 표 전체 폭을 명시적으로 알림."""
    hwp.HAction.GetDefault("TableCreate", hwp.HParameterSet.HTableCreation.HSet)
    T = hwp.HParameterSet.HTableCreation
    T.Rows = len(ROWS_MM)
    T.Cols = len(SENT)
    T.WidthType = 2
    T.HeightType = 1
    T.WidthValue = hwp.MiliToHwpUnit(sum(SENT))      # ★ 명시
    T.HeightValue = hwp.MiliToHwpUnit(sum(ROWS_MM))  # ★ 명시
    T.CreateItemArray("ColWidth", len(SENT))
    for i, w in enumerate(SENT):
        T.ColWidth.SetItem(i, hwp.MiliToHwpUnit(w))
    T.CreateItemArray("RowHeight", len(ROWS_MM))
    for i, h in enumerate(ROWS_MM):
        T.RowHeight.SetItem(i, hwp.MiliToHwpUnit(h))
    T.TableProperties.TreatAsChar = 1
    hwp.HAction.Execute("TableCreate", hwp.HParameterSet.HTableCreation.HSet)


print(f"\n>>> 테스트 cols_mm = {SENT}, 합 = {sum(SENT):.2f} mm")
print(">>> 페이지 본문 폭 = 170mm (A4 - 좌20 - 우20)")

# ── 케이스 1: 현재 (WidthValue 미명시 + 셀여백제로 호출 안 함) ──
print("\n[케이스 1] make_table 기본 (셀여백 처리 없음)")
make_new_doc()
make_table_default()
measure_4_cells("baseline")

# ── 케이스 2: 표 생성 직후 셀여백제로 (tool2 용인빨파제목 패턴) ──
print("\n[케이스 2] make_table → 셀여백제로 (즉시) → 측정")
make_new_doc()
make_table_default()
p.set_cell_margin_zero(hwp)
goto_first_cell()
measure_4_cells("cell_margin_zero immediate")

# ── 케이스 3: WidthValue 명시 + 셀여백제로 ──
print("\n[케이스 3] WidthValue 명시 + 셀여백제로 (즉시)")
make_new_doc()
make_table_with_width_value()
p.set_cell_margin_zero(hwp)
goto_first_cell()
measure_4_cells("WidthValue + cell_margin_zero")

# ── 케이스 4: WidthValue 명시 + 셀 블록 후 셀여백제로 (우리 현재) ──
print("\n[케이스 4] WidthValue 명시 + 셀 블록 선택 후 셀여백제로 (우리 현재)")
make_new_doc()
make_table_with_width_value()
saved = p.get_current_pos(hwp)
p.select_all_cells(hwp)
p.set_cell_margin_zero(hwp)
p.set_table_outside_margin_zero(hwp)
hwp.HAction.Run("Cancel")
p.set_current_pos(hwp, saved)
measure_4_cells("WidthValue + select_all + margin_zero")

# ── 케이스 5: cols_mm 보내기 전 셀당 -3.67mm 보정 ──
print("\n[케이스 5] cols_mm 각 값에 -3.67mm 보정 후 표 생성")
make_new_doc()
SENT_COMP = [w - 3.67 for w in SENT]
hwp.HAction.GetDefault("TableCreate", hwp.HParameterSet.HTableCreation.HSet)
T = hwp.HParameterSet.HTableCreation
T.Rows = len(ROWS_MM); T.Cols = len(SENT_COMP)
T.WidthType = 2; T.HeightType = 1
T.CreateItemArray("ColWidth", len(SENT_COMP))
for i, w in enumerate(SENT_COMP):
    T.ColWidth.SetItem(i, hwp.MiliToHwpUnit(w))
T.CreateItemArray("RowHeight", len(ROWS_MM))
for i, h in enumerate(ROWS_MM):
    T.RowHeight.SetItem(i, hwp.MiliToHwpUnit(h))
T.TableProperties.TreatAsChar = 1
hwp.HAction.Execute("TableCreate", hwp.HParameterSet.HTableCreation.HSet)
print(f"  보낸 cols_mm = {[f'{w:.2f}' for w in SENT_COMP]} 합 = {sum(SENT_COMP):.2f}mm")
measure_4_cells("compensated -3.67")

# ── 케이스 6: TreatAsChar=0 (인라인 → 블록 표) ──
print("\n[케이스 6] TreatAsChar=0 (블록 배치)")
make_new_doc()
hwp.HAction.GetDefault("TableCreate", hwp.HParameterSet.HTableCreation.HSet)
T = hwp.HParameterSet.HTableCreation
T.Rows = len(ROWS_MM); T.Cols = len(SENT)
T.WidthType = 2; T.HeightType = 1
T.CreateItemArray("ColWidth", len(SENT))
for i, w in enumerate(SENT):
    T.ColWidth.SetItem(i, hwp.MiliToHwpUnit(w))
T.CreateItemArray("RowHeight", len(ROWS_MM))
for i, h in enumerate(ROWS_MM):
    T.RowHeight.SetItem(i, hwp.MiliToHwpUnit(h))
T.TableProperties.TreatAsChar = 0  # ★
hwp.HAction.Execute("TableCreate", hwp.HParameterSet.HTableCreation.HSet)
measure_4_cells("TreatAsChar=0")
