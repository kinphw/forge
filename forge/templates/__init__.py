"""tool2 디컴파일 메서드 1:1 재현 — '버튼 1 클릭 = tool2 메서드 1 호출'.

Forge 의 일반 렌더러(MetadataRenderer 등) 와 별개의 실험 영역. 여기 함수들은
tool2 권위 코드를 그대로 옮긴 것이고, 사용자가 templates_tab 에서 클릭해 어떤
양식이 유용한지 선별하는 게 목적. 검증된 것은 후속 작업에서 정식 renderer 로
승격, 나머지는 정리.
"""
from .fss_tool2 import 금감_TEMPLATES

__all__ = ["금감_TEMPLATES"]
