import { HttpClient } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { API_BASE_URL } from '../api.config';

export interface DirectoryEntry {
  id: string;
  fullName: string;
  initials: string;
  roleLabel: string;
  roleColor: string;
  headline: string | null;
  email: string | null;
  skills: string[];
  achievementCount: number;
  hasPhoto: boolean;
  /** Changes when the picture does, so the <img> URL can be cached hard. */
  photoVersion: string | null;
  isActive: boolean;
  boardCount: number;
}

export interface ProfileSkill {
  id: string;
  name: string;
}

export interface ProfileAchievement {
  id: string;
  title: string;
  detail: string | null;
  achievedOn: string | null;
}

export interface PersonProfileCard {
  id: string;
  fullName: string;
  initials: string;
  roleLabel: string;
  roleColor: string;
  headline: string | null;
  about: string | null;
  email: string | null;
  isActive: boolean;
  hasPhoto: boolean;
  photoVersion: string | null;
  skills: ProfileSkill[];
  achievements: ProfileAchievement[];
  /** Their own profile, or the caller is an admin. The server enforces it regardless. */
  canEdit: boolean;
}

@Injectable({ providedIn: 'root' })
export class ProfilesService {
  private readonly http = inject(HttpClient);
  private readonly baseUrl = inject(API_BASE_URL);

  private get url(): string {
    return `${this.baseUrl}/profiles`;
  }

  directory(search?: string, includeInactive = false): Promise<DirectoryEntry[]> {
    const params: Record<string, string> = { includeInactive: String(includeInactive) };
    if (search?.trim()) params['q'] = search.trim();

    return firstValueFrom(this.http.get<DirectoryEntry[]>(this.url, { params }));
  }

  get(personId: string): Promise<PersonProfileCard> {
    return firstValueFrom(this.http.get<PersonProfileCard>(`${this.url}/${personId}`));
  }

  save(
    personId: string,
    body: { headline: string | null; about: string | null },
  ): Promise<PersonProfileCard> {
    return firstValueFrom(this.http.put<PersonProfileCard>(`${this.url}/${personId}`, body));
  }

  addSkills(personId: string, names: string): Promise<PersonProfileCard> {
    return firstValueFrom(
      this.http.post<PersonProfileCard>(`${this.url}/${personId}/skills`, { names }),
    );
  }

  removeSkill(personId: string, skillId: string): Promise<PersonProfileCard> {
    return firstValueFrom(
      this.http.delete<PersonProfileCard>(`${this.url}/${personId}/skills/${skillId}`),
    );
  }

  addAchievement(
    personId: string,
    body: { title: string; detail: string | null; achievedOn: string | null },
  ): Promise<PersonProfileCard> {
    return firstValueFrom(
      this.http.post<PersonProfileCard>(`${this.url}/${personId}/achievements`, body),
    );
  }

  removeAchievement(personId: string, achievementId: string): Promise<PersonProfileCard> {
    return firstValueFrom(
      this.http.delete<PersonProfileCard>(`${this.url}/${personId}/achievements/${achievementId}`),
    );
  }

  uploadPhoto(personId: string, file: File): Promise<PersonProfileCard> {
    const form = new FormData();
    form.append('file', file);

    return firstValueFrom(
      this.http.post<PersonProfileCard>(`${this.url}/${personId}/photo`, form),
    );
  }

  removePhoto(personId: string): Promise<PersonProfileCard> {
    return firstValueFrom(this.http.delete<PersonProfileCard>(`${this.url}/${personId}/photo`));
  }

  /**
   * The picture's URL. The version is in the query string rather than a cache-busting
   * random value: the same photo keeps the same URL and stays cached, a new one gets a new
   * URL and appears at once.
   */
  photoUrl(personId: string, version: string | null): string {
    return `${this.url}/${personId}/photo${version ? `?v=${version}` : ''}`;
  }
}
