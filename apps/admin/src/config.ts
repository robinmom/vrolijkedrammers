/** Aanmeldinstellingen, opgehaald bij de API (zelfde portalpakket in Dev, Acc en Prod). */
export interface PortalConfig {
  clientId: string;
  authority: string;
  apiScope: string;
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
