// pywin32 의 EnsureDispatch 등가물 — ITypeInfo 통해 dispid lookup + IDispatch.Invoke 직접 호출.
//
// 배경:
//   한컴 HWP COM 의 IDispatch.GetIDsOfNames 가 ParameterArray.SetItem 같은
//   일부 멤버를 노출 안 함. C# 의 dynamic ComBinder / Type.InvokeMember 둘 다
//   GetIDsOfNames 만 사용 → DISP_E_UNKNOWNNAME. pywin32 의 EnsureDispatch 는
//   typelib (ITypeLib/ITypeInfo) 기반 early-bound 라 typelib 의 모든 멤버 dispid 알아냄.
//   이 헬퍼가 .NET 8 BCL 만으로 같은 패턴 재현.
//
// 핵심 흐름:
//   1. obj.GetType() == System.__ComObject
//   2. obj as IDispatch → GetTypeInfo(0, lcid, out ITypeInfo)
//   3. ITypeInfo.GetIDsOfNames(name, 1, out int[]) — typelib 의 전체 멤버에서 lookup
//   4. IDispatch.Invoke(dispid, ...) 로 호출 + 반환값 marshal
//
// 한계: 한컴 IDispatch.GetTypeInfo 가 ITypeInfo 안 주면 (typelib 미등록) 동작 안 함.

using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace Forge.Core;

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("00020400-0000-0000-C000-000000000046")]
internal interface IDispatch
{
    [PreserveSig]
    int GetTypeInfoCount(out int pcTInfo);

    [PreserveSig]
    int GetTypeInfo(int iTInfo, int lcid, out ITypeInfo pTInfo);

    [PreserveSig]
    int GetIDsOfNames(
        ref Guid riid,
        [MarshalAs(UnmanagedType.LPArray, ArraySubType = UnmanagedType.BStr)] string[] rgszNames,
        int cNames, int lcid,
        [Out, MarshalAs(UnmanagedType.LPArray)] int[] rgDispId);

    [PreserveSig]
    int Invoke(
        int dispIdMember,
        ref Guid riid,
        int lcid,
        short wFlags,
        ref DISPPARAMS pDispParams,
        IntPtr pVarResult,
        IntPtr pExcepInfo,
        IntPtr puArgErr);
}

public static class TypelibDispatch
{
    private const int LOCALE_USER_DEFAULT = 0x0400;

    // wFlags for IDispatch.Invoke
    private const short DISPATCH_METHOD = 1;
    private const short DISPATCH_PROPERTYGET = 2;
    private const short DISPATCH_PROPERTYPUT = 4;
    private const short DISPATCH_PROPERTYPUTREF = 8;

    private static Guid IID_NULL = Guid.Empty;

    // dispid 캐시 — (typeInfoPtr, name) → dispid. typeInfoPtr 은 .GetHashCode 로 식별.
    // 실제 IntPtr 비교는 위험 (GC). 일단 type 별로 small cache.
    private static readonly Dictionary<(Type, string), int> DispIdCache = new();

    /// <summary>
    /// ITypeInfo 통해 멤버의 dispid 를 찾는다. IDispatch.GetIDsOfNames 가 못 찾는
    /// 멤버도 typelib 에 등록되어 있으면 여기서 찾힘.
    /// </summary>
    public static int FindDispId(object comObj, string memberName)
    {
        if (comObj is not IDispatch disp)
            throw new ArgumentException("COM object 가 IDispatch 를 구현하지 않음.", nameof(comObj));

        int hr = disp.GetTypeInfo(0, LOCALE_USER_DEFAULT, out var typeInfo);
        Marshal.ThrowExceptionForHR(hr);
        if (typeInfo is null)
            throw new InvalidOperationException(
                "IDispatch.GetTypeInfo 가 null 반환 — 한컴이 typelib 정보를 안 줌. PIA tlbimp 필요.");

        try
        {
            var ids = new int[1];
            var names = new[] { memberName };
            typeInfo.GetIDsOfNames(names, 1, ids);
            return ids[0];
        }
        finally
        {
            Marshal.ReleaseComObject(typeInfo);
        }
    }

