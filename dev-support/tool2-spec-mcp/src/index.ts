/**
 * tool2-spec-mcp
 * 금감원 오피스 프로그램(tool2) spec MCP 서버.
 * Forge 룰 작성 시 LLM 이 tool2 spec 을 즉시 조회하기 위한 dev-support 도구.
 * (개발 시점 전용. 환경2 배포에 포함되지 않음.)
 */
import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { z } from 'zod';
import * as dotenv from 'dotenv';
import { fileURLToPath } from 'url';
import { dirname, resolve, isAbsolute } from 'path';
import { readFileSync } from 'fs';
import {
  searchMethods, getMethod,
  listTemplates, getTemplate,
  listDirectives, getActionUsage,
  getMethodSourceRange,
} from './client/Tool2SpecDbClient.js';

const __dirname = dirname(fileURLToPath(import.meta.url));
const PROJECT_ROOT = resolve(__dirname, '../../..');
dotenv.config({ path: resolve(PROJECT_ROOT, '.env') });

const server = new McpServer({
  name: 'tool2-spec-mcp',
  version: '0.1.0',
});

// ─────────────────────────────────────────────────────────────
// 1) methods 검색 / 단건 조회 / 소스 추출
// ─────────────────────────────────────────────────────────────
server.tool(
  'search_tool2_methods',
  'tool2 (금감원 오피스 프로그램) 한컴라이브러리 의 411 메서드 카탈로그를 검색합니다. ' +
  '이름·brief 부분 매칭. 카테고리(글자/문단/문서/표/셀/블록/쪽/템플릿/마크다운/기타) ' +
  '또는 기관 접두사(금감보고서/금감원페이지/금감업무정보/금감보도자료/금감원장/...) ' +
  '필터 가능. fss=true 로 금감원* 메서드만 검색.',
  {
    query: z.string().describe('검색 키워드 (예: "자간", "글머리", "표만들기")'),
    category: z.enum([
      '글자', '문단', '문서', '표', '셀', '블록', '쪽',
      '템플릿', '마크다운', '기타',
    ]).optional().describe('카테고리 필터'),
    org: z.string().optional().describe('기관 접두사 필터 (예: "금감원페이지")'),
    fss: z.boolean().optional().describe('true 면 금감원* 메서드만'),
    limit: z.number().int().min(1).max(100).default(20),
  },
  async ({ query, category, org, fss, limit }) => {
    try {
      const rows = await searchMethods(query, { category, org, fss, limit });
      if (rows.length === 0) {
        return { content: [{ type: 'text', text: `"${query}" 매칭 메서드 없음.` }] };
      }
      const text = rows.map(r => [
        `[${r.id}] ${r.name}(${'?'.repeat(r.arg_count)})`,
        `  category=${r.category ?? '-'} ` +
          `org=${r.org_prefix ?? '-'} fss=${r.fss_specific} ` +
          `line=${r.decompiled_line ?? '-'}`,
        r.brief ? `  brief: ${r.brief}` : '',
      ].filter(Boolean).join('\n')).join('\n\n');
      return { content: [{ type: 'text', text: `총 ${rows.length}건\n\n${text}` }] };
    } catch (e) {
      return { content: [{ type: 'text', text: `오류: ${(e as Error).message}` }], isError: true };
    }
  },
);

server.tool(
  'get_tool2_method',
  'tool2 메서드 1건의 상세 정보 (인자, 사용한 self.X, 호출한 HWP COM 액션, ' +
  'decompiled.py 위치). get_tool2_method_source 와 함께 사용하면 ' +
  '메서드의 실제 소스도 가져올 수 있습니다.',
  {
    name: z.string().describe('메서드 이름 (예: "자간헌터", "금감원페이지")'),
  },
  async ({ name }) => {
    try {
      const m = await getMethod(name);
      if (!m) return { content: [{ type: 'text', text: `메서드 ${name} 을 찾을 수 없습니다.` }] };
      const lines = [
        `# ${m.name}(${m.args.join(', ')})`,
        `id: ${m.id} | category: ${m.category ?? '-'} | ` +
          `org: ${m.org_prefix ?? '-'} | fss: ${m.fss_specific}`,
        `decompiled.py: line ${m.line_start ?? m.decompiled_line ?? '-'}` +
          `${m.line_end ? ` ~ ${m.line_end}` : ''}`,
      ];
      if (m.brief) lines.push('', '## brief', m.brief);
      if (m.used_actions.length) {
        lines.push('', '## HWP COM actions used',
          m.used_actions.map(a => `- ${a}`).join('\n'));
      }
      if (m.co_names.length) {
        lines.push('', '## self.X attributes referenced',
          m.co_names.slice(0, 50).join(', ') +
          (m.co_names.length > 50 ? ` ... (+${m.co_names.length - 50})` : ''));
      }
      return { content: [{ type: 'text', text: lines.join('\n') }] };
    } catch (e) {
      return { content: [{ type: 'text', text: `오류: ${(e as Error).message}` }], isError: true };
    }
  },
);

