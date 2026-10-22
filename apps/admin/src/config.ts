/** Aanmeldinstellingen, opgehaald bij de API (zelfde portalpakket in Dev, Acc en Prod). */
export interface PortalConfig {
  clientId: string;
  authority: string;
  apiScope: string;
  /** Omgeving: 'dev', 'acc' of 'prod'; bepaalt de achtergrondkleur van het portal. */
  environment: string;
}

/** Zet de omgeving op <html> zodat de CSS buiten productie de achtergrond kleurt en een banner toont. */
export function applyEnvironment(environment: string) {
  document.documentElement.dataset.environment = environment || 'prod';
}

export async function loadPortalConfig(): Promise<PortalConfig> {
  const response = await fetch('/api/v1/portal-config');
  if (!response.ok) {
    throw new Error(`Portalconfiguratie niet beschikbaar (${response.status})`);
  }
  return (await response.json()) as PortalConfig;
}

/** End-to-endtests draaien met een nep-login en een gemockte API (VITE_E2E_AUTH=mock). */
export const isE2eMock = import.meta.env.VITE_E2E_AUTH === 'mock';
