import { InjectionToken } from '@angular/core';

/**
 * Base URL of the API. Injected rather than imported from an environment file so
 * a container image can be pointed at a different API without a rebuild.
 */
export const API_BASE_URL = new InjectionToken<string>('API_BASE_URL', {
  providedIn: 'root',
  factory: () => '/api/v1',
});
