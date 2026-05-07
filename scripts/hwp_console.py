"""
한/글 COM API 인터랙티브 시험판.

사용: VSCode 에서 본 파일 열고, 필요한 줄/블록을 드래그 → Shift+Enter
      ('Run Selection in Python Terminal'). 위에서부터 순서대로 실행하면 됨.

전제: 한/글이 먼저 띄워져 있고, 시험 대상 텍스트가 영역 선택돼 있음.

A/S/F/G hotkey 가 안 먹는 원인 분리 진단을 위한 5 케이스 (#1~#5) 가 차례로
배치돼 있음. 각 케이스 실행 후 한/글 화면 변화 + readback 비교로 분리.
"""


# ── attach ─ ROT 에서 한/글 첫 인스턴스 잡기 (한 번만 실행) ────
import pythoncom, re
from win32com.client import gencache, Dispatch

pythoncom.CoInitialize()
ctx = pythoncom.CreateBindCtx(0)
rot = pythoncom.GetRunningObjectTable()
mk = next(m for m in rot
          if re.match(r'^!HwpObject\.\d+\.\d+$', m.GetDisplayName(ctx, None)))
disp = rot.GetObject(mk).QueryInterface(pythoncom.IID_IDispatch)
try:
    hwp = gencache.EnsureDispatch(disp)
except (TypeError, AttributeError):
    hwp = Dispatch(disp)
print('attached:', mk.GetDisplayName(ctx, None), '| Path =', repr(hwp.Path))


# ── selection 좌표 (GetSelectedPosBySet, 모든 한/글 버전 호환) ──
s = hwp.CreateSet('ListParaPos')
e = hwp.CreateSet('ListParaPos')
ok = hwp.GetSelectedPosBySet(s, e)
print('ok=', ok,
      'start=', (s.Item('List'), s.Item('Para'), s.Item('Pos')),
      'end=',   (e.Item('List'), e.Item('Para'), e.Item('Pos')))


# ── selection 텍스트 (saveblock) ─────────────────────────────
hwp.GetTextFile('TEXT', 'saveblock')


# ── CharShape readback (자주 쓰는 항목) ──────────────────────
hwp.HAction.GetDefault('CharShape', hwp.HParameterSet.HCharShape.HSet)
cs = hwp.HParameterSet.HCharShape
{
    'FaceNameHangul': str(cs.FaceNameHangul),
    'FaceNameLatin':  str(cs.FaceNameLatin),
    'FontTypeHangul': cs.FontTypeHangul,
    'FontTypeLatin':  cs.FontTypeLatin,
    'Height':         cs.Height,
    'Bold':           cs.Bold,
    'Italic':         cs.Italic,
}


# ── #1: Forge set_font 그대로 (7면 + FontType=0 + Height + Bold=0) ──
act = hwp.CreateAction('CharShape')
ps = act.CreateSet()
act.GetDefault(ps)
for face in ('Hangul','Latin','Hanja','Japanese','Other','Symbol','User'):
    ps.SetItem(f'FaceName{face}', '맑은 고딕')
    ps.SetItem(f'FontType{face}', 0)
ps.SetItem('Height', 1200)
ps.SetItem('Bold', 0)
act.Execute(ps)


# ── #2: Bold 키 제외 (Bold=0 강제 의심) ──────────────────────
act = hwp.CreateAction('CharShape')
ps = act.CreateSet()
act.GetDefault(ps)
for face in ('Hangul','Latin','Hanja','Japanese','Other','Symbol','User'):
    ps.SetItem(f'FaceName{face}', '맑은 고딕')
    ps.SetItem(f'FontType{face}', 0)
ps.SetItem('Height', 1200)
act.Execute(ps)


# ── #3: FontType=1 (TTF 강제) 의심 ───────────────────────────
act = hwp.CreateAction('CharShape')
ps = act.CreateSet()
act.GetDefault(ps)
for face in ('Hangul','Latin','Hanja','Japanese','Other','Symbol','User'):
    ps.SetItem(f'FaceName{face}', '맑은 고딕')
    ps.SetItem(f'FontType{face}', 1)
