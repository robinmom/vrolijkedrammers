import { Link, Outlet, useRouterState } from '@tanstack/react-router';
import { useEffect, useState } from 'react';
import { useMe } from '../api/hooks';
import appIcon from '../assets/app-icoon.png';
import { useAuth } from '../auth/AuthContext';
import { navSections } from '../navigation';
import { Icon } from './Icon';
import { ProblemAlert } from './ProblemAlert';

function initials(name: string | undefined): string {
  const parts = (name ?? '').split(/\s+/).filter(Boolean);
  return ((parts[0]?.[0] ?? '') + (parts.length > 1 ? (parts.at(-1)?.[0] ?? '') : '')).toUpperCase() || '?';
}

const COLLAPSED_KEY = 'dvd-menu-ingeklapt';

/** Ingeklapte menukoppen, per browser onthouden; zonder opslag (privévenster) gewoon alles open. */
function readCollapsed(): string[] {
  try {
    const value: unknown = JSON.parse(localStorage.getItem(COLLAPSED_KEY) ?? '[]');
    return Array.isArray(value) ? value.filter((v): v is string => typeof v === 'string') : [];
  } catch {
    return [];
  }
}

function writeCollapsed(sections: string[]) {
  try {
    localStorage.setItem(COLLAPSED_KEY, JSON.stringify(sections));
  } catch {
    // Opslag niet beschikbaar: alleen voor deze sessie.
  }
}

/**
 * Hoofdlayout (Figma "Beheerportal"): zijbalk met logo, menu per sectie met iconen en de aangemelde gebruiker.
 * Navigatie op basis van permissions; de UI verbergt, de API dwingt af (docs/07 §1).
 */
export function Layout() {
  const auth = useAuth();
  const me = useMe();
  const [menuOpen, setMenuOpen] = useState(false);
  const sections = navSections(me.data?.permissions ?? []);
  // Een menu-item met onderliggende menu-items (bijv. /optocht en /optocht/samenstellen) is alleen actief op zijn eigen pad.
  const allPaths = sections.flatMap((s) => s.items.map((i) => i.to));
  const exact = (to: string) => to === '/' || allPaths.some((p) => p !== to && p.startsWith(`${to}/`));
  // Menukoppen inklappen; de kop van de huidige pagina gaat vanzelf open.
  const [collapsed, setCollapsed] = useState<string[]>(readCollapsed);
  const pathname = useRouterState({ select: (state) => state.location.pathname });
  const isActive = (to: string) => pathname === to || (!exact(to) && pathname.startsWith(`${to}/`));
  const activeSection = sections.find((s) => s.items.some((i) => isActive(i.to)))?.section;
  useEffect(() => {
    if (activeSection) {
      setCollapsed((current) =>
        current.includes(activeSection) ? current.filter((s) => s !== activeSection) : current,
      );
    }
  }, [activeSection, pathname]);
  const toggle = (section: string) =>
    setCollapsed((current) => {
      const next = current.includes(section) ? current.filter((s) => s !== section) : [...current, section];
      writeCollapsed(next);
      return next;
    });
  const name = me.data?.displayName ?? auth.user?.name;
  const role = me.data?.roles[0]?.name;

  return (
    <div className="layout">
      <a className="skip-link" href="#inhoud">
        Naar de inhoud
      </a>
      <header className="sidebar">
        <div className="brand">
          <img src={appIcon} alt="" className="brand-icon" width={40} height={40} />
          <div className="brand-text">
            <p className="brand-title">De Vrolijke Drammers</p>
            <p className="brand-subtitle">Beheerportal</p>
          </div>
          <button
            type="button"
            className="button secondary small menu-toggle"
            aria-expanded={menuOpen}
            aria-controls="hoofdmenu"
            onClick={() => setMenuOpen((v) => !v)}
          >
            Menu
          </button>
        </div>
        <nav id="hoofdmenu" aria-label="Navigatie" className={menuOpen ? 'open' : undefined}>
          {me.isSuccess && sections.length === 0 ? (
            <p className="muted">Geen beheerrechten</p>
          ) : (
            sections.map(({ section, items }) => (
              <div key={section ?? 'start'} className="nav-section">
                {section ? (
                  <button
                    type="button"
                    className="nav-heading"
                    aria-expanded={!collapsed.includes(section)}
                    aria-controls={`menu-${section}`}
                    onClick={() => toggle(section)}
                  >
                    {section}
                    <Icon name="chevron" size={14} />
                  </button>
                ) : null}
                <ul
                  id={section ? `menu-${section}` : undefined}
                  hidden={section ? collapsed.includes(section) : undefined}
                >
                  {items.map((item) => (
                    <li key={item.to}>
                      <Link
                        to={item.to}
                        activeOptions={{ exact: exact(item.to) }}
                        activeProps={{ 'aria-current': 'page', className: 'active' }}
                        onClick={() => setMenuOpen(false)}
                      >
                        <Icon name={item.icon} />
                        {item.label}
                      </Link>
                    </li>
                  ))}
                </ul>
              </div>
            ))
          )}
        </nav>
        <div className="account">
          <span className="avatar" aria-hidden="true">
            {initials(name)}
          </span>
          <div className="account-name">
            <p>{name}</p>
            {role ? <p className="muted">{role}</p> : null}
          </div>
          <button type="button" className="icon-button" onClick={() => void auth.signOut()} aria-label="Afmelden" title="Afmelden">
            <Icon name="afmelden" />
          </button>
        </div>
      </header>
      <main id="inhoud" tabIndex={-1}>
        {me.isError ? <ProblemAlert error={me.error} /> : null}
        {me.isSuccess && sections.length === 0 ? <NoAccess /> : me.isSuccess ? <Outlet /> : <p>Laden…</p>}
      </main>
    </div>
  );
}

function NoAccess() {
  return (
    <section className="card">
      <h1>Geen beheerrechten</h1>
      <p>
        Je bent aangemeld, maar je account heeft geen rechten voor het beheerportal. Neem contact op met het bestuur als je
        denkt dat dit niet klopt.
      </p>
    </section>
  );
}
