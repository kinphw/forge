import mysql from 'mysql2/promise';
import type {
  HwpActionDetail,
  HwpActionSummary,
  HwpMemberDetail,
  HwpMemberSummary,
  HwpParameterSetDetail,
  HwpParameterSetSummary,
  ParameterSetFlag,
  MemberKind,
} from '../types/index.js';

let pool: mysql.Pool | null = null;

function getPool(): mysql.Pool {
  if (!pool) {
    pool = mysql.createPool({
      host: process.env.MYSQL_HOST ?? 'localhost',
      port: Number(process.env.MYSQL_PORT ?? 3306),
      user: process.env.MYSQL_USER,
      password: process.env.MYSQL_PASSWORD,
      database: process.env.DB_NAME ?? 'hwp_api_db',
      waitForConnections: true,
      connectionLimit: 5,
    });
  }
  return pool;
}

function splitKeywords(query: string): string[] {
  return query.split(/\s+/).map(k => k.trim()).filter(Boolean);
}

// ─────────────────────────────────────────────────────────────
// Actions
// ─────────────────────────────────────────────────────────────
export async function searchActions(query: string, limit: number = 20): Promise<HwpActionSummary[]> {
  const keywords = splitKeywords(query);
  if (keywords.length === 0) return [];

  const params: unknown[] = [];
  const where = keywords.map(k => {
    const like = `%${k}%`;
    params.push(like, like);
    return '(action_id LIKE ? OR description LIKE ?)';
  }).join(' AND ');
  params.push(limit);

  const sql = `
    SELECT id, action_id, parameterset_id, parameterset_flag, description
    FROM hwp_actions
    WHERE ${where}
    ORDER BY action_id ASC, id ASC
    LIMIT ?
  `;
  const [rows] = await getPool().query<mysql.RowDataPacket[]>(sql, params);
  return rows.map(r => ({
    id: r.id,
    action_id: r.action_id,
    parameterset_id: r.parameterset_id ?? null,
    parameterset_flag: r.parameterset_flag as ParameterSetFlag,
    description: r.description ?? null,
  }));
}

export async function getAction(id: number): Promise<HwpActionDetail | null> {
  const [rows] = await getPool().query<mysql.RowDataPacket[]>(
    `SELECT id, action_id, parameterset_id, parameterset_flag, description, note, page_number
     FROM hwp_actions WHERE id = ? LIMIT 1`,
    [id],
  );
  if (rows.length === 0) return null;
  const r = rows[0];
  return {
    id: r.id,
    action_id: r.action_id,
    parameterset_id: r.parameterset_id ?? null,
    parameterset_flag: r.parameterset_flag as ParameterSetFlag,
    description: r.description ?? null,
    note: r.note ?? null,
    page_number: r.page_number ?? null,
  };
}

// ─────────────────────────────────────────────────────────────
// ParameterSets
// ─────────────────────────────────────────────────────────────
export async function searchParameterSets(query: string, limit: number = 20): Promise<HwpParameterSetSummary[]> {
  const keywords = splitKeywords(query);
  if (keywords.length === 0) return [];

  const params: unknown[] = [];
  const where = keywords.map(k => {
    const like = `%${k}%`;
    params.push(like, like);
    return '(p.set_id LIKE ? OR p.description LIKE ?)';
  }).join(' AND ');
  params.push(limit);

  const sql = `
    SELECT p.id, p.set_id, p.description, COUNT(i.id) AS item_count
    FROM hwp_parametersets p
    LEFT JOIN hwp_parameterset_items i ON i.parameterset_id = p.id
    WHERE ${where}
    GROUP BY p.id
    ORDER BY p.set_id ASC, p.id ASC
    LIMIT ?
  `;
  const [rows] = await getPool().query<mysql.RowDataPacket[]>(sql, params);
  return rows.map(r => ({
    id: r.id,
    set_id: r.set_id,
    description: r.description ?? null,
    item_count: Number(r.item_count ?? 0),
  }));
}

export async function getParameterSet(id: number): Promise<HwpParameterSetDetail | null> {
  const [headerRows] = await getPool().query<mysql.RowDataPacket[]>(
    `SELECT id, set_id, description, section_index, page_number
     FROM hwp_parametersets WHERE id = ? LIMIT 1`,
    [id],
  );
  if (headerRows.length === 0) return null;
  const h = headerRows[0];

  const [itemRows] = await getPool().query<mysql.RowDataPacket[]>(
    `SELECT item_id, item_type, sub_type, description, ord
     FROM hwp_parameterset_items
     WHERE parameterset_id = ?
     ORDER BY ord ASC`,
    [id],
  );

  return {
    id: h.id,
    set_id: h.set_id,
    description: h.description ?? null,
    section_index: h.section_index ?? null,
    page_number: h.page_number ?? null,
    items: itemRows.map(i => ({
      item_id: i.item_id,
      item_type: i.item_type ?? null,
      sub_type: i.sub_type ?? null,
      description: i.description ?? null,
      ord: i.ord,
    })),
  };
}

// ─────────────────────────────────────────────────────────────
// Members (HwpAutomation methods/properties/events)
// ─────────────────────────────────────────────────────────────
export async function searchMembers(query: string, limit: number = 20, kindFilter?: MemberKind): Promise<HwpMemberSummary[]> {
  const keywords = splitKeywords(query);
  if (keywords.length === 0) return [];

  const params: unknown[] = [];
  const where: string[] = [];

  for (const k of keywords) {
    const like = `%${k}%`;
    params.push(like, like);
    where.push('(name LIKE ? OR description LIKE ?)');
  }
  if (kindFilter) {
    where.push('kind = ?');
    params.push(kindFilter);
  }
  params.push(limit);

  const sql = `
    SELECT id, name, kind, description
    FROM hwp_members
    WHERE ${where.join(' AND ')}
    ORDER BY name ASC, id ASC
    LIMIT ?
  `;
  const [rows] = await getPool().query<mysql.RowDataPacket[]>(sql, params);
  return rows.map(r => ({
    id: r.id,
    name: r.name,
    kind: r.kind as MemberKind,
    description: r.description ?? null,
  }));
}

export async function getMember(id: number): Promise<HwpMemberDetail | null> {
  const [rows] = await getPool().query<mysql.RowDataPacket[]>(
    `SELECT id, name, kind, description, declaration, parameters_text,
            return_text, remark, source_file, page_number
     FROM hwp_members WHERE id = ? LIMIT 1`,
    [id],
  );
  if (rows.length === 0) return null;
  const r = rows[0];

  const [itemRows] = await getPool().query<mysql.RowDataPacket[]>(
    `SELECT item_id, item_type, description, ord
     FROM hwp_member_items
     WHERE member_id = ?
     ORDER BY ord ASC`,
    [id],
  );

  return {
    id: r.id,
    name: r.name,
    kind: r.kind as MemberKind,
    description: r.description ?? null,
    declaration: r.declaration ?? null,
    parameters_text: r.parameters_text ?? null,
    return_text: r.return_text ?? null,
    remark: r.remark ?? null,
    source_file: r.source_file ?? null,
    page_number: r.page_number ?? null,
    items: itemRows.map(i => ({
      item_id: i.item_id,
      item_type: i.item_type ?? null,
      description: i.description ?? null,
      ord: i.ord,
    })),
  };
}
