/**
 * Client mirrors of the API contracts in src/Application/Contracts.
 * Kept hand-written and narrow so the compiler catches drift at the call site.
 */

export type RoleName =
  | 'ProductOwner'
  | 'TechLead'
  | 'Developer'
  | 'QaEngineer'
  | 'UxDesigner'
  | 'BusinessAnalyst'
  | 'DevOps';

export type BoardStatusName = 'OnTrack' | 'AtRisk' | 'Blocked' | 'InReview' | 'Delivered';

/** Numeric enum values as persisted by the API. Do not renumber. */
export const Role = {
  ProductOwner: 0,
  TechLead: 1,
  Developer: 2,
  QaEngineer: 3,
  UxDesigner: 4,
  BusinessAnalyst: 5,
  DevOps: 6,
} as const;
export type Role = (typeof Role)[keyof typeof Role];

export const BoardStatus = {
  OnTrack: 0,
  AtRisk: 1,
  Blocked: 2,
  InReview: 3,
  Delivered: 4,
} as const;
export type BoardStatus = (typeof BoardStatus)[keyof typeof BoardStatus];

export interface RoleOption {
  value: Role;
  name: RoleName;
  label: string;
  color: string;
}

export interface StatusOption {
  value: BoardStatus;
  name: BoardStatusName;
  label: string;
  color: string;
}

/** Role and status reference data, fetched once so the palette lives in one place. */
export interface Metadata {
  roles: RoleOption[];
  statuses: StatusOption[];
}

/** Which optional integrations this deployment actually has wired up. */
export interface Capabilities {
  jiraSyncEnabled: boolean;
  serverExportEnabled: boolean;
}

export interface CompositionSegment {
  role: Role;
  label: string;
  /** Correct plural from the server, e.g. "Developers", and "DevOps" unchanged. */
  pluralLabel: string;
  color: string;
  count: number;
  /** Share of the composition bar; segments sum to exactly 100. */
  percent: number;
}

export interface Composition {
  total: number;
  /** e.g. "2 Developers · 1 QA Engineer · 1 Product Owner" */
  legendText: string;
  segments: CompositionSegment[];
}

export interface SquadMember {
  id: string;
  personId: string;
  fullName: string;
  initials: string;
  role: Role;
  roleLabel: string;
  /** Person override if set, otherwise the role colour. */
  roleColor: string;
  detail: string | null;
  allocationPercent: number | null;
  orderIndex: number;
}

export interface BoardSummary {
  id: string;
  title: string;
  product: string;
  squadName: string;
  sprint: string | null;
  status: BoardStatus;
  statusLabel: string;
  statusColor: string;
  progressPercent: number;
  memberCount: number;
  compositionLegend: string;
  orderIndex: number;
  updatedAt: string;
}

export interface BoardDetail extends Omit<BoardSummary, 'memberCount' | 'compositionLegend'> {
  blockerNote: string | null;
  velocity: number | null;
  targetDate: string | null;
  jiraProjectKey: string | null;
  jiraBoardId: string | null;
  createdBy: string;
  createdAt: string;
  members: SquadMember[];
  composition: Composition;
  /** Advisory only — never blocks a save. */
  warnings: string[];
}

export interface Person {
  id: string;
  fullName: string;
  initials: string;
  defaultRole: Role;
  defaultRoleLabel: string;
  defaultRoleColor: string;
  defaultDetail: string | null;
  email: string | null;
  avatarColorOverride: string | null;
  isActive: boolean;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
  totalPages: number;
  hasNext: boolean;
  hasPrevious: boolean;
}