server.tool(
  'get_tool2_method_source',
  '디컴파일된 tool2 메서드의 실제 소스 코드를 한컴라이브러리_decompiled.py 에서 ' +
  '직접 읽어옵니다. line range 는 source_refs 테이블 기준.',
  {
    name: z.string().describe('메서드 이름 (예: "자간헌터")'),
  },
  async ({ name }) => {
    try {
      const range = await getMethodSourceRange(name);
      if (!range) {
        return { content: [{ type: 'text', text: `${name} 의 source range 를 찾을 수 없습니다.` }] };
      }
      const abs = isAbsolute(range.file_path)
        ? range.file_path
        : resolve(PROJECT_ROOT, range.file_path);
      const all = readFileSync(abs, 'utf-8').split(/\r?\n/);
      const slice = all.slice(range.line_start - 1, range.line_end);
      const text = `# source: ${range.file_path}\n# lines ${range.line_start}-${range.line_end}\n\n${slice.join('\n')}`;
      return { content: [{ type: 'text', text }] };
    } catch (e) {
      return { content: [{ type: 'text', text: `오류: ${(e as Error).message}` }], isError: true };
    }
  },
);

// ─────────────────────────────────────────────────────────────
// 2) templates
// ─────────────────────────────────────────────────────────────
server.tool(
  'list_tool2_templates',
  '5종 FSS 보고서 템플릿 목록과 각 템플릿의 마진/주 폰트 요약. ' +
  '상세 정보(진입 절차, 글머리 명세 등)는 get_tool2_template 으로.',
  {},
  async () => {
    try {
      const ts = await listTemplates();
      const text = ts.map(t => [
        `[${t.id}] ${t.name}  (${t.category})`,
        `  args: ${t.args.length === 0 ? '(없음)' : t.args.join(', ')}`,
        `  margins(mm): L=${t.margins_mm.L} R=${t.margins_mm.R} ` +
          `T=${t.margins_mm.T} B=${t.margins_mm.B} H=${t.margins_mm.H} F=${t.margins_mm.F}`,
        `  primary_font: ${t.primary_font ?? '-'}` +
          (t.title_font ? ` | title_font: ${t.title_font}` : ''),
      ].join('\n')).join('\n\n');
      return { content: [{ type: 'text', text: `총 ${ts.length}개 템플릿\n\n${text}` }] };
    } catch (e) {
      return { content: [{ type: 'text', text: `오류: ${(e as Error).message}` }], isError: true };
    }
  },
);

