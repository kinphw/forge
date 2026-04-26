import { McpServer } from '@modelcontextprotocol/sdk/server/mcp.js';
import { StdioServerTransport } from '@modelcontextprotocol/sdk/server/stdio.js';
import { z } from 'zod';
import * as dotenv from 'dotenv';
import { fileURLToPath } from 'url';
import { dirname, resolve } from 'path';
import {
  searchActions, getAction,
  searchParameterSets, getParameterSet,
  searchMembers, getMember,
} from './client/HwpApiDbClient.js';

const __dirname = dirname(fileURLToPath(import.meta.url));
dotenv.config({ path: resolve(__dirname, '../../../.env') });

const server = new McpServer({
  name: 'hwp-api-mcp',
  version: '1.0.0',
});

// ─────────────────────────────────────────────────────────────
// 1) Action 검색 / 단건 조회
// ─────────────────────────────────────────────────────────────
server.tool(
  'search_hwp_action',
  '한컴 HWP COM API의 Action(메뉴 명령어) 카탈로그를 키워드로 검색합니다. ' +
  'action_id(영문)와 description(한글) 모두에서 검색하며, 공백으로 구분된 키워드는 AND 조건입니다. ' +
  '결과의 id로 get_hwp_action을 호출해 상세 확인. ' +
  'parameterset_flag: none=ParameterSet 없음, pending=외부 미노출, required=Execute 시 ParameterSet 필수, plain=ParameterSet 있으나 Run도 가능.',
  {
    query: z.string().describe('Action 검색 키워드 (예: "글자 모양", "찾기 바꾸기", "shape")'),
    limit: z.number().int().min(1).max(50).default(20).describe('최대 반환 건수 (기본 20)'),
  },
  async ({ query, limit }) => {
    try {
      const results = await searchActions(query, limit);
      if (results.length === 0) {
        return { content: [{ type: 'text', text: `"${query}"에 해당하는 Action을 찾을 수 없습니다.` }] };
      }
      const text = results.map(r => [
        `[id: ${r.id}] ${r.action_id}  (flag: ${r.parameterset_flag}` +
          (r.parameterset_id ? `, ParameterSet: ${r.parameterset_id}` : '') + ')',
        `  ${r.description ?? '(설명 없음)'}`,
      ].join('\n')).join('\n\n');
      return { content: [{ type: 'text', text: `총 ${results.length}건\n\n${text}` }] };
    } catch (error) {
      return { content: [{ type: 'text', text: `오류: ${(error as Error).message}` }], isError: true };
    }
  },
);

server.tool(
  'get_hwp_action',
  'id로 HWP Action 1건의 상세 정보를 조회합니다. ' +
  '연결된 ParameterSet ID가 있다면 search_hwp_parameterset 또는 get_hwp_parameterset으로 항목을 추가 확인하세요.',
  {
    id: z.number().int().positive().describe('Action id (search_hwp_action 결과에서 획득)'),
  },
  async ({ id }) => {
    try {
      const r = await getAction(id);
      if (!r) return { content: [{ type: 'text', text: `id ${id} Action을 찾을 수 없습니다.` }] };
      const text = [
        `# ${r.action_id}`,
        `id: ${r.id} | flag: ${r.parameterset_flag}` +
          (r.parameterset_id ? ` | ParameterSet: ${r.parameterset_id}` : ''),
        `page: ${r.page_number ?? '-'}`,
        '',
        `## Description`,
        r.description ?? '(없음)',
        r.note ? `\n## Note\n${r.note}` : '',
      ].filter(Boolean).join('\n');
      return { content: [{ type: 'text', text }] };
    } catch (error) {
      return { content: [{ type: 'text', text: `오류: ${(error as Error).message}` }], isError: true };
    }
  },
);

// ─────────────────────────────────────────────────────────────
// 2) ParameterSet 검색 / 단건 조회
// ─────────────────────────────────────────────────────────────
server.tool(
  'search_hwp_parameterset',
  'HWP API의 ParameterSet(Action 옵션 묶음) 카탈로그를 키워드로 검색합니다. ' +
  'set_id(영문)와 description(한글) 모두에서 검색. ' +
  '결과의 id로 get_hwp_parameterset을 호출해 항목(Item ID/Type/Description) 전체를 확인하세요.',
  {
    query: z.string().describe('ParameterSet 검색 키워드 (예: "찾기", "글자", "CharShape", "도형")'),
    limit: z.number().int().min(1).max(50).default(20).describe('최대 반환 건수 (기본 20)'),
  },
  async ({ query, limit }) => {
    try {
      const results = await searchParameterSets(query, limit);
      if (results.length === 0) {
        return { content: [{ type: 'text', text: `"${query}"에 해당하는 ParameterSet을 찾을 수 없습니다.` }] };
      }
      const text = results.map(r => [
        `[id: ${r.id}] ${r.set_id}  (items: ${r.item_count})`,
        `  ${r.description ?? '(설명 없음)'}`,
      ].join('\n')).join('\n\n');
      return { content: [{ type: 'text', text: `총 ${results.length}건\n\n${text}` }] };
    } catch (error) {
      return { content: [{ type: 'text', text: `오류: ${(error as Error).message}` }], isError: true };
    }
  },
);