ps.SetItem('Height', 1200)
act.Execute(ps)


# ── #4: Hangul 면만 (Latin 면 거부 의심) ─────────────────────
act = hwp.CreateAction('CharShape')
ps = act.CreateSet()
act.GetDefault(ps)
ps.SetItem('FaceNameHangul', '맑은 고딕')
ps.SetItem('FontTypeHangul', 0)
ps.SetItem('Height', 1200)
act.Execute(ps)


# ── #5: Height 만 (가장 단순) ─────────────────────────────────
act = hwp.CreateAction('CharShape')
ps = act.CreateSet()
act.GetDefault(ps)
ps.SetItem('Height', 1200)
act.Execute(ps)


# ── 참고: 휴먼명조 권위 spec (한컴라이브러리_decompiled.py:1014-1034) ──
act = hwp.CreateAction('CharShape')
ps = act.CreateSet()
act.GetDefault(ps)
ps.SetItem('FaceNameHangul',   '휴먼명조');   ps.SetItem('FontTypeHangul',   2)
ps.SetItem('FaceNameUser',     '명조');       ps.SetItem('FontTypeUser',     2)
ps.SetItem('FaceNameSymbol',   '한양신명조'); ps.SetItem('FontTypeSymbol',   2)
ps.SetItem('FaceNameOther',    '한양신명조'); ps.SetItem('FontTypeOther',    2)
ps.SetItem('FaceNameJapanese', '한양신명조'); ps.SetItem('FontTypeJapanese', 2)
ps.SetItem('FaceNameHanja',    '한양신명조'); ps.SetItem('FontTypeHanja',    2)
ps.SetItem('FaceNameLatin',    'HCI Poppy');  ps.SetItem('FontTypeLatin',    2)
ps.SetItem('Height', 1500)
act.Execute(ps)


# ── 참고: HY울릉도M (Ctrl+Shift+G 기본값) ────────────────────
act = hwp.CreateAction('CharShape')
ps = act.CreateSet()
act.GetDefault(ps)
for face in ('Hangul','Latin','Hanja','Japanese','Other','Symbol','User'):
    ps.SetItem(f'FaceName{face}', 'HY울릉도M')
    ps.SetItem(f'FontType{face}', 1)   # TTF — 0(don't care) 은 한/글 2018 거부
ps.SetItem('Height', 1500)
act.Execute(ps)


# ── 한/글 권위 폰트 list (첫 50) ─────────────────────────────
list(hwp.GetFontList())[:50]


# ── CharShape 65 항목 전체 dump ──────────────────────────────
hwp.HAction.GetDefault('CharShape', hwp.HParameterSet.HCharShape.HSet)
cs = hwp.HParameterSet.HCharShape
keys = (
    [f'FaceName{x}' for x in ('Hangul','Latin','Hanja','Japanese','Other','Symbol','User')] +
    [f'FontType{x}' for x in ('Hangul','Latin','Hanja','Japanese','Other','Symbol','User')] +
    [f'Size{x}'     for x in ('Hangul','Latin','Hanja','Japanese','Other','Symbol','User')] +
    [f'Ratio{x}'    for x in ('Hangul','Latin','Hanja','Japanese','Other','Symbol','User')] +
    ['Bold','Italic','SmallCaps','Emboss','Engrave','SuperScript','SubScript',
     'UnderlineType','UnderlineShape','OutlineType','ShadowType',
     'TextColor','ShadeColor','ShadowColor','Height']
)
{k: getattr(cs, k, '<missing>') for k in keys}


# ── 단순 액션 (필요시 한 줄 드래그 + Shift+Enter) ───────────
# hwp.HAction.Run('Cancel')          # selection 해제
# hwp.HAction.Run('MoveParaBegin')   # 문단 처음
# hwp.HAction.Run('MoveSelParaEnd')  # 문단 끝까지 selection