server.tool(
  'get_tool2_template',
  '템플릿 1건의 전체 spec: 마진, 주 폰트, 진입 절차(method 호출 시퀀스), ' +
  '글머리 명세(11속성 × 층위) 모두. Forge 가 같은 템플릿을 만들 때 1차 참조.',
  {
    name: z.string().describe('템플릿 이름 (금감보고서/금감원페이지/금감업무정보/금감보도자료/금감원장보고)'),
  },
  async ({ name }) => {
    try {
      const t = await getTemplate(name);
      if (!t) return { content: [{ type: 'text', text: `${name} 템플릿을 찾을 수 없습니다.` }] };
      const out: string[] = [
        `# ${t.name}  (${t.category})`,
        `id: ${t.id} | entry: ${t.entry_method}() | ` +
          `args: ${t.args.length === 0 ? '(없음)' : t.args.join(', ')}`,
        `decompiled.py: line ${t.decompiled_line ?? '-'}`,
        '',
        `## 페이지 설정`,
        `- 마진(mm): L=${t.margins_mm.L} R=${t.margins_mm.R} ` +
          `T=${t.margins_mm.T} B=${t.margins_mm.B} H=${t.margins_mm.H} F=${t.margins_mm.F}`,
        `- 주 폰트: ${t.primary_font ?? '-'}` +
          (t.title_font ? `  /  제목 폰트: ${t.title_font}` : ''),
        `- 줄간격: ${t.line_spacing ?? '-'}`,
      ];
      if (t.bullet_specs.length) {
        out.push('', `## 글머리 명세 (level별)`);
        for (const b of t.bullet_specs) {
          out.push(
            `- L${b.level} md='${b.md_glyph}' out='${b.out_glyph}' ` +
            `font=${b.font ?? '-'} size=${b.size_pt ?? '-'}pt ` +
            `indent=${b.indent_pt ?? '-'}pt bold=${b.bold} ` +
            `space_above=${b.space_above_pt ?? '-'} line_spacing=${b.line_spacing ?? '-'}% ` +
            `fixed_pre/post=${b.fixed_pre}/${b.fixed_post} ` +
            `leadin=${b.leadin_size_pt ?? '-'}pt`
          );
        }
      } else {
        out.push('', `## 글머리 명세`, '(자동 추출 안 됨 — 템플릿 본문은 글머리 메서드 대신 ' +
          'self.문장(\'□\') + self.내어쓰기(N) 등으로 인라인. ' +
          'get_tool2_method_source 로 본문 직접 확인 권장.)');
      }
      out.push('', `## 진입 절차 (총 ${t.steps.length} 단계)`);
      for (const s of t.steps) {
        const args = s.args_repr ? ` (${s.args_repr})` : ' ()';
        out.push(`${String(s.step_order).padStart(3, ' ')}. self.${s.method_name}${args}`);
      }
      if (t.notes) out.push('', `## notes`, t.notes);
      return { content: [{ type: 'text', text: out.join('\n') }] };
    } catch (e) {
      return { content: [{ type: 'text', text: `오류: ${(e as Error).message}` }], isError: true };
    }
  },
);

// ─────────────────────────────────────────────────────────────
// 3) markdown directives
// ─────────────────────────────────────────────────────────────
server.tool(
  'list_tool2_directives',
  'tool2 마크다운 directive 카탈로그 (네모/동그라미/바/소제목/표 등). ' +
  '키워드 → 출력 토큰 + Forge md spec 대응 표기.',
  {
    category: z.enum(['bullet', 'box', 'heading', 'table', 'special'])
      .optional().describe('카테고리 필터'),
  },
  async ({ category }) => {
    try {
      const ds = await listDirectives(category);
      const text = ds.map(d => {
        const head = `[${d.category}] ${d.keyword}` +
          (d.aliases.length ? ` (alias: ${d.aliases.join(', ')})` : '') +
          (d.auto_count ? ' [auto-count]' : '');
        const out = d.output_token ? `  → '${d.output_token}'` : '';
        const style = d.output_style ? `  style: ${d.output_style}` : '';
        const eq = d.forge_md_equiv ? `  Forge md ≈ ${d.forge_md_equiv}` : '';
        const desc = d.description ? `  ${d.description}` : '';
        return [head, out, style, eq, desc].filter(Boolean).join('\n');
      }).join('\n\n');
      return { content: [{ type: 'text', text: `총 ${ds.length}건\n\n${text}` }] };
    } catch (e) {
      return { content: [{ type: 'text', text: `오류: ${(e as Error).message}` }], isError: true };
    }
  },
);

// ─────────────────────────────────────────────────────────────
// 4) HWP COM action cross-ref (tool2 가 어떻게 사용하는가)
// ─────────────────────────────────────────────────────────────
server.tool(
  'get_tool2_action_usage',
  'tool2 코드에서 특정 HWP COM 액션 (예: ParagraphShape, CharShape, TableCreate)을 ' +
  '사용하는 모든 메서드 목록. 액션 자체의 정의(파라미터 등)는 hwp-api-mcp 로 조회.',
  {
    action: z.string().describe('HWP COM 액션 이름 (예: "ParagraphShape", "TableCreate", "InsertText")'),
  },
  async ({ action }) => {
    try {
      const rows = await getActionUsage(action);
      if (rows.length === 0) {
        return { content: [{ type: 'text', text: `tool2 코드에서 액션 "${action}" 사용 없음.` }] };
      }
      const text = `tool2에서 "${action}" 사용 메서드 ${rows.length}개:\n\n` +
        rows.map(r => `- ${r.method_name}` +
          (r.items.length ? ` (items: ${r.items.join(', ')})` : '')
        ).join('\n');
      return { content: [{ type: 'text', text }] };
    } catch (e) {
      return { content: [{ type: 'text', text: `오류: ${(e as Error).message}` }], isError: true };
    }
  },
);

const transport = new StdioServerTransport();
await server.connect(transport);
