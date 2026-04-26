-- =====================================================================
-- tool2-spec-mcp MariaDB schema
-- 출처: reference/tool2/_unpacked/한컴라이브러리_decompiled.py (411 메서드)
--      + 분석노트.txt §12.5
-- 용도: Forge 룰 작성 시 LLM 이 tool2 spec 을 즉시 조회 (개발 시점 전용)
-- DB  : tool2_spec_db (hwp_api_db 와 같은 MariaDB 인스턴스, 별개 DB)
-- =====================================================================

-- 사용 예 (생성):
--   mysql -u root -p -e "CREATE DATABASE tool2_spec_db
--                          CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci"
--   mysql -u root -p -e "GRANT ALL ON tool2_spec_db.* TO 'hdbuser'@'localhost'"
--   mysql -u hdbuser -p tool2_spec_db < schema.sql

SET NAMES utf8mb4;
SET CHARACTER_SET_CLIENT = utf8mb4;
SET CHARACTER_SET_CONNECTION = utf8mb4;

-- ─────────────────────────────────────────────────────────────────────
-- 1) methods : 411 메서드 카탈로그 (전체 목록)
-- ─────────────────────────────────────────────────────────────────────
DROP TABLE IF EXISTS source_refs;
DROP TABLE IF EXISTS hwp_actions_used;
DROP TABLE IF EXISTS bullet_specs;
DROP TABLE IF EXISTS template_steps;
DROP TABLE IF EXISTS templates;
DROP TABLE IF EXISTS markdown_directives;
DROP TABLE IF EXISTS methods;