    /// <summary>IDispatch.Invoke 직접 호출 — DISPATCH_METHOD. 반환값 받음.</summary>
    public static object? InvokeMethod(object comObj, string memberName, params object?[] args) =>
        InvokeCore(comObj, memberName, DISPATCH_METHOD, args);

    // 객체별 ITypeInfo dump 캐시 — 같은 객체에 반복 호출 시 dump 비용 회피.
    // key: COM RCW. ConditionalWeakTable 가 GC 친화적이지만 .NET 의 __ComObject 와
    // 호환되는 키로는 IUnknown 포인터가 정답. 단순화 위해 일단 캐시 없이 매번 dump
    // (한컴 dispatch 자체가 millisecond 단위라 dump 한 번이 큰 부담 아님).

    /// <summary>
    /// ITypeInfo.GetFuncDesc 로 typelib 의 정확한 dispid 알아내고 IDispatch.Invoke 직접 호출.
    /// 한컴 IDispatch.GetIDsOfNames 가 일부 멤버 미노출 (예: ParameterArray.SetItem) 해도
    /// ITypeInfo 는 전체 함수 list 노출 — 이 경로로 dispatch.
    /// </summary>
    public static object? InvokeMethodViaTypeInfo(object comObj, string memberName, params object?[] args)
    {
        int dispid = FindDispIdViaTypeInfo(comObj, memberName);
        return InvokeCoreByDispId(comObj, dispid, DISPATCH_METHOD, args);
    }

    /// <summary>ITypeInfo dump 기반 dispid 로 property get.</summary>
    public static object? GetPropertyViaTypeInfo(object comObj, string memberName)
    {
        int dispid = FindDispIdViaTypeInfo(comObj, memberName);
        return InvokeCoreByDispId(comObj, dispid, DISPATCH_PROPERTYGET, Array.Empty<object?>());
    }

    /// <summary>
    /// indexed property setter (예: ParameterArray 의 Item[i] = value).
    /// COM 표준: dispatch_property_put + named arg DISPID_PROPERTYPUT(-3) + (index, value).
    /// Python pywin32 의 `arr.SetItem(i, v)` 가 사실 `Item` property 의 PROPERTYPUT —
    /// typelib 의 ITypeInfo dump 결과로 검증 (SetItem 따로 없음, Item dispid=2 만).
    /// </summary>
    public static void SetIndexedItemViaTypeInfo(object comObj, string indexerName, int index, object value)
    {
        int dispid = FindDispIdViaTypeInfo(comObj, indexerName);
        InvokeCoreByDispId(comObj, dispid, DISPATCH_PROPERTYPUT,
            new object?[] { index, value }, namedDispIdPropPut: true);
    }

    private static int FindDispIdViaTypeInfo(object comObj, string memberName)
    {
        var members = DumpTypeInfoMembers(comObj);
        if (!members.TryGetValue(memberName, out int dispid))
            throw new InvalidOperationException(
                $"ITypeInfo 에 '{memberName}' 없음. 가용 멤버: " +
                string.Join(", ", members.Keys.Take(20)));
        return dispid;
    }

    /// <summary>
    /// PIA reflection 기반 dispid lookup + IDispatch.Invoke 직접 호출.
    /// 한컴 ITypeInfo.GetIDsOfNames 가 NotImplementedException 던지므로 우회.
    /// PIA cast 실패 (한컴이 typed IID 응답 안 함) 도 우회 — IDispatch (표준 IID) 만 사용.
    /// </summary>
    public static object? InvokeMethodByPia(object comObj, Type piaInterface, string methodName, params object?[] args)
    {
        var dispid = GetDispIdFromPia(piaInterface, methodName);
        return InvokeCoreByDispId(comObj, dispid, DISPATCH_METHOD, args);
    }

    /// <summary>PIA interface 의 메서드에서 [DispId] attribute 추출.</summary>
    public static int GetDispIdFromPia(Type piaInterface, string methodName)
    {
        var method = piaInterface.GetMethod(methodName)
            ?? throw new ArgumentException($"PIA {piaInterface.Name} 에 {methodName} 없음");
        var attr = method.GetCustomAttribute<DispIdAttribute>()
            ?? throw new InvalidOperationException($"{piaInterface.Name}.{methodName} 에 DispId 없음");
        return attr.Value;
    }

