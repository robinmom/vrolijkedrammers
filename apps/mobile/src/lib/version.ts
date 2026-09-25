/** Vergelijkt semver-achtige versies ("1.2.10" > "1.2.9"); ontbrekende delen tellen als 0. */
export function compareVersions(a: string, b: string): number {
  const pa = a.split('.').map((part) => Number.parseInt(part, 10) || 0);
  const pb = b.split('.').map((part) => Number.parseInt(part, 10) || 0);
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const diff = (pa[i] ?? 0) - (pb[i] ?? 0);
    if (diff !== 0) {
      return Math.sign(diff);
    }
  }
  return 0;
}

export const isUpdateRequired = (installed: string, minimum: string) => compareVersions(installed, minimum) < 0;
