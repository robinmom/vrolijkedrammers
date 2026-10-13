import { router } from 'expo-router';
import { Linking } from 'react-native';
import { contact } from '../content/static';

const albumPath = /^\/fotos\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\/?$/i;

/**
 * Link uit een nieuwsbericht of activiteit (2026-10-13): een pad of adres op onze eigen site; een fotoalbum opent in de
 * app, de rest op de website. Andere links (ook mail en telefoon) gaan naar het systeem.
 */
export function resolveLink(href: string): { album: string } | { url: string } {
  let path: string | null = null;
  if (href.startsWith('/') && !href.startsWith('//')) {
    path = href;
  } else {
    const match = /^https?:\/\/(?:www\.)?vrolijkedrammers\.nl(\/[^?#]*)?/i.exec(href);
    if (match) path = match[1] ?? '/';
  }
  const album = path ? albumPath.exec(path) : null;
  if (album?.[1]) return { album: album[1].toLowerCase() };
  return { url: path && href.startsWith('/') ? `${contact.website}${href}` : href };
}

export function openLink(href: string) {
  const target = resolveLink(href);
  if ('album' in target) {
    router.push(`/fotos/${target.album}`);
  } else {
    void Linking.openURL(target.url);
  }
}
