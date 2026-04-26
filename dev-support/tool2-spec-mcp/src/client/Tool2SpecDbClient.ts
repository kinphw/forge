import mysql from 'mysql2/promise';
import type {
  MethodSummary, MethodDetail,
  TemplateSummary, TemplateDetail, TemplateStep, BulletSpec,
  MarkdownDirective, HwpActionUsage,
  Category, TemplateCategory, DirectiveCategory,
} from '../types/index.js';

let pool: mysql.Pool | null = null;

function getPool(): mysql.Pool {
  if (!pool) {
    pool = mysql.createPool({
      host: process.env.MYSQL_HOST ?? 'localhost',
      port: Number(process.env.MYSQL_PORT ?? 3306),
      user: process.env.MYSQL_USER,
      password: process.env.MYSQL_PASSWORD,
      database: process.env.DB_NAME ?? 'tool2_spec_db',
      charset: 'utf8mb4',
      waitForConnections: true,
      connectionLimit: 5,
    });
  }
  return pool;
}

function splitKeywords(query: string): string[] {
  return query.split(/\s+/).map(k => k.trim()).filter(Boolean);
}

function asArray(v: unknown): string[] {
  if (Array.isArray(v)) return v as string[];
  if (typeof v === 'string') {
    try { return JSON.parse(v) as string[]; }
    catch { return []; }
  }
  return [];
}

// ─────────────────────────────────────────────────────────────
// methods
// ─────────────────────────────────────────────────────────────
export async function searchMethods(
  query: string,
  opts: { category?: Category; org?: string; fss?: boolean; limit?: number } = {},
): Promise<MethodSummary[]> {
  const limit = opts.limit ?? 20;
  const keywords = splitKeywords(query);

  const where: string[] = [];
  const params: unknown[] = [];

  for (const k of keywords) {
    where.push('(name LIKE ? OR brief LIKE ?)');
    params.push(`%${k}%`, `%${k}%`);
  }
  if (opts.category) {
    where.push('category = ?');
    params.push(opts.category);
  }
  if (opts.org) {
    where.push('org_prefix = ?');
    params.push(opts.org);
  }
  if (opts.fss !== undefined) {
    where.push('fss_specific = ?');
    params.push(opts.fss ? 1 : 0);
  }

  const sql = `
    SELECT id, name, arg_count, category, fss_specific, org_prefix,
           decompiled_line, brief
    FROM methods
    ${where.length ? 'WHERE ' + where.join(' AND ') : ''}
    ORDER BY decompiled_line ASC
    LIMIT ?
  `;
  params.push(limit);

  const [rows] = await getPool().query<mysql.RowDataPacket[]>(sql, params);
  return rows.map(r => ({
    id: r.id, name: r.name, arg_count: r.arg_count,
    category: r.category, fss_specific: !!r.fss_specific,
    org_prefix: r.org_prefix, decompiled_line: r.decompiled_line,
    brief: r.brief,
  }));
}

export async function getMethod(name: string): Promise<MethodDetail | null> {
  const sql = `
    SELECT m.*, s.file_path, s.line_start, s.line_end
    FROM methods m
    LEFT JOIN source_refs s ON s.method_id = m.id AND s.source_kind = 'decompiled'
    WHERE m.name = ?
    LIMIT 1
  `;
  const [rows] = await getPool().query<mysql.RowDataPacket[]>(sql, [name]);
  if (rows.length === 0) return null;
  const r = rows[0];
  return {
    id: r.id, name: r.name, arg_count: r.arg_count,
    args: asArray(r.args_json),
    category: r.category, fss_specific: !!r.fss_specific,
    org_prefix: r.org_prefix, decompiled_line: r.decompiled_line,
    brief: r.brief,
    co_names: asArray(r.co_names_json),
    used_actions: asArray(r.used_actions),
    source_path: r.file_path, line_start: r.line_start, line_end: r.line_end,
  };
}

// ─────────────────────────────────────────────────────────────
// templates
// ─────────────────────────────────────────────────────────────
export async function listTemplates(): Promise<TemplateSummary[]> {
  const [rows] = await getPool().query<mysql.RowDataPacket[]>(
    `SELECT * FROM templates ORDER BY id`
  );
  return rows.map(r => ({
    id: r.id, name: r.name, category: r.category as TemplateCategory,
    entry_method: r.entry_method,
    args: asArray(r.args_json),
    margins_mm: {
      L: r.margin_l_mm !== null ? Number(r.margin_l_mm) : null,
      R: r.margin_r_mm !== null ? Number(r.margin_r_mm) : null,
      T: r.margin_t_mm !== null ? Number(r.margin_t_mm) : null,
      B: r.margin_b_mm !== null ? Number(r.margin_b_mm) : null,
      H: r.margin_h_mm !== null ? Number(r.margin_h_mm) : null,
      F: r.margin_f_mm !== null ? Number(r.margin_f_mm) : null,
    },
    primary_font: r.primary_font, title_font: r.title_font,
  }));
}

