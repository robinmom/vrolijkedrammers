import AsyncStorage from '@react-native-async-storage/async-storage';
import type { components } from '@drammers/api-client';
import { api, unwrap } from '../api/client';
import { storageKeys } from '../lib/storageKeys';

export type Criterion = components['schemas']['JudgingCriterion'];
export type JurorSession = components['schemas']['JurorSessionResponse'];
export type JudgingEntry = components['schemas']['JudgingEntryResponse'];
export type JudgingWeights = components['schemas']['JudgingCategoryWeightsResponse'];

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

/** Per gebruiker: een volgende gebruiker van de telefoon ziet ze niet (en bij uitloggen worden ze gewist). */
const storageKey = (userId: string, paradeId: string) => `${storageKeys.paradeJuryPrefix}${userId}:${paradeId}`;

/** Scores van deze optocht op de telefoon (overleeft afsluiten en een optocht zonder netwerk). */
export async function loadLocalScores(userId: string, paradeId: string): Promise<LocalScore[]> {
  try {
    const raw = await AsyncStorage.getItem(storageKey(userId, paradeId));
    return raw ? (JSON.parse(raw) as LocalScore[]) : [];
  } catch {
    return [];
  }
}

export async function saveLocalScores(userId: string, paradeId: string, scores: LocalScore[]) {
  await AsyncStorage.setItem(storageKey(userId, paradeId), JSON.stringify(scores));
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

/**
 * De waarde uit het laatste eerdere voorbijtrekken, als startpunt voor dit voorbijtrekken. Alleen om te tonen: zolang
 * het jurylid hem niet bevestigt blijft het criterium officieel niet ingevuld, dus hij wordt niet opgeslagen en de
 * eindcontrole blijft hem melden.
 */
export function carriedOver(
  scores: Map<string, LocalScore>,
  registrationId: string,
  pass: number,
  criterion: Criterion,
): { value: number; fromPass: number } | null {
  for (let p = pass - 1; p >= 1; p--) {
    const value = valueOf(scores, registrationId, p, criterion);
    if (value !== null) return { value, fromPass: p };
  }
  return null;
}

export const entryLabel = (e: JudgingEntry) => `${e.startNumber != null ? `nr. ${e.startNumber} ` : ''}${e.groupName}`;

/** Het eerste voorbijtrekken dat nog niet af is; is alles af, dan het eerste (om te kunnen nakijken). */
export function firstIncompletePass(scores: Map<string, LocalScore>, registrationId: string): number {
  return passes.find((p) => !passComplete(scores, registrationId, p)) ?? passes[0];
}

const weightKeys: Record<Criterion, 'originality' | 'carnivalesque' | 'quality' | 'overall'> = {
  Originality: 'originality',
  Carnivalesque: 'carnivalesque',
  Quality: 'quality',
  Overall: 'overall',
};

/**
 * Het totaal van dit jurylid voor één inzending, op dezelfde manier als de uiteindelijke uitslag: per criterium het
 * gemiddelde over de ingevulde voorbijtrekkens, maal de weging van de categorie. Een gemist voorbijtrekken verlaagt het
 * gemiddelde dus niet. `null` als er nog niets is ingevuld of de weging onbekend is.
 */
export function entryTotal(
  scores: Map<string, LocalScore>,
  entry: JudgingEntry,
  // Kan ontbreken: een sessie uit de cache van vóór deze versie heeft nog geen wegingen.
  weights: readonly JudgingWeights[] | undefined,
): number | null {
  const weight = weights?.find((w) => w.categoryId === entry.categoryId);
  if (!weight) return null;
  let total = 0;
  let scored = false;
  for (const c of criteria) {
    const values = passes.map((p) => valueOf(scores, entry.registrationId, p, c.key)).filter((v): v is number => v !== null);
    if (values.length === 0) continue;
    scored = true;
    total += (values.reduce((sum, v) => sum + v, 0) / values.length) * weight[weightKeys[c.key]];
  }
  // Eén decimaal, net als de uitslag.
  return scored ? Math.round(total * 10) / 10 : null;
}

/** Het hoogst haalbare totaal voor een categorie: 100 per criterium maal de weging. */
export function maxTotal(entry: JudgingEntry, weights: readonly JudgingWeights[] | undefined): number | null {
  const w = weights?.find((x) => x.categoryId === entry.categoryId);
  return w ? 100 * (w.originality + w.carnivalesque + w.quality + w.overall) : null;
}
