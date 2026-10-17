import { onlineManager } from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ApiError } from '../api/client';
import {
  loadLocalScores,
  mergeScores,
  pushScores,
  saveLocalScores,
  scoreKey,
  type Criterion,
  type JurorSession,
  type LocalScore,
} from './judging';

const SYNC_DELAY_MS = 1500;

/**
 * Scores van het jurylid (fase 22b): direct op de telefoon bewaard en daarna in de achtergrond verstuurd, ook als de
 * verbinding pas later terugkomt. Na indienen (409) is niets meer te wijzigen.
 */
export function useJudging(session: JurorSession | undefined, userId: string | undefined) {
  const [scores, setScores] = useState<LocalScore[]>([]);
  // Voor welke gebruiker en optocht de scores zijn ingelezen; pas dan worden ze (onder die sleutel) bewaard.
  const [loadedFor, setLoadedFor] = useState<string | null>(null);
  const [syncing, setSyncing] = useState(false);
  const [syncFailed, setSyncFailed] = useState(false);
  const latest = useRef<LocalScore[]>([]);
  useEffect(() => {
    latest.current = scores;
  }, [scores]);
  const paradeId = session?.paradeId;
  const owner = userId && paradeId ? `${userId}:${paradeId}` : null;
  const loaded = owner !== null && loadedFor === owner;

  useEffect(() => {
    if (!session || !userId) return;
    let active = true;
    void loadLocalScores(userId, session.paradeId).then((local) => {
      if (!active) return;
      setScores(mergeScores(session.scores, local));
      setLoadedFor(`${userId}:${session.paradeId}`);
    });
    return () => {
      active = false;
    };
  }, [session, userId]);

  useEffect(() => {
    if (loaded && userId && paradeId) void saveLocalScores(userId, paradeId, scores).catch(() => undefined);
  }, [loaded, userId, paradeId, scores]);

  const sync = useCallback(async () => {
    if (!paradeId || !latest.current.some((s) => !s.synced)) return true;
    setSyncing(true);
    try {
      const sent = await pushScores(paradeId, latest.current);
      setScores((current) =>
        current.map((s) => (sent.has(`${scoreKey(s.registrationId, s.pass, s.criterion)}@${s.scoredAt}`) ? { ...s, synced: true } : s)),
      );
      setSyncFailed(false);
      return true;
    } catch (error) {
      // Al ingediend (409): de server heeft het laatste woord; anders later opnieuw proberen.
      setSyncFailed(!(error instanceof ApiError && error.status === 409));
      return false;
    } finally {
      setSyncing(false);
    }
  }, [paradeId]);

  const pending = scores.filter((s) => !s.synced).length;
  useEffect(() => {
    if (!loaded || pending === 0) return;
    const timer = setTimeout(() => void sync(), SYNC_DELAY_MS);
    return () => clearTimeout(timer);
  }, [loaded, pending, scores, sync]);
  useEffect(() => onlineManager.subscribe((online) => (online ? void sync() : undefined)), [sync]);

  const setScore = useCallback((registrationId: string, pass: number, criterion: Criterion, value: number) => {
    const scoredAt = new Date().toISOString();
    setScores((current) => [
      ...current.filter((s) => !(s.registrationId === registrationId && s.pass === pass && s.criterion === criterion)),
      { registrationId, pass, criterion, value, scoredAt, synced: false },
    ]);
  }, []);

  const byKey = useMemo(() => new Map(scores.map((s) => [scoreKey(s.registrationId, s.pass, s.criterion), s])), [scores]);
  return { loaded, scores: byKey, setScore, pending, syncing, syncFailed, sync };
}
