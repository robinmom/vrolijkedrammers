import { carnivalMidnight, countdown, dateBlockParts, fullDate, greeting, monthSection, newsDateLong, newsDateShort, seasonLabel, timeRange } from '../dates';
import { eventBadge } from '../events';
import { decodeEntities, parseHtml } from '../html';
import { isGuid } from '../ids';
import { compareVersions, isUpdateRequired } from '../version';

describe('datums (Europe/Amsterdam, Nederlands)', () => {
  it('datumblok zoals Figma 02', () => {
    expect(dateBlockParts('2026-11-11T10:11:00Z')).toEqual({ weekday: 'WO', day: '11', month: 'NOV' });
    // 23:30 UTC op 6 februari is in Loil al zondag 7 februari.
    expect(dateBlockParts('2027-02-06T23:30:00Z')).toEqual({ weekday: 'ZO', day: '07', month: 'FEB' });
  });

  it('tijden en tijdvakken', () => {
    expect(timeRange('2026-11-11T10:11:00Z', null, false)).toBe('11:11 uur');
    expect(timeRange('2027-01-16T19:00:00Z', '2027-01-16T23:30:00Z', false)).toBe('20:00 – 00:30 uur');
    expect(timeRange('2027-01-16T19:00:00Z', null, true)).toBe('Hele dag');
    // Zomertijd: 18:00 UTC in juli = 20:00 in Loil.
    expect(timeRange('2026-07-01T18:00:00Z', null, false)).toBe('20:00 uur');
  });

  it('secties, lange datums en nieuwsdatums', () => {
    expect(monthSection('2026-11-11T10:11:00Z')).toBe('NOVEMBER 2026');
    expect(fullDate('2027-01-16T19:00:00Z')).toBe('Zaterdag 16 januari 2027');
    expect(newsDateLong('2026-09-24T08:00:00Z')).toBe('24 september 2026');
    expect(newsDateShort('2026-09-20T08:00:00Z')).toBe('20 sep 2026');
    expect(seasonLabel('2026/2027')).toBe('Seizoen 2026–2027');
  });

  it('groet per dagdeel', () => {
    expect(greeting(new Date('2026-09-25T06:00:00Z'))).toBe('Goedemorgen');
    expect(greeting(new Date('2026-09-25T12:00:00Z'))).toBe('Goedemiddag');
    expect(greeting(new Date('2026-09-25T18:00:00Z'))).toBe('Goedenavond');
  });

  it('countdown tot middernacht in Loil', () => {
    const start = carnivalMidnight('2027-02-06');
    expect(start.toISOString()).toBe('2027-02-05T23:00:00.000Z');
    expect(countdown(start, new Date('2027-02-04T21:58:59Z'))).toEqual({ days: 1, hours: 1, minutes: 1, seconds: 1 });
    expect(countdown(start, new Date('2027-02-06T00:00:00Z'))).toBeNull();
  });
});

describe('versies', () => {
  it('vergelijkt numeriek per onderdeel', () => {
    expect(compareVersions('1.2.10', '1.2.9')).toBe(1);
    expect(compareVersions('1.0', '1.0.0')).toBe(0);
    expect(isUpdateRequired('1.0.0', '1.1.0')).toBe(true);
    expect(isUpdateRequired('1.1.0', '1.1.0')).toBe(false);
  });
});

describe('deeplink-ID', () => {
  it('accepteert alleen GUIDs', () => {
    expect(isGuid('44444444-4444-4444-8444-444444444444')).toBe(true);
    expect(isGuid('../../admin')).toBe(false);
    expect(isGuid(['44444444-4444-4444-8444-444444444444'])).toBe(false);
  });
});

describe('HTML uit de API', () => {
  it('alinea’s, koppen, lijsten en opmaak', () => {
    const blocks = parseHtml('<h2>Kop</h2>\n<p>Een <strong>vette</strong> en <em>schuine</em> tekst</p>\n<ol>\n<li>een</li>\n<li>twee</li>\n</ol>\n<ul><li>los</li></ul>');
    expect(blocks.map((b) => [b.type, b.marker, b.spans.map((s) => s.text).join('')])).toEqual([
      ['heading', undefined, 'Kop'],
      ['paragraph', undefined, 'Een vette en schuine tekst'],
      ['listItem', '1.', 'een'],
      ['listItem', '2.', 'twee'],
      ['listItem', '•', 'los'],
    ]);
    expect(blocks[1]!.spans.find((s) => s.text === 'vette')?.bold).toBe(true);
    expect(blocks[1]!.spans.find((s) => s.text === 'schuine')?.italic).toBe(true);
  });

  it('alleen http(s)- en mailto-links zijn klikbaar', () => {
    const [block] = parseHtml('<p><a href="https://example.org">site</a> <a href="javascript:alert(1)">kwaad</a></p>');
    expect(block!.spans.find((s) => s.text === 'site')?.href).toBe('https://example.org');
    expect(block!.spans.find((s) => s.text === 'kwaad')?.href).toBeUndefined();
  });

  it('decodeert entiteiten', () => {
    expect(decodeEntities('R&amp;D &lt;3 &#233;&#x20AC;&nbsp;')).toBe('R&D <3 é€ ');
  });
});

describe('eventbadge', () => {
  const category = { id: 1, code: 'carnaval', name: 'Carnaval' };
  it('hoogtepunt geel, badgetekst en jeugd groen', () => {
    expect(eventBadge({ isHighlight: true, badgeText: null, category })).toEqual({ label: 'Hoogtepunt', variant: 'highlight' });
    expect(eventBadge({ isHighlight: false, badgeText: 'Gratis', category })).toEqual({ label: 'Gratis', variant: 'youth' });
    expect(eventBadge({ isHighlight: false, badgeText: null, category: { id: 2, code: 'jeugd', name: 'Jeugd' } })).toEqual({ label: 'Jeugd', variant: 'youth' });
    expect(eventBadge({ isHighlight: false, badgeText: null, category })).toBeUndefined();
  });
});
