// COM late-bound dispatch helper.
//
// 배경:
//   C# `dynamic` 의 COM 바인더는 IDispatch 위에 동작하지만, sub-COM 객체
//   (예: HParameterSet 의 ItemArray) 의 메서드를 navigate 못 하는 알려진
//   한계가 있다 — "'System.__ComObject' does not contain a definition for 'XXX'"
//   RuntimeBinderException 발생.
//
// 우회:
//   Type.InvokeMember(...) 로 System.__ComObject 의 IDispatch 를 직접 호출.
//   pywin32 의 instance proxy 와 동등한 late-bound dispatch.
//
// 사용 예 (Primitives.MakeTable):
//   object colWidth = T.ColWidth;   // dynamic 에서 object 로 materialize
//   ComLateBind.Call(colWidth, "SetItem", i, ComHelpers.MmToHwp(hwp, w));

using System.Reflection;

namespace Forge.Core;

public static class ComLateBind
{
    private const BindingFlags Method = BindingFlags.InvokeMethod;
    private const BindingFlags GetProp = BindingFlags.GetProperty;
    private const BindingFlags SetProp = BindingFlags.SetProperty;

    /// <summary>COM 객체의 메서드 호출 (반환값 무시).</summary>
    public static void Call(object com, string name, params object[] args) =>
        com.GetType().InvokeMember(name, Method, null, com, args);

    /// <summary>COM 객체의 메서드 호출 + 반환값.</summary>
    public static object? Invoke(object com, string name, params object[] args) =>
        com.GetType().InvokeMember(name, Method, null, com, args);

    /// <summary>COM 객체의 속성 읽기.</summary>
    public static object? Get(object com, string name) =>
        com.GetType().InvokeMember(name, GetProp, null, com, null);

    /// <summary>COM 객체의 속성 쓰기.</summary>
    public static void Set(object com, string name, object value) =>
        com.GetType().InvokeMember(name, SetProp, null, com, new[] { value });
}
