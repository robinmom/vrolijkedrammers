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

/**
 * Een aantekening van het jurylid bij één inzending in één passage (wens 2026-11-05). Alleen voor hemzelf: bij het
 * indienen leest hij ze terug. Ze komen niet in de uitslag; het bestuur kan ze in het portal raadplegen.
 */
export interface LocalComment {
  registrationId: string;
  pass: number;
  text: string;
  writtenAt: string;
  synced: boolean;
}

export const commentKey = (registrationId: string, pass: number) => `${registrationId}|${pass}`;

/** Wat er op de telefoon staat. Een oudere versie bewaarde alleen een lijst scores; die vorm blijft werken. */
interface LocalJudging {
  scores: LocalScore[];
  comments: LocalComment[];
}

/** Per gebruiker: een volgende gebruiker van de telefoon ziet ze niet (en bij uitloggen worden ze gewist). */
const storageKey = (userId: string, paradeId: string) => `${storageKeys.paradeJuryPrefix}${userId}:${paradeId}`;

/** Scores en aantekeningen van deze optocht op de telefoon (overleeft afsluiten en een optocht zonder netwerk). */
export async function loadLocalJudging(userId: string, paradeId: string): Promise<LocalJudging> {
  try {
    const raw = await AsyncStorage.getItem(storageKey(userId, paradeId));
    if (!raw) return { scores: [], comments: [] };
    const parsed = JSON.parse(raw) as LocalScore[] | LocalJudging;
    // Vóór de aantekeningen stond hier alleen een lijst scores; die van een lopende optocht mag niet verloren gaan.
    return Array.isArray(parsed) ? { scores: parsed, comments: [] } : { scores: parsed.scores ?? [], comments: parsed.comments ?? [] };
  } catch {
    return { scores: [], comments: [] };
  }
}

export async function saveLocalJudging(userId: string, paradeId: string, judging: LocalJudging) {
  await AsyncStorage.setItem(storageKey(userId, paradeId), JSON.stringify(judging));
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

/** Samenvoegen van de aantekeningen op dezelfde manier als de scores: de nieuwste tekst wint. */
export function mergeComments(server: JurorSession['comments'], local: LocalComment[]): LocalComment[] {
  const merged = new Map<string, LocalComment>();
  for (const c of server ?? []) {
    merged.set(commentKey(c.registrationId, c.pass), { ...c, synced: true });
  }
  for (const c of local) {
    const key = commentKey(c.registrationId, c.pass);
    const other = merged.get(key);
    if (!other || (!c.synced && Date.parse(c.writtenAt) > Date.parse(other.writtenAt))) {
      merged.set(key, c);
    }
  }
  return [...merged.values()];
}

/**
 * Stuurt wat de server nog niet heeft, in porties. Geeft terug wat nu verstuurd is (als sleutel met tijdstip), zodat
 * alleen díé versie als `synced` geldt; wat tussendoor opnieuw is ingevuld blijft openstaan.
 */
export async function pushJudging(paradeId: string, scores: LocalScore[], comments: LocalComment[]): Promise<Set<string>> {
  const pendingScores = scores.filter((s) => !s.synced);
  const pendingComments = comments.filter((c) => !c.synced);
  const sent = new Set<string>();
  const batches = Math.max(1, Math.ceil(Math.max(pendingScores.length, pendingComments.length) / 500));
  for (let i = 0; i < batches; i++) {
    const scoreChunk = pendingScores.slice(i * 500, (i + 1) * 500);
    const commentChunk = pendingComments.slice(i * 500, (i + 1) * 500);
    if (scoreChunk.length === 0 && commentChunk.length === 0) continue;
    await unwrap(
      api.PUT('/api/v1/jury/parades/{paradeId}/scores', {
        params: { path: { paradeId } },
        body: {
          scores: scoreChunk.map((s) => ({ registrationId: s.registrationId, pass: s.pass, criterion: s.criterion, value: s.value, scoredAt: s.scoredAt })),
          comments: commentChunk.map((c) => ({ registrationId: c.registrationId, pass: c.pass, text: c.text, writtenAt: c.writtenAt })),
        },
      }),
    );
    scoreChunk.forEach((s) => sent.add(`${scoreKey(s.registrationId, s.pass, s.criterion)}@${s.scoredAt}`));
    commentChunk.forEach((c) => sent.add(`c:${commentKey(c.registrationId, c.pass)}@${c.writtenAt}`));
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
