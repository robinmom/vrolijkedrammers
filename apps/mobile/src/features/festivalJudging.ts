import AsyncStorage from '@react-native-async-storage/async-storage';
import type { components } from '@drammers/api-client';
import { api, unwrap } from '../api/client';

export type FestivalCriterion = components['schemas']['FestivalCriterion'];
export type FestivalSession = components['schemas']['FestivalJurySession'];
export type FestivalDance = components['schemas']['JuryDance'];
export type HeadJury = components['schemas']['HeadJuryView'];

/** De vier onderdelen van het juryformulier (2026-10-15), elk 0 tot en met 10; bij solo heet het eerste Ritme. */
export const festivalCriteria = (solo: boolean): { key: FestivalCriterion; label: string }[] => [
  { key: 'Synchronicity', label: solo ? 'Ritme' : 'Synchroniteit' },
  { key: 'Choreography', label: 'Choreografie' },
  { key: 'Execution', label: 'Uitvoering' },
  { key: 'Difficulty', label: 'Moeilijkheidsgraad' },
];

export const MAX_SCORE = 10;

/** Punten zoals de telefoon ze bewaart; `synced` = de server heeft deze versie. */
export interface LocalFestivalScore {
  entryId: string;
  criterion: FestivalCriterion;
  value: number;
  scoredAt: string;
  synced: boolean;
}

export interface LocalComment {
  entryId: string;
  text: string;
  writtenAt: string;
  synced: boolean;
}

export interface FestivalStore {
  scores: LocalFestivalScore[];
  comments: LocalComment[];
}

export const festivalScoreKey = (entryId: string, criterion: FestivalCriterion) => `${entryId}|${criterion}`;

const storageKey = (festivalId: string) => `festival-jury:${festivalId}`;

/** Punten en opmerkingen van dit festival op de telefoon (overleeft afsluiten en een zaal zonder netwerk). */
export async function loadFestivalStore(festivalId: string): Promise<FestivalStore> {
  try {
    const raw = await AsyncStorage.getItem(storageKey(festivalId));
    return raw ? (JSON.parse(raw) as FestivalStore) : { scores: [], comments: [] };
  } catch {
    return { scores: [], comments: [] };
  }
}

export async function saveFestivalStore(festivalId: string, store: FestivalStore) {
  await AsyncStorage.setItem(storageKey(festivalId), JSON.stringify(store));
}

/** Server en telefoon samenvoegen: per onderdeel en per opmerking wint wat nog niet verstuurd en nieuwer is. */
export function mergeFestival(session: FestivalSession, local: FestivalStore): FestivalStore {
  const scores = new Map<string, LocalFestivalScore>();
  for (const s of session.scores) scores.set(festivalScoreKey(s.entryId, s.criterion), { ...s, synced: true });
  for (const s of local.scores) {
    const key = festivalScoreKey(s.entryId, s.criterion);
    const other = scores.get(key);
    if (!other || (!s.synced && Date.parse(s.scoredAt) > Date.parse(other.scoredAt))) scores.set(key, s);
  }
  const comments = new Map<string, LocalComment>();
  for (const c of session.comments) comments.set(c.entryId, { entryId: c.entryId, text: c.text, writtenAt: '', synced: true });
  for (const c of local.comments) if (!c.synced) comments.set(c.entryId, c);
  return { scores: [...scores.values()], comments: [...comments.values()] };
}

/** Stuurt wat de server nog niet heeft; geeft de sleutels terug van wat nu verstuurd is. */
export async function pushFestival(store: FestivalStore): Promise<Set<string>> {
  const scores = store.scores.filter((s) => !s.synced);
  const comments = store.comments.filter((c) => !c.synced);
  const sent = new Set<string>();
  if (scores.length === 0 && comments.length === 0) return sent;
  await unwrap(
    api.PUT('/api/v1/festival-jury/scores', {
      body: {
        scores: scores.map((s) => ({ entryId: s.entryId, criterion: s.criterion, value: s.value, scoredAt: s.scoredAt })),
        comments: comments.map((c) => ({ entryId: c.entryId, text: c.text })),
      },
    }),
  );
  scores.forEach((s) => sent.add(`s:${festivalScoreKey(s.entryId, s.criterion)}@${s.scoredAt}`));
  comments.forEach((c) => sent.add(`c:${c.entryId}@${c.writtenAt}`));
  return sent;
}

/** Een dans is af als alle vier de onderdelen punten hebben. */
export function danceComplete(scores: Map<string, LocalFestivalScore>, entryId: string): boolean {
  return festivalCriteria(false).every((c) => scores.has(festivalScoreKey(entryId, c.key)));
}

export function danceTotal(scores: Map<string, LocalFestivalScore>, entryId: string): number {
  return festivalCriteria(false).reduce((sum, c) => sum + (scores.get(festivalScoreKey(entryId, c.key))?.value ?? 0), 0);
}

export const danceLabel = (d: FestivalDance) => (d.title ? `${d.title} · ${d.names}` : d.names);
