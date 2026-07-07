import { InjectionToken } from '@angular/core';

export interface AppConfig {
  apiBaseUrl: string;
  hubUrl: string;
}

// Local dev talks to the Kestrel host; anywhere else talks to the deployed API.
// (The backend's CORS allows any localhost origin plus the configured production origins.)
function resolveApiBase(): string {
  const host = typeof window !== 'undefined' ? window.location.hostname : '';
  if (host === 'localhost' || host === '127.0.0.1') {
    return 'http://localhost:5199';
  }
  return 'https://api.cachescope.dev';
}

export const APP_CONFIG = new InjectionToken<AppConfig>('APP_CONFIG', {
  providedIn: 'root',
  factory: () => {
    const apiBaseUrl = resolveApiBase();
    return { apiBaseUrl, hubUrl: `${apiBaseUrl}/hubs/traces` };
  },
});
