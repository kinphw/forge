/**
 * tool2-spec-mcp 타입 정의
 * 출처: scripts/schema.sql (MariaDB tool2_spec_db)
 */

export type Category =
  | '글자' | '문단' | '문서' | '표' | '셀' | '블록' | '쪽'
  | '템플릿' | '마크다운' | '기타';

export type TemplateCategory =
  | '일반' | '원페이지' | '업무정보' | '보도자료' | '원장';

export type DirectiveCategory =
  | 'bullet' | 'box' | 'heading' | 'table' | 'special';

// methods
export interface MethodSummary {
  id: number;
  name: string;
  arg_count: number;
  category: Category | null;
  fss_specific: boolean;
  org_prefix: string | null;
  decompiled_line: number | null;
  brief: string | null;
}

export interface MethodDetail extends MethodSummary {
  args: string[];
  co_names: string[];
  used_actions: string[];
  source_path: string | null;
  line_start: number | null;
  line_end: number | null;
}

// templates
export interface TemplateSummary {
  id: number;
  name: string;
  category: TemplateCategory;
  entry_method: string;
  args: string[];
  margins_mm: { L: number | null; R: number | null;
                 T: number | null; B: number | null;
                 H: number | null; F: number | null };
  primary_font: string | null;
  title_font: string | null;
}

export interface TemplateStep {
  step_order: number;
  method_name: string;
  args_repr: string | null;
  purpose: string | null;
}

export interface TemplateDetail extends TemplateSummary {
  decompiled_line: number | null;
  notes: string | null;
  line_spacing: number | null;
  steps: TemplateStep[];
  bullet_specs: BulletSpec[];
}

// bullet_specs
export interface BulletSpec {
  level: number;
  md_glyph: string;
  out_glyph: string;
  font: string | null;
  size_pt: number | null;
  indent_pt: number | null;
  bold: boolean;
  space_above_pt: number | null;
  line_spacing: number | null;
  fixed_pre: number;
  fixed_post: number;
  leadin_size_pt: number | null;
  notes: string | null;
}

// markdown_directives
export interface MarkdownDirective {
  id: number;
  keyword: string;
  aliases: string[];
  output_token: string | null;
  output_style: string | null;
  category: DirectiveCategory | null;
  auto_count: boolean;
  description: string | null;
  forge_md_equiv: string | null;
}

// hwp_actions_used (cross-ref)
export interface HwpActionUsage {
  action_name: string;
  method_id: number;
  method_name: string;
  items: string[];
}

// source range (for get_method_source)
export interface SourceRange {
  source_kind: 'decompiled' | 'disasm';
  file_path: string;
  line_start: number;
  line_end: number;
}