    /// <summary>
    /// ITypeInfo.GetFuncDesc + GetNames 로 typelib 의 전체 함수 dispid 매핑.
    /// 한컴 ITypeInfo 가 GetIDsOfNames 만 NotImplementedException — 다른 메서드는
    /// 동작할 가능성. 작동하면 정확한 dispid 알 수 있어 PIA reflection 의 부정확성
    /// 우회 가능.
    /// </summary>
    public static Dictionary<string, int> DumpTypeInfoMembers(object comObj)
    {
        var result = new Dictionary<string, int>();
        if (comObj is not IDispatch disp) return result;
        int hr = disp.GetTypeInfo(0, LOCALE_USER_DEFAULT, out ITypeInfo ti);
        if (hr != 0 || ti is null) return result;

        try
        {
            ti.GetTypeAttr(out IntPtr pAttr);
            var attr = Marshal.PtrToStructure<TYPEATTR>(pAttr);
            int nFuncs = attr.cFuncs;
            ti.ReleaseTypeAttr(pAttr);

            for (int i = 0; i < nFuncs; i++)
            {
                try
                {
                    ti.GetFuncDesc(i, out IntPtr pFunc);
                    var fd = Marshal.PtrToStructure<FUNCDESC>(pFunc);
                    int memid = fd.memid;
                    ti.ReleaseFuncDesc(pFunc);

                    var names = new string[1];
                    ti.GetNames(memid, names, 1, out int cNames);
                    if (cNames > 0 && !string.IsNullOrEmpty(names[0]))
                        result[names[0]] = memid;
                }
                catch { /* 일부 함수만 실패 — skip */ }
            }
        }
        catch { /* GetTypeAttr 자체 실패 — typeinfo 미지원 */ }
        finally { Marshal.ReleaseComObject(ti); }
        return result;
    }

    /// <summary>진단용 — 실제 COM 객체의 IDispatch.GetIDsOfNames 응답.</summary>
    public static int GetDispIdViaIDispatch(object comObj, string memberName)
    {
        if (comObj is not IDispatch disp)
            throw new ArgumentException("not IDispatch");
        var ids = new int[1];
        var names = new[] { memberName };
        int hr = disp.GetIDsOfNames(ref IID_NULL_ref, names, 1, LOCALE_USER_DEFAULT, ids);
        Marshal.ThrowExceptionForHR(hr);
        return ids[0];
    }

    private static Guid IID_NULL_ref = Guid.Empty;

    /// <summary>IDispatch.Invoke 직접 호출 — DISPATCH_PROPERTYGET.</summary>
    public static object? GetProperty(object comObj, string memberName) =>
        InvokeCore(comObj, memberName, DISPATCH_PROPERTYGET, Array.Empty<object?>());

    /// <summary>IDispatch.Invoke 직접 호출 — DISPATCH_PROPERTYPUT.</summary>
    public static void SetProperty(object comObj, string memberName, object? value)
    {
        InvokeCore(comObj, memberName, DISPATCH_PROPERTYPUT, new[] { value }, namedDispIdPropPut: true);
    }

    private static object? InvokeCore(
        object comObj, string memberName, short wFlags,
        object?[] args, bool namedDispIdPropPut = false)
    {
        int dispid = FindDispId(comObj, memberName);
        return InvokeCoreByDispId(comObj, dispid, wFlags, args, namedDispIdPropPut);
    }

    private static object? InvokeCoreByDispId(
        object comObj, int dispid, short wFlags,
        object?[] args, bool namedDispIdPropPut = false)
    {
        if (comObj is not IDispatch disp)
            throw new ArgumentException("COM object 가 IDispatch 를 구현하지 않음.", nameof(comObj));

        // VARIANT 인자 배열 — IDispatch.Invoke 는 인자를 역순으로 받음.
        // GCHandle 으로 lifetime 잡고 unmanaged 메모리 alloc.
        int nArgs = args.Length;
        IntPtr argsPtr = IntPtr.Zero;
        IntPtr namedArgsPtr = IntPtr.Zero;
        var variantSize = Marshal.SizeOf<VARIANT>();
        var allocated = new List<IntPtr>();