CREATE TABLE methods (
    id              INT             AUTO_INCREMENT PRIMARY KEY,
    name            VARCHAR(100)    NOT NULL UNIQUE,    -- 자간헌터, 금감원페이지 등
    args_json       JSON            NOT NULL,           -- arg 이름 배열
    arg_count       INT             NOT NULL,
    category        VARCHAR(20),                        -- 글자|문단|문서|표|셀|블록|쪽|템플릿|마크다운|기타
    fss_specific    TINYINT(1)      NOT NULL DEFAULT 0, -- 1 = 금감원* 메서드
    org_prefix      VARCHAR(50),                        -- 금감원|금감보고서|금감원페이지|... or NULL
    decompiled_line INT,                                -- 한컴라이브러리_decompiled.py 의 줄 번호
    brief           TEXT,                               -- 1줄 요약 (수동 또는 자동)
    co_names_json   JSON,                               -- 메서드가 호출하는 이름들 (의존성 분석용)
    used_actions    JSON,                               -- 이 메서드가 호출하는 HWP COM 액션명 배열

    INDEX idx_methods_category   (category),
    INDEX idx_methods_org_prefix (org_prefix),
    INDEX idx_methods_fss        (fss_specific)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ─────────────────────────────────────────────────────────────────────
-- 2) templates : 5종 보고서 템플릿
--    금감보고서 / 금감원페이지 / 금감업무정보 / 금감보도자료 / 금감원장보고
-- ─────────────────────────────────────────────────────────────────────
CREATE TABLE templates (
    id              INT             AUTO_INCREMENT PRIMARY KEY,
    name            VARCHAR(100)    NOT NULL UNIQUE,    -- 금감원페이지
    category        VARCHAR(20)     NOT NULL,           -- 일반|원페이지|업무정보|보도자료|원장
    entry_method    VARCHAR(100)    NOT NULL,           -- 진입점 메서드명 (보통 name 과 동일)
    args_json       JSON            NOT NULL,           -- 예: ["이미지1","이미지2","이미지3"]

    -- 추출된 표준 명세 (코드에서 직접)
    margin_l_mm     DECIMAL(5,2),
    margin_r_mm     DECIMAL(5,2),
    margin_t_mm     DECIMAL(5,2),
    margin_b_mm     DECIMAL(5,2),
    margin_h_mm     DECIMAL(5,2),                       -- 머리말
    margin_f_mm     DECIMAL(5,2),                       -- 꼬리말
    line_spacing    INT,                                -- 본문 기본 줄간격 %
    primary_font    VARCHAR(50),                        -- 휴먼명조, HY헤드라인M 등
    title_font      VARCHAR(50),                        -- 제목용 폰트 (다를 경우)

    decompiled_line INT,
    notes           TEXT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ─────────────────────────────────────────────────────────────────────
-- 3) template_steps : 각 템플릿의 진입 절차 (ordered method calls)
--    예: 금감원페이지 → 새창() → 문서여백(20,20,8,8,8,8) → 쪽번호() → ...
-- ─────────────────────────────────────────────────────────────────────
CREATE TABLE template_steps (
    id              INT             AUTO_INCREMENT PRIMARY KEY,
    template_id     INT             NOT NULL,
    step_order      INT             NOT NULL,
    method_name     VARCHAR(100)    NOT NULL,           -- 호출되는 메서드 (methods.name 참조)
    args_repr       TEXT,                               -- 인자 표현 (예: "20, 20, 8, 8, 8, 8")
    purpose         TEXT,                               -- 이 단계가 무엇인지 (수동 주석)

    INDEX idx_steps_template (template_id, step_order),
    CONSTRAINT fk_steps_template
        FOREIGN KEY (template_id) REFERENCES templates(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ─────────────────────────────────────────────────────────────────────
-- 4) bullet_specs : 글머리 11속성 (template × md 층위)
--    금감원글머리지정(글머리, 폰트, 크기, 내어쓰기, 진하게, 위, 줄간,
--                    고정칸앞, 고정칸뒤, 여백크기) 시그니처 기반
-- ─────────────────────────────────────────────────────────────────────
CREATE TABLE bullet_specs (
    id              INT             AUTO_INCREMENT PRIMARY KEY,
    template_id     INT             NOT NULL,
    level           INT             NOT NULL,           -- 1=□큰항목 2=○중간 3=-작은 4=·더작은
    md_glyph        VARCHAR(8)      NOT NULL,           -- '□' '○' '-' '·' (Forge md spec)
    out_glyph       VARCHAR(8)      NOT NULL,           -- '□' '◦' '-' '†' (실제 출력)
    font            VARCHAR(50),                        -- 맑은 고딕, 휴먼명조 등
    size_pt         DECIMAL(5,2),
    indent_pt       DECIMAL(6,2),                       -- 음수 가능 (hanging indent)
    bold            TINYINT(1)      NOT NULL DEFAULT 0,
    space_above_pt DECIMAL(5,2),
    line_spacing    INT,                                -- %
    fixed_pre       INT             DEFAULT 0,          -- 글머리 앞 InsertFixedWidthSpace 횟수
    fixed_post      INT             DEFAULT 0,
    leadin_size_pt DECIMAL(5,2),                        -- lead-in 빈줄 글자 크기
    notes           TEXT,

    INDEX idx_bullets_template (template_id, level),
    CONSTRAINT fk_bullets_template
        FOREIGN KEY (template_id) REFERENCES templates(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ─────────────────────────────────────────────────────────────────────
-- 5) markdown_directives : tool2 마크다운 키워드 → 출력 매핑
--    한컴라이브러리.마크다운 메서드의 분기 테이블
-- ─────────────────────────────────────────────────────────────────────
CREATE TABLE markdown_directives (
    id              INT             AUTO_INCREMENT PRIMARY KEY,
    keyword         VARCHAR(50)     NOT NULL UNIQUE,    -- 네모, 동그라미, 바, 당구장, 주석1 등
    aliases_json    JSON,                               -- ["네모","사각형"] 같이
    output_token    VARCHAR(20),                        -- '□ ', ' ○ ', '   - ', '    ※ ' 등
    output_style    VARCHAR(100),                       -- 'Bold + 맑은 고딕' 등
    category        VARCHAR(20),                        -- bullet|box|heading|table|special
    auto_count      TINYINT(1)      NOT NULL DEFAULT 0, -- 1 = 소제목/소제목로마처럼 자동 카운팅
    description     TEXT,
    forge_md_equiv  VARCHAR(50)                         -- Forge md spec 의 1:1 대응 (□/○/-/·/* 등)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ─────────────────────────────────────────────────────────────────────
-- 6) hwp_actions_used : 메서드 → 사용한 HWP COM 액션 인덱스
--    methods.used_actions 의 정규화 형태. cross-ref 검색용.
--    (action 자체의 정의는 hwp-api-mcp 의 hwp_api_db 에서 조회)
-- ─────────────────────────────────────────────────────────────────────
CREATE TABLE hwp_actions_used (
    id              INT             AUTO_INCREMENT PRIMARY KEY,
    action_name     VARCHAR(100)    NOT NULL,           -- 'ParagraphShape', 'CharShape', 'InsertText' 등
    method_id       INT             NOT NULL,           -- 사용한 메서드
    items_json      JSON,                               -- ['BreakNonLatinWord', 'LineSpacing'] 등

    INDEX idx_actions_used_name   (action_name),
    INDEX idx_actions_used_method (method_id),
    CONSTRAINT fk_actions_used_method
        FOREIGN KEY (method_id) REFERENCES methods(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;


-- ─────────────────────────────────────────────────────────────────────
-- 7) source_refs : 원본 파일 참조 (decompiled vs disasm)
--    get_method_source 가 어떤 파일에서 어떤 줄 범위를 읽을지 결정용
-- ─────────────────────────────────────────────────────────────────────
CREATE TABLE source_refs (
    id              INT             AUTO_INCREMENT PRIMARY KEY,
    method_id       INT             NOT NULL,
    source_kind     VARCHAR(20)     NOT NULL,           -- 'decompiled' | 'disasm'
    file_path       VARCHAR(255)    NOT NULL,           -- 한컴라이브러리_decompiled.py 등의 상대 경로
    line_start      INT             NOT NULL,
    line_end        INT             NOT NULL,

    INDEX idx_source_method (method_id),
    CONSTRAINT fk_source_method
        FOREIGN KEY (method_id) REFERENCES methods(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
