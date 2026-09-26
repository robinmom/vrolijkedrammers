import { Link, Outlet } from '@tanstack/react-router';
import { useState } from 'react';
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

/**
 * Hoofdlayout (Figma "Beheerportal"): zijbalk met logo, menu per sectie met iconen en de aangemelde gebruiker.
 * Navigatie op basis van permissions; de UI verbergt, de API dwingt af (docs/07 §1).
 */
export function Layout() {
  const auth = useAuth();
  const me = useMe();
  const [menuOpen, setMenuOpen] = useState(false);
  const sections = navSections(me.data?.permissions ?? []);
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
                {section ? <p className="nav-heading">{section}</p> : null}
                <ul>
                  {items.map((item) => (
                    <li key={item.to}>
                      <Link
                        to={item.to}
                        activeOptions={{ exact: item.to === '/' }}
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
