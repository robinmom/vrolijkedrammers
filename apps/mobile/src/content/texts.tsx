import { createContext, useCallback, useContext, useEffect, type ReactNode } from 'react';
import { useAppTexts } from '../api/queries';

/**
 * Teksten uit het portal (Beheer → App-teksten) in de plaats zetten van die uit de app-bundel, zodat het bestuur de
 * teksten kan wijzigen zonder nieuwe app in de store.
 *
 * De vervanging gaat op de tekst zelf, niet op een sleutel: de API stuurt alleen wat er aangepast is, als
 * `{ "Lid worden": "Drammer worden" }`. Daardoor hoeft geen enkel scherm een sleutel te kennen en blijft de tekst in
 * de bron de tekst die de app zonder netwerk toont. Het portal weet welke teksten er zijn uit de catalogus die uit
 * deze bron gegenereerd wordt (apps/mobile/scripts/collect-texts.mjs).
 *
 * Wat hier dus níet onder valt: een tekst die in de app wordt samengesteld (`${aantal} dansen`), want die staat als
 * geheel nergens in de bron. Zulke teksten staan ook niet in het portal.
 */
const none: Record<string, string> = {};

const TextsContext = createContext<Record<string, string>>(none);

/** Laatste stand, voor code die buiten het tekenen om tekst nodig heeft (meldingen, zie lib/alert.ts). */
let latest: Record<string, string> = none;

export function TextsProvider({ children }: { children: ReactNode }) {
  const query = useAppTexts();
  const overrides = query.data?.texts ?? none;

  useEffect(() => {
    latest = overrides;
  }, [overrides]);

  return <TextsContext.Provider value={overrides}>{children}</TextsContext.Provider>;
}

/** Geeft een functie die een tekst uit de bundel vervangt door de aangepaste versie, als die er is. */
export function useText(): (text: string) => string {
  const overrides = useContext(TextsContext);
  return useCallback((text: string) => overrides[text] ?? text, [overrides]);
}

/** Dezelfde vervanging buiten het tekenen om; gebruik in schermen `useText()`, dat ververst wel. */
export function appText(text: string): string {
  return latest[text] ?? text;
}