server.tool(
  'get_hwp_parameterset',
  'id로 HWP ParameterSet 1건의 항목 전체(Item ID/Type/SubType/Description)를 조회합니다. ' +
  '같은 set_id가 다른 섹션에 중복 등재된 경우(예: InsertFieldTemplate) 별개의 id로 분리되어 있습니다.',
  {
    id: z.number().int().positive().describe('ParameterSet id (search_hwp_parameterset 결과에서 획득)'),
  },
  async ({ id }) => {
    try {
      const r = await getParameterSet(id);
      if (!r) return { content: [{ type: 'text', text: `id ${id} ParameterSet을 찾을 수 없습니다.` }] };
      const itemLines = r.items.length === 0 ? '(항목 없음)' :
        r.items.map(it => {
          const head = it.sub_type
            ? `${it.item_id}  ${it.item_type ?? '-'} (${it.sub_type})`
            : `${it.item_id}  ${it.item_type ?? '-'}`;
          return `- ${head}\n    ${it.description ?? ''}`.trimEnd();
        }).join('\n');
      const text = [
        `# ${r.set_id}`,
        `id: ${r.id} | section: ${r.section_index ?? '-'} | page: ${r.page_number ?? '-'} | items: ${r.items.length}`,
        '',
        `## Description`,
        r.description ?? '(없음)',
        '',
        `## Items`,
        itemLines,
      ].join('\n');
      return { content: [{ type: 'text', text }] };
    } catch (error) {
      return { content: [{ type: 'text', text: `오류: ${(error as Error).message}` }], isError: true };
    }
  },
);

// ─────────────────────────────────────────────────────────────
// 3) HwpAutomation Member 검색 / 단건 조회
// ─────────────────────────────────────────────────────────────
server.tool(
  'search_hwp_member',
  'HwpAutomation 객체의 Member(Method/Property/Event)를 키워드로 검색합니다. ' +
  'Action 시스템 외의 직접 호출 API(예: GetPos, MovePos, InitScan, Open, Save, EngineProperties 등)가 여기에 있습니다. ' +
  'name(영문)과 description(한글) 모두에서 검색. kind 필터로 Method/Property/Event 좁힐 수 있음.',
  {
    query: z.string().describe('Member 검색 키워드 (예: "캐럿", "InitScan", "텍스트 추출")'),
    kind: z.enum(['Method', 'Property', 'Event']).optional().describe('Method/Property/Event 필터 (선택)'),
    limit: z.number().int().min(1).max(50).default(20).describe('최대 반환 건수 (기본 20)'),
  },
  async ({ query, kind, limit }) => {
    try {
      const results = await searchMembers(query, limit, kind);
      if (results.length === 0) {
        return { content: [{ type: 'text', text: `"${query}"에 해당하는 Member를 찾을 수 없습니다.` }] };
      }
      const text = results.map(r => [
        `[id: ${r.id}] ${r.name}  (${r.kind})`,
        `  ${r.description ?? '(설명 없음)'}`,
      ].join('\n')).join('\n\n');
      return { content: [{ type: 'text', text: `총 ${results.length}건\n\n${text}` }] };
    } catch (error) {
      return { content: [{ type: 'text', text: `오류: ${(error as Error).message}` }], isError: true };
    }
  },
);

server.tool(
  'get_hwp_member',
  'id로 HwpAutomation Member 1건의 전체 시그니처(Description/Declaration/Parameters/Return/Remark)를 조회합니다. ' +
  '본문에 내부 표(Item ID/Type/Description)가 있는 경우 함께 반환합니다.',
  {
    id: z.number().int().positive().describe('Member id (search_hwp_member 결과에서 획득)'),
  },
  async ({ id }) => {
    try {
      const r = await getMember(id);
      if (!r) return { content: [{ type: 'text', text: `id ${id} Member를 찾을 수 없습니다.` }] };
      const sections: string[] = [
        `# ${r.name}(${r.kind})`,
        `id: ${r.id} | source: ${r.source_file ?? '-'} | page: ${r.page_number ?? '-'}`,
      ];
      if (r.description) sections.push('', '## Description', r.description);
      if (r.declaration) sections.push('', '## Declaration', r.declaration);
      if (r.parameters_text) sections.push('', '## Parameters', r.parameters_text);
      if (r.return_text) sections.push('', '## Return', r.return_text);
      if (r.remark) sections.push('', '## Remark', r.remark);
      if (r.items.length > 0) {
        sections.push('', '## Items (내부 표)');
        for (const it of r.items) {
          sections.push(`- ${it.item_id}  ${it.item_type ?? '-'}\n    ${it.description ?? ''}`.trimEnd());
        }
      }
      return { content: [{ type: 'text', text: sections.join('\n') }] };
    } catch (error) {
      return { content: [{ type: 'text', text: `오류: ${(error as Error).message}` }], isError: true };
    }
  },
);

const transport = new StdioServerTransport();
await server.connect(transport);
