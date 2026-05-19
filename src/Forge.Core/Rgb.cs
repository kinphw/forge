// 공용 RGB 컬러 표현.
//
// Python 원본은 tuple[int, int, int] = (250, 250, 191) 사용.
// C# 에서는 readonly record struct 로 동등 표현 — value type, immutable, by-value equality.
//
// 한/글 COM 의 RGBColor 호출은 별도 헬퍼 (ComHelpers.Rgb) 로 분리.
// Drawing.Color 와 의도적 분리 — System.Drawing 의존성 끌어들이지 않기 위해.

namespace Forge.Core;

public readonly record struct Rgb(byte R, byte G, byte B);
