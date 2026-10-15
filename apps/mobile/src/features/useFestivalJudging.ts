import { onlineManager } from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ApiError } from '../api/client';
import {
  festivalScoreKey,
  loadFestivalStore,
  mergeFestival,
  pushFestival,
  saveFestivalStore,
  type FestivalCriterion,
  type FestivalSession,
  type FestivalStore,
  type LocalFestivalScore,
} from './festivalJudging';

const SYNC_DELAY_MS = 1500;

/**
 * Punten en opmerkingen van een jurylid op het Dansgardefestival (2026-10-15): direct op de telefoon bewaard en daarna in
 * de achtergrond verstuurd, ook als de verbinding pas later terugkomt (zoals bij de optocht).
 */
export function useFestivalJudging(session: FestivalSession | undefined) {
  const [store, setStore] = useState<FestivalStore>({ scores: [], comments: [] });
  const [loaded, setLoaded] = useState(false);
  const [syncing, setSyncing] = useState(false);
  const [syncFailed, setSyncFailed] = useState(false);
  const latest = useRef(store);
  useEffect(() => {
    latest.current = store;
  }, [store]);
  const festivalId = session?.festivalId;

  useEffect(() => {
    if (!session) return;
    let active = true;
    void loadFestivalStore(session.festivalId).then((local) => {
      if (!active) return;
      setStore(mergeFestival(session, local));
      setLoaded(true);
    });
    return () => {
      active = false;
    };
  }, [session]);

  useEffect(() => {
    if (loaded && festivalId) void saveFestivalStore(festivalId, store).catch(() => undefined);
  }, [loaded, festivalId, store]);

  const sync = useCallback(async () => {
    const current = latest.current;
    if (!current.scores.some((s) => !s.synced) && !current.comments.some((c) => !c.synced)) return true;
    setSyncing(true);
    try {
      const sent = await pushFestival(current);
      setStore((s) => ({
        scores: s.scores.map((x) => (sent.has(`s:${festivalScoreKey(x.entryId, x.criterion)}@${x.scoredAt}`) ? { ...x, synced: true } : x)),
        comments: s.comments.map((x) => (sent.has(`c:${x.entryId}@${x.writtenAt}`) ? { ...x, synced: true } : x)),
      }));
      setSyncFailed(false);
      return true;
    } catch (error) {
      setSyncFailed(!(error instanceof ApiError && error.status === 409));
      return false;
    } finally {
      setSyncing(false);
    }
  }, []);

  const pending = store.scores.filter((s) => !s.synced).length + store.comments.filter((c) => !c.synced).length;
  useEffect(() => {
    if (!loaded || pending === 0) return;
    const timer = setTimeout(() => void sync(), SYNC_DELAY_MS);
    return () => clearTimeout(timer);
  }, [loaded, pending, store, sync]);
  useEffect(() => onlineManager.subscribe((online) => (online ? void sync() : undefined)), [sync]);

  const setScore = useCallback((entryId: string, criterion: FestivalCriterion, value: number) => {
    const scoredAt = new Date().toISOString();
    setStore((s) => ({
      ...s,
      scores: [...s.scores.filter((x) => !(x.entryId === entryId && x.criterion === criterion)), { entryId, criterion, value, scoredAt, synced: false }],
    }));
  }, []);

  const setComment = useCallback((entryId: string, text: string) => {
    const writtenAt = new Date().toISOString();
    setStore((s) => ({ ...s, comments: [...s.comments.filter((c) => c.entryId !== entryId), { entryId, text, writtenAt, synced: false }] }));
  }, []);

  const scores = useMemo(() => new Map<string, LocalFestivalScore>(store.scores.map((s) => [festivalScoreKey(s.entryId, s.criterion), s])), [store.scores]);
  const comments = useMemo(() => new Map(store.comments.map((c) => [c.entryId, c.text])), [store.comments]);
  return { scores, comments, setScore, setComment, sync, pending, syncing, syncFailed, loaded };
}
