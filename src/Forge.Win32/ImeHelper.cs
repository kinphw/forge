// IME 조합 확정 헬퍼 — 상용구(Ctrl+Shift+I) 등 "방금 조합 중인 글자"를 대상으로
// COM 편집하기 전에 조합을 확정(commit)시킨다.
//
// 전역 단축키(RegisterHotKey)는 포커스를 뺏지 않아 대상 앱(한/글)의 IME 조합이
// 살아있음. 확정 안 하면 COM 편집(□) 후 커서 이동 시 조합 버퍼의 글자(ㅁ)가
// 되살아나 원복됨.
//
// ★ 확정 방법 = 오른쪽 방향키 1회 주입.
//   방향키(캐럿 이동 키)는 진행 중 IME 조합을 확정시키는 표준 동작이고, 조합이
//   없으면 캐럿만 한 칸 움직이는(줄 끝에선 무해) no-op 에 가깝다. 확정 후 캐럿은
//   확정 글자 '뒤'에 오므로, 호출부의 "앞 글자 읽기"(MoveSelPrevChar) 방향이 맞는다.
//
//   이전 구현(AttachThreadInput + SetFocus 포커스 넛지 + ImmNotifyIME)은:
//     - 한/글 2018 이 ImmNotifyIME 무반응(TSF 계열)이라 IMM 은 헛일이었고,
//     - 포커스 넛지는 SetFocus 가 대상 창(또는 최상위 부모)을 활성화하는 Win32
//       공식 동작 탓에 Forge 창이 화면에 떴다 사라지고 크로스프로세스 입력큐
//       동기화로 느렸다.
//   방향키 주입은 포커스/창을 전혀 안 건드려 깜박임 0 + 훨씬 가볍다.

namespace Forge.Win32;

public static class ImeHelper
{
    /// <summary>
    /// 포그라운드(한/글) 창의 진행 중 IME 조합을 확정한다.
    /// 오른쪽 방향키를 주입 — 조합 확정 + 캐럿을 확정 글자 뒤로 이동. best-effort.
    /// </summary>
    public static void CommitComposition()
    {
        try
        {
            NativeMethods.keybd_event(NativeMethods.VK_RIGHT, 0, 0, UIntPtr.Zero);                        // down
            NativeMethods.keybd_event(NativeMethods.VK_RIGHT, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero); // up
        }
        catch { /* 주입 실패는 무해 — 조합 없으면 어차피 no-op */ }
    }
}
