"""
마크다운 요소별 독립 렌더러 모음.

각 렌더러는 ElementRenderer를 상속받아 render() 메서드 1개를 노출.
STAGE 1 dispatcher (hwpx_writer.py)와 STAGE 3 실시간 모드 (탭 ③ 버튼) 양쪽에서
동일 인스턴스를 호출 — "한 벌로 두 모드 완전 커버" 아키텍처.

자세한 사양: spec/renderer-spec.md
"""
from .base import ElementRenderer
from .metadata import MetadataRenderer
from .section import SectionRenderer
from .subsection import SubsectionRenderer
from .bullet import BulletRenderer
from .annotation import AnnotationRenderer
from .conclusion import ConclusionRenderer
from .note_callout import NoteCalloutRenderer
from .attachment import AttachmentRenderer
from .table import TableRenderer

__all__ = [
    "ElementRenderer",
    "MetadataRenderer",
    "SectionRenderer",
    "SubsectionRenderer",
    "BulletRenderer",
    "AnnotationRenderer",
    "ConclusionRenderer",
    "NoteCalloutRenderer",
    "AttachmentRenderer",
    "TableRenderer",
]
