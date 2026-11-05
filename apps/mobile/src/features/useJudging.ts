import { onlineManager } from '@tanstack/react-query';
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { ApiError } from '../api/client';
import {
  commentKey,
  loadLocalJudging,
  mergeComments,
  mergeScores,
  pushJudging,
  saveLocalJudging,
  scoreKey,
  type Criterion,
  type JurorSession,
  type LocalComment,
  type LocalScore,
} from './judging';

const SYNC_DELAY_MS = 1500;

/**
 * Scores van het jurylid (fase 22b): direct op de telefoon bewaard en daarna in de achtergrond verstuurd, ook als de
 * verbinding pas later terugkomt. Na indienen (409) is niets meer te wijzigen.
 */
export function useJudging(session: JurorSession | undefined, userId: string | undefined) {
  const [scores, setScores] = useState<LocalScore[]>([]);
  const [comments, setComments] = useState<LocalComment[]>([]);
  // Voor welke gebruiker en optocht de scores zijn ingelezen; pas dan worden ze (onder die sleutel) bewaard.
  const [loadedFor, setLoadedFor] = useState<string | null>(null);
  const [syncing, setSyncing] = useState(false);
  const [syncFailed, setSyncFailed] = useState(false);
  const latest = useRef<{ scores: LocalScore[]; comments: LocalComment[] }>({ scores: [], comments: [] });
  useEffect(() => {
    latest.current = { scores, comments };
  }, [scores, comments]);
  const paradeId = session?.paradeId;
  const owner = userId && paradeId ? `${userId}:${paradeId}` : null;
  const loaded = owner !== null && loadedFor === owner;

  useEffect(() => {
    if (!session || !userId) return;
    let active = true;
    void loadLocalJudging(userId, session.paradeId).then((local) => {
      if (!active) return;
      setScores(mergeScores(session.scores, local.scores));
      setComments(mergeComments(session.comments, local.comments));
      setLoadedFor(`${userId}:${session.paradeId}`);
    });
    return () => {
      active = false;
    };
  }, [session, userId]);

  useEffect(() => {
    if (loaded && userId && paradeId) void saveLocalJudging(userId, paradeId, { scores, comments }).catch(() => undefined);
  }, [loaded, userId, paradeId, scores, comments]);

  const sync = useCallback(async () => {
    const open = latest.current.scores.some((s) => !s.synced) || latest.current.comments.some((c) => !c.synced);
    if (!paradeId || !open) return true;
    setSyncing(true);
    try {
      const sent = await pushJudging(paradeId, latest.current.scores, latest.current.comments);
      setScores((current) =>
        current.map((s) => (sent.has(`${scoreKey(s.registrationId, s.pass, s.criterion)}@${s.scoredAt}`) ? { ...s, synced: true } : s)),
      );
      setComments((current) =>
        current.map((c) => (sent.has(`c:${commentKey(c.registrationId, c.pass)}@${c.writtenAt}`) ? { ...c, synced: true } : c)),
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

  const pending = scores.filter((s) => !s.synced).length + comments.filter((c) => !c.synced).length;
  useEffect(() => {
    if (!loaded || pending === 0) return;
    const timer = setTimeout(() => void sync(), SYNC_DELAY_MS);
    return () => clearTimeout(timer);
  }, [loaded, pending, scores, comments, sync]);
  useEffect(() => onlineManager.subscribe((online) => (online ? void sync() : undefined)), [sync]);

  const setScore = useCallback((registrationId: string, pass: number, criterion: Criterion, value: number) => {
    const scoredAt = new Date().toISOString();
    setScores((current) => [
      ...current.filter((s) => !(s.registrationId === registrationId && s.pass === pass && s.criterion === criterion)),
      { registrationId, pass, criterion, value, scoredAt, synced: false },
    ]);
  }, []);

  /** Een lege tekst wist de aantekening; de server doet hetzelfde. */
  const setComment = useCallback((registrationId: string, pass: number, text: string) => {
    const writtenAt = new Date().toISOString();
    setComments((current) => [
      ...current.filter((c) => !(c.registrationId === registrationId && c.pass === pass)),
      { registrationId, pass, text, writtenAt, synced: false },
    ]);
  }, []);

  const byKey = useMemo(() => new Map(scores.map((s) => [scoreKey(s.registrationId, s.pass, s.criterion), s])), [scores]);
  const commentsByKey = useMemo(() => new Map(comments.map((c) => [commentKey(c.registrationId, c.pass), c.text])), [comments]);
  return { loaded, scores: byKey, setScore, comments: commentsByKey, setComment, pending, syncing, syncFailed, sync };
}
