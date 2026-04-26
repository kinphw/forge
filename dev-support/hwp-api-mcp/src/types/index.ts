export type ParameterSetFlag = 'none' | 'pending' | 'required' | 'plain';
export type MemberKind = 'Method' | 'Property' | 'Event';

export interface HwpActionSummary {
  id: number;
  action_id: string;
  parameterset_id: string | null;
  parameterset_flag: ParameterSetFlag;
  description: string | null;
}

export interface HwpActionDetail extends HwpActionSummary {
  note: string | null;
  page_number: number | null;
}

export interface HwpParameterSetSummary {
  id: number;
  set_id: string;
  description: string | null;
  item_count: number;
}

export interface HwpParameterSetItem {
  item_id: string;
  item_type: string | null;
  sub_type: string | null;
  description: string | null;
  ord: number;
}

export interface HwpParameterSetDetail {
  id: number;
  set_id: string;
  description: string | null;
  section_index: number | null;
  page_number: number | null;
  items: HwpParameterSetItem[];
}

export interface HwpMemberSummary {
  id: number;
  name: string;
  kind: MemberKind;
  description: string | null;
}

export interface HwpMemberItem {
  item_id: string;
  item_type: string | null;
  description: string | null;
  ord: number;
}

export interface HwpMemberDetail {
  id: number;
  name: string;
  kind: MemberKind;
  description: string | null;
  declaration: string | null;
  parameters_text: string | null;
  return_text: string | null;
  remark: string | null;
  source_file: string | null;
  page_number: number | null;
  items: HwpMemberItem[];
}
