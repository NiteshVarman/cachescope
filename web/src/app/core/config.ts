import { InjectionToken } from '@angular/core';

export interface AppConfig {
  apiBaseUrl: string;
  hubUrl: string;
}

// Dev points at the Kestrel host; the backend's dev CORS policy allows http://localhost:4200.
export const APP_CONFIG = new InjectionToken<AppConfig>('APP_CONFIG', {
  providedIn: 'root',
  factory: () => ({
    apiBaseUrl: 'http://localhost:5199',
    hubUrl: 'http://localhost:5199/hubs/traces',
  }),
});
