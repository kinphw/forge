-- hwp_api_db schema
-- HWP COM API 공식 문서(4개 PDF)를 구조화 적재
-- 한컴 ParameterSet/Action/Automation/Event 4계층

USE hwp_api_db;

DROP TABLE IF EXISTS hwp_member_items;
DROP TABLE IF EXISTS hwp_members;
DROP TABLE IF EXISTS hwp_actions;
DROP TABLE IF EXISTS hwp_parameterset_items;
DROP TABLE IF EXISTS hwp_parametersets;

-- 1) HwpAutomation: HwpCtrl 객체의 메서드/프로퍼티/이벤트
CREATE TABLE hwp_members (
  id              INT AUTO_INCREMENT PRIMARY KEY,
  name            VARCHAR(255) NOT NULL,
  kind            ENUM('Method','Property','Event') NOT NULL,
  description     TEXT,
  declaration     TEXT,
  parameters_text TEXT,
  return_text     TEXT,
  remark          TEXT,
  raw_text        MEDIUMTEXT,
  source_file     VARCHAR(100),
  page_number     INT,
  INDEX idx_member_name (name),
  INDEX idx_member_kind (kind)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 1-1) HwpAutomation 멤버 본문 안의 내부 표 (예: EngineProperties Item ID/Type/Description)
CREATE TABLE hwp_member_items (
  id          INT AUTO_INCREMENT PRIMARY KEY,
  member_id   INT NOT NULL,
  item_id     VARCHAR(255) NOT NULL,
  item_type   VARCHAR(50),
  description TEXT,
  ord         INT NOT NULL DEFAULT 0,
  FOREIGN KEY (member_id) REFERENCES hwp_members(id) ON DELETE CASCADE,
  INDEX idx_member_item_member (member_id),
  INDEX idx_member_item_id (item_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 2) ActionTable: Action ID와 ParameterSet ID 매핑 카탈로그
CREATE TABLE hwp_actions (
  id                  INT AUTO_INCREMENT PRIMARY KEY,
  action_id           VARCHAR(255) NOT NULL,
  parameterset_id     VARCHAR(255),
  -- '-': ParameterSet 없음
  -- '+': 외부 미노출(추가 예정)
  -- 'Name*': ParameterSet 외부에서 만들어야 함 (HwpCtrl.Run 불가)
  parameterset_flag   ENUM('none','pending','required','plain') NOT NULL DEFAULT 'plain',
  description         TEXT,
  note                TEXT,
  page_number         INT,
  INDEX idx_action_id (action_id),
  INDEX idx_action_pset (parameterset_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 3) ParameterSetTable: ParameterSet 정의 (섹션 헤더)
CREATE TABLE hwp_parametersets (
  id              INT AUTO_INCREMENT PRIMARY KEY,
  set_id          VARCHAR(255) NOT NULL,  -- 중복 허용 (예: InsertFieldTemplate가 2개 섹션에 등장)
  description     TEXT,
  section_index   INT,
  page_number     INT,
  INDEX idx_pset_setid (set_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

-- 3-1) ParameterSet 항목 (Item ID / Type / SubType / Description)
CREATE TABLE hwp_parameterset_items (
  id              INT AUTO_INCREMENT PRIMARY KEY,
  parameterset_id INT NOT NULL,
  item_id         VARCHAR(255) NOT NULL,
  item_type       VARCHAR(50),
  sub_type        VARCHAR(100),
  description     TEXT,
  ord             INT NOT NULL DEFAULT 0,
  FOREIGN KEY (parameterset_id) REFERENCES hwp_parametersets(id) ON DELETE CASCADE,
  INDEX idx_pset_item_pset (parameterset_id),
  INDEX idx_pset_item_id (item_id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
