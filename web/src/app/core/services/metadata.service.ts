import { HttpClient } from '@angular/common/http';
import { Injectable, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../api.config';
import type {
  BoardStatus,
  Capabilities,
  Metadata,
  Role,
  RoleOption,
  StatusOption,
} from '../models/board.models';

/**
 * Loads role/status reference data once at startup and exposes it as signals.
 * Components read labels and colours from here instead of hardcoding tokens,
 * so the palette has exactly one authority (the server).
 */
@Injectable({ providedIn: 'root' })
export class MetadataService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private readonly metadata = signal<Metadata | null>(null);
  private readonly capabilities = signal<Capabilities>({
    jiraSyncEnabled: false,
    serverExportEnabled: false,
  });

  readonly roles = computed<RoleOption[]>(() => this.metadata()?.roles ?? []);
  readonly statuses = computed<StatusOption[]>(() => this.metadata()?.statuses ?? []);
  readonly jiraSyncEnabled = computed(() => this.capabilities().jiraSyncEnabled);
  readonly serverExportEnabled = computed(() => this.capabilities().serverExportEnabled);
  readonly isLoaded = computed(() => this.metadata() !== null);

  /** Called by an APP_INITIALIZER so the palette is present before the first render. */
  async load(): Promise<void> {
    const [metadata, capabilities] = await Promise.all([
      firstValueFrom(this.http.get<Metadata>(`${this.baseUrl}/metadata`)),
      firstValueFrom(this.http.get<Capabilities>(`${this.baseUrl}/metadata/capabilities`)),
    ]);

    this.metadata.set(metadata);
    this.capabilities.set(capabilities);
  }

  roleOption(role: Role): RoleOption | undefined {
    return this.roles().find((r) => r.value === role);
  }

  statusOption(status: BoardStatus): StatusOption | undefined {
    return this.statuses().find((s) => s.value === status);
  }
}