export async function getTemplate(name: string): Promise<TemplateDetail | null> {
  const [trows] = await getPool().query<mysql.RowDataPacket[]>(
    `SELECT * FROM templates WHERE name = ? LIMIT 1`, [name]
  );
  if (trows.length === 0) return null;
  const t = trows[0];

  const [steps] = await getPool().query<mysql.RowDataPacket[]>(
    `SELECT step_order, method_name, args_repr, purpose
     FROM template_steps
     WHERE template_id = ?
     ORDER BY step_order`,
    [t.id]
  );
  const [bullets] = await getPool().query<mysql.RowDataPacket[]>(
    `SELECT level, md_glyph, out_glyph, font, size_pt, indent_pt, bold,
            space_above_pt, line_spacing, fixed_pre, fixed_post,
            leadin_size_pt, notes
     FROM bullet_specs
     WHERE template_id = ?
     ORDER BY level`,
    [t.id]
  );

  return {
    id: t.id, name: t.name, category: t.category as TemplateCategory,
    entry_method: t.entry_method,
    args: asArray(t.args_json),
    margins_mm: {
      L: t.margin_l_mm !== null ? Number(t.margin_l_mm) : null,
      R: t.margin_r_mm !== null ? Number(t.margin_r_mm) : null,
      T: t.margin_t_mm !== null ? Number(t.margin_t_mm) : null,
      B: t.margin_b_mm !== null ? Number(t.margin_b_mm) : null,
      H: t.margin_h_mm !== null ? Number(t.margin_h_mm) : null,
      F: t.margin_f_mm !== null ? Number(t.margin_f_mm) : null,
    },
    primary_font: t.primary_font, title_font: t.title_font,
    decompiled_line: t.decompiled_line, notes: t.notes,
    line_spacing: t.line_spacing,
    steps: steps.map(s => ({
      step_order: s.step_order, method_name: s.method_name,
      args_repr: s.args_repr, purpose: s.purpose,
    })),
    bullet_specs: bullets.map(b => ({
      level: b.level, md_glyph: b.md_glyph, out_glyph: b.out_glyph,
      font: b.font,
      size_pt: b.size_pt !== null ? Number(b.size_pt) : null,
      indent_pt: b.indent_pt !== null ? Number(b.indent_pt) : null,
      bold: !!b.bold,
      space_above_pt: b.space_above_pt !== null ? Number(b.space_above_pt) : null,
      line_spacing: b.line_spacing,
      fixed_pre: b.fixed_pre, fixed_post: b.fixed_post,
      leadin_size_pt: b.leadin_size_pt !== null ? Number(b.leadin_size_pt) : null,
      notes: b.notes,
    })),
  };
}

// ─────────────────────────────────────────────────────────────
// markdown_directives
// ─────────────────────────────────────────────────────────────
export async function listDirectives(
  category?: DirectiveCategory,
): Promise<MarkdownDirective[]> {
  const sql = category
    ? `SELECT * FROM markdown_directives WHERE category = ? ORDER BY category, keyword`
    : `SELECT * FROM markdown_directives ORDER BY category, keyword`;
  const params = category ? [category] : [];
  const [rows] = await getPool().query<mysql.RowDataPacket[]>(sql, params);
  return rows.map(r => ({
    id: r.id, keyword: r.keyword,
    aliases: asArray(r.aliases_json),
    output_token: r.output_token, output_style: r.output_style,
    category: r.category as DirectiveCategory | null,
    auto_count: !!r.auto_count,
    description: r.description, forge_md_equiv: r.forge_md_equiv,
  }));
}

// ─────────────────────────────────────────────────────────────
// hwp_actions_used cross-ref
// ─────────────────────────────────────────────────────────────
export async function getActionUsage(actionName: string): Promise<HwpActionUsage[]> {
  const [rows] = await getPool().query<mysql.RowDataPacket[]>(
    `SELECT a.action_name, a.method_id, m.name AS method_name, a.items_json
     FROM hwp_actions_used a
     JOIN methods m ON m.id = a.method_id
     WHERE a.action_name = ?
     ORDER BY m.name`,
    [actionName]
  );
  return rows.map(r => ({
    action_name: r.action_name,
    method_id: r.method_id, method_name: r.method_name,
    items: asArray(r.items_json),
  }));
}

// ─────────────────────────────────────────────────────────────
// source ranges (for get_method_source)
// ─────────────────────────────────────────────────────────────
export async function getMethodSourceRange(name: string)
    : Promise<{ file_path: string; line_start: number; line_end: number } | null> {
  const [rows] = await getPool().query<mysql.RowDataPacket[]>(
    `SELECT s.file_path, s.line_start, s.line_end
     FROM methods m
     JOIN source_refs s ON s.method_id = m.id AND s.source_kind = 'decompiled'
     WHERE m.name = ?
     LIMIT 1`,
    [name]
  );
  if (rows.length === 0) return null;
  return { file_path: rows[0].file_path, line_start: rows[0].line_start, line_end: rows[0].line_end };
}
