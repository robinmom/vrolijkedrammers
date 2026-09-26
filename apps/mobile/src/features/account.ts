import { api } from '../api/client';
import { clearLocalSession, getInstallationId } from '../auth/session';

export const statusLabels: Record<string, string> = {
  Active: 'Actief lid',
  Inactive: 'Niet actief',
  Suspended: 'Geschorst',
  Deceased: 'Overleden',
};

export const groupFunctionLabels: Record<string, string> = { Member: 'Lid', Lead: 'Leiding' };

/** Uitloggen: dit apparaat afmelden bij de API (als dat lukt) en daarna de tokens op het toestel wissen. */
export async function signOut(): Promise<void> {
  try {
    const current = await getInstallationId();
    const { data } = await api.GET('/api/v1/me/devices');
    const device = data?.find((d) => d.current);
    if (device) {
      await api.DELETE('/api/v1/me/devices/{id}', {
        params: { path: { id: device.id } },
        headers: { 'x-device-id': current },
      });
    }
  } catch {
    // Offline uitloggen kan altijd; het apparaat blijft dan in de lijst tot het lid het afmeldt.
  }
  await clearLocalSession();
}