        try
        {
            if (nArgs > 0)
            {
                argsPtr = Marshal.AllocCoTaskMem(variantSize * nArgs);
                allocated.Add(argsPtr);
                for (int i = 0; i < nArgs; i++)
                {
                    // 역순: args[0] → 마지막 위치
                    int dst = nArgs - 1 - i;
                    var variant = ObjectToVariant(args[i]);
                    Marshal.StructureToPtr(variant, argsPtr + dst * variantSize, false);
                }
            }

            // PROPERTYPUT 의 경우 named arg "DISPID_PROPERTYPUT(-3)" 추가 필요
            int cNamedArgs = 0;
            if (namedDispIdPropPut && nArgs > 0)
            {
                namedArgsPtr = Marshal.AllocCoTaskMem(sizeof(int));
                allocated.Add(namedArgsPtr);
                Marshal.WriteInt32(namedArgsPtr, -3);  // DISPID_PROPERTYPUT
                cNamedArgs = 1;
            }

            var dp = new DISPPARAMS
            {
                rgvarg = argsPtr,
                rgdispidNamedArgs = namedArgsPtr,
                cArgs = nArgs,
                cNamedArgs = cNamedArgs,
            };

            IntPtr resultPtr = IntPtr.Zero;
            object? result = null;
            if ((wFlags & DISPATCH_PROPERTYPUT) == 0)
            {
                resultPtr = Marshal.AllocCoTaskMem(variantSize);
                allocated.Add(resultPtr);
                // VT_EMPTY 로 초기화
                Marshal.WriteInt16(resultPtr, 0);
            }

            int hr = disp.Invoke(
                dispid, ref IID_NULL, LOCALE_USER_DEFAULT, wFlags,
                ref dp, resultPtr, IntPtr.Zero, IntPtr.Zero);
            Marshal.ThrowExceptionForHR(hr);

            if (resultPtr != IntPtr.Zero)
            {
                result = Marshal.GetObjectForNativeVariant(resultPtr);
                Marshal.DestroyStructure<VARIANT>(resultPtr);
            }

            // args VARIANT cleanup
            if (argsPtr != IntPtr.Zero)
            {
                for (int i = 0; i < nArgs; i++)
                    Marshal.DestroyStructure<VARIANT>(argsPtr + i * variantSize);
            }

            return result;
        }
        finally
        {
            foreach (var p in allocated)
                Marshal.FreeCoTaskMem(p);
        }
    }

    private static VARIANT ObjectToVariant(object? value)
    {
        // .NET 의 Marshal.GetNativeVariantForObject 가 VARIANT 마샬링 자동 처리.
        var ptr = Marshal.AllocCoTaskMem(Marshal.SizeOf<VARIANT>());
        try
        {
            Marshal.GetNativeVariantForObject(value, ptr);
            return Marshal.PtrToStructure<VARIANT>(ptr);
        }
        finally
        {
            Marshal.FreeCoTaskMem(ptr);
        }
    }

    /// <summary>
    /// 진단 헬퍼 — IDispatch.GetTypeInfo 가 정상 동작하는지 확인.
    /// </summary>
    public static bool HasTypeInfo(object comObj)
    {
        if (comObj is not IDispatch disp) return false;
        int hr = disp.GetTypeInfoCount(out int count);
        if (hr != 0 || count == 0) return false;
        hr = disp.GetTypeInfo(0, LOCALE_USER_DEFAULT, out var ti);
        if (hr != 0 || ti is null) return false;
        Marshal.ReleaseComObject(ti);
        return true;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISPPARAMS
{
    public IntPtr rgvarg;
    public IntPtr rgdispidNamedArgs;
    public int cArgs;
    public int cNamedArgs;
}

// VARIANT 표준 layout. x86 = 16 byte, x64 = 24 byte. Size 명시 안 함 — runtime 이
// IntPtr 폭에 맞게 자연 size 결정. 명시 Size=16 (이전 코드) 은 x64 에서 buffer
// underrun → 한컴이 LPDISPATCH 반환 시 결과 누락 사고 (2026-05-19 검증).
[StructLayout(LayoutKind.Sequential)]
internal struct VARIANT
{
    public ushort vt;
    public ushort wReserved1;
    public ushort wReserved2;
    public ushort wReserved3;
    public IntPtr data1;   // union LowPart (LPDISPATCH, LONG, etc.)
    public IntPtr data2;   // union HighPart (DECIMAL / LARGE_INTEGER 등)
}
