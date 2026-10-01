import AsyncStorage from '@react-native-async-storage/async-storage';
import type { components } from '@drammers/api-client';
import { api, unwrap } from '../api/client';

export type Criterion = components['schemas']['JudgingCriterion'];
export type JurorSession = components['schemas']['JurorSessionResponse'];
export type JudgingEntry = components['schemas']['JudgingEntryResponse'];

/** De vier criteria (fase 22), in de volgorde van het jureerscherm. */
export const criteria: { key: Criterion; label: string }[] = [
  { key: 'Originality', label: 'Originaliteit' },
  { key: 'Carnivalesque', label: 'Carnavalesk' },
  { key: 'Quality', label: 'Kwaliteit' },
  { key: 'Overall', label: 'Algemene indruk' },
];

export const passes = [1, 2, 3] as const;

/** Eén score zoals de telefoon hem bewaart; `synced` = de server heeft deze versie. */
export interface LocalScore {
  registrationId: string;
  pass: number;
  criterion: Criterion;
  value: number;
  scoredAt: string;
  synced: boolean;
}

export const scoreKey = (registrationId: string, pass: number, criterion: Criterion) => `${registrationId}|${pass}|${criterion}`;

const storageKey = (paradeId: string) => `jury:scores:${paradeId}`;

/** Scores van deze optocht op de telefoon (overleeft afsluiten en een optocht zonder netwerk). */
export async function loadLocalScores(paradeId: string): Promise<LocalScore[]> {
  try {
    const raw = await AsyncStorage.getItem(storageKey(paradeId));
    return raw ? (JSON.parse(raw) as LocalScore[]) : [];
  } catch {
    return [];
  }
}

export async function saveLocalScores(paradeId: string, scores: LocalScore[]) {
  await AsyncStorage.setItem(storageKey(paradeId), JSON.stringify(scores));
}

/** Server en telefoon samenvoegen: per score wint de nieuwste invulling (net als op de server). */
export function mergeScores(server: JurorSession['scores'], local: LocalScore[]): LocalScore[] {
  const merged = new Map<string, LocalScore>();
  for (const s of server) {
    merged.set(scoreKey(s.registrationId, s.pass, s.criterion), { ...s, synced: true });
  }
  for (const s of local) {
    const key = scoreKey(s.registrationId, s.pass, s.criterion);
    const other = merged.get(key);
    if (!other || (!s.synced && Date.parse(s.scoredAt) > Date.parse(other.scoredAt))) {
      merged.set(key, s);
    }
  }
  return [...merged.values()];
}

/**
 * Stuurt de scores die de server nog niet heeft, in porties. Geeft de nieuwe lijst terug met wat nu verstuurd is als
 * `synced`; wat tussendoor opnieuw is ingevuld blijft openstaan.
 */
export async function pushScores(paradeId: string, scores: LocalScore[]): Promise<Set<string>> {
  const pending = scores.filter((s) => !s.synced);
  const sent = new Set<string>();
  for (let i = 0; i < pending.length; i += 500) {
    const chunk = pending.slice(i, i + 500);
    await unwrap(
      api.PUT('/api/v1/jury/parades/{paradeId}/scores', {
        params: { path: { paradeId } },
        body: {
          scores: chunk.map((s) => ({ registrationId: s.registrationId, pass: s.pass, criterion: s.criterion, value: s.value, scoredAt: s.scoredAt })),
        },
      }),
    );
    chunk.forEach((s) => sent.add(`${scoreKey(s.registrationId, s.pass, s.criterion)}@${s.scoredAt}`));
  }
  return sent;
}

/** Waarde van een criterium in een passage, of `null` als die nog niet is ingevuld. */
export function valueOf(scores: Map<string, LocalScore>, registrationId: string, pass: number, criterion: Criterion): number | null {
  return scores.get(scoreKey(registrationId, pass, criterion))?.value ?? null;
}

/** Een passage is af als alle vier de criteria zijn ingevuld. */
export function passComplete(scores: Map<string, LocalScore>, registrationId: string, pass: number): boolean {
  return criteria.every((c) => scores.has(scoreKey(registrationId, pass, c.key)));
}

export const entryLabel = (e: JudgingEntry) => `${e.startNumber != null ? `nr. ${e.startNumber} ` : ''}${e.groupName}`;
