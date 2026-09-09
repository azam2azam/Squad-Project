import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../api.config';

/** One week column of the team schedule. Percentages, so 100 is a full week. */
export interface WeekCell {
  availablePercent: number;
  committedPercent: number;
  freePercent: number;
  overCommitted: boolean;
  boards: string[];
  awayReasons: string[];
}

export interface Assignment {
  boardId: string;
  boardTitle: string;
  squadName: string;
  roleLabel: string;
  roleColor: string;
  allocationPercent: number | null;
  startsOn: string | null;
  endsOn: string | null;
}

export interface StaffRow {
  personId: string;
  fullName: string;
  initials: string;
  color: string;
  roleLabel: string;
  isActive: boolean;
  weeks: WeekCell[];
  assignments: Assignment[];
  openWorkItems: number;
  overdueWorkItems: number;
}

export interface WeekColumn {
  start: string;
  end: string;
  label: string;
  isCurrent: boolean;
}

/** How complete the underlying data is, so the page can qualify what it shows. */
export interface ScheduleCoverage {
  assignments: number;
  assignmentsWithoutAllocation: number;
  assignmentsWithoutDates: number;
  note: string;
}

export interface StaffSchedule {
  weeks: WeekColumn[];
  people: StaffRow[];
  coverage: ScheduleCoverage;
}

export interface Availability {
  id: string;
  fromDate: string;
  toDate: string;
  kind: number;
  kindLabel: string;
  kindColor: string;
  /** What remains, not what is lost: 0 is fully away. */
  capacityPercent: number;
  note: string | null;
  isCurrent: boolean;
}

export interface WorkItem {
  id: string;
  boardId: string;
  boardTitle: string;
  personId: string | null;
  personName: string | null;
  title: string;
  detail: string | null;
  status: number;
  statusLabel: string;
  statusColor: string;
  plannedStart: string | null;
  plannedEnd: string | null;
  completedOn: string | null;
  effortHours: number | null;
  isOverdue: boolean;
}

export interface PersonProfile {
  personId: string;
  fullName: string;
  initials: string;
  color: string;
  roleLabel: string;
  email: string | null;
  isActive: boolean;
  capacity: {
    availablePercent: number;
    committedPercent: number;
    freePercent: number;
    overCommitted: boolean;
    awayReasons: string[];
  };
  assignments: Assignment[];
  availability: Availability[];
  workItems: WorkItem[];
  work: {
    total: number;
    open: number;
    done: number;
    blocked: number;
    overdue: number;
    completedLast30Days: number;
  };
  activity: { at: string; boardTitle: string; boardId: string; summary: string }[];
  activityNote: string;
}

export interface StaffOption {
  value: number;
  name: string;
  label: string;
  color: string;
}

export interface SaveWorkItemRequest {
  boardId: string;
  title: string;
  personId: string | null;
  status: number;
  plannedStart: string | null;
  plannedEnd: string | null;
  effortHours: number | null;
  detail: string | null;
}

@Injectable({ providedIn: 'root' })
export class StaffService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  schedule(weeks: number, from?: string): Promise<StaffSchedule> {
    const params: Record<string, string> = { weeks: String(weeks) };
    if (from) params['from'] = from;

    return firstValueFrom(
      this.http.get<StaffSchedule>(`${this.baseUrl}/staff/schedule`, { params }),
    );
  }

  profile(personId: string): Promise<PersonProfile> {
    return firstValueFrom(this.http.get<PersonProfile>(`${this.baseUrl}/staff/${personId}`));
  }

  options(): Promise<{ availabilityKinds: StaffOption[]; workItemStatuses: StaffOption[] }> {
    return firstValueFrom(
      this.http.get<{ availabilityKinds: StaffOption[]; workItemStatuses: StaffOption[] }>(
        `${this.baseUrl}/staff/options`,
      ),
    );
  }

  /** Puts dates and an allocation on an assignment — what makes it a schedule. */
  scheduleAssignment(
    memberId: string,
    body: { allocationPercent: number | null; startsOn: string | null; endsOn: string | null },
  ): Promise<Assignment> {
    return firstValueFrom(
      this.http.put<Assignment>(`${this.baseUrl}/staff/assignments/${memberId}`, body),
    );
  }

  saveAvailability(body: {
    id: string | null;
    personId: string;
    fromDate: string;
    toDate: string;
    kind: number;
    capacityPercent: number;
    note: string | null;
  }): Promise<Availability> {
    return firstValueFrom(
      this.http.post<Availability>(`${this.baseUrl}/staff/availability`, body),
    );
  }

  deleteAvailability(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.baseUrl}/staff/availability/${id}`));
  }

  workItems(options: { boardId?: string; personId?: string; includeDone?: boolean } = {}) {
    const params: Record<string, string> = {};
    if (options.boardId) params['boardId'] = options.boardId;
    if (options.personId) params['personId'] = options.personId;
    if (options.includeDone !== undefined) params['includeDone'] = String(options.includeDone);

    return firstValueFrom(this.http.get<WorkItem[]>(`${this.baseUrl}/work-items`, { params }));
  }

  createWorkItem(body: SaveWorkItemRequest): Promise<WorkItem> {
    return firstValueFrom(this.http.post<WorkItem>(`${this.baseUrl}/work-items`, body));
  }

  updateWorkItem(id: string, body: SaveWorkItemRequest): Promise<WorkItem> {
    return firstValueFrom(this.http.put<WorkItem>(`${this.baseUrl}/work-items/${id}`, body));
  }

  deleteWorkItem(id: string): Promise<void> {
    return firstValueFrom(this.http.delete<void>(`${this.baseUrl}/work-items/${id}`));
  }
}
