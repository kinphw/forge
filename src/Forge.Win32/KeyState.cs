// 전역 키 상태 조회 — UI 스레드를 점유하는 동기 루프(W 자간 좁힘 등) 도중 사용자
// 취소(ESC)를 감지하기 위한 헬퍼.
//
// 일반 WinForms KeyDown 은 UI 스레드가 루프에 묶여 있으면 디스패치되지 않으므로,
// 루프가 매 iteration 마다 GetAsyncKeyState(ESC) 를 폴링해 즉시 중단한다.
// GetAsyncKeyState 는 포커스와 무관한 전역 물리 키 상태라, 사용자가 한/글 창에서
// ESC 를 눌러도 감지된다.

namespace Forge.Win32;

public static class KeyState
{
    /// <summary>ESC 키가 지금 물리적으로 눌려 있는가 (포커스 무관 전역). 루프 취소 감지용.</summary>
    public static bool IsEscPressed()
        => (NativeMethods.GetAsyncKeyState(NativeMethods.VK_ESCAPE) & 0x8000) != 0;
}
