import { resolveLink } from '../lib/links';

describe('links in nieuws en activiteiten', () => {
  const album = '0192a3b4-c5d6-7e8f-9a0b-1c2d3e4f5a6b';

  it('een fotoalbum van onze site opent in de app', () => {
    expect(resolveLink(`/fotos/${album}`)).toEqual({ album });
    expect(resolveLink(`https://www.vrolijkedrammers.nl/fotos/${album}`)).toEqual({ album });
    expect(resolveLink(`https://vrolijkedrammers.nl/fotos/${album}/`)).toEqual({ album });
  });

  it('andere paden gaan naar de website, andere sites ongewijzigd', () => {
    expect(resolveLink('/dansgardefestival')).toEqual({ url: 'https://www.vrolijkedrammers.nl/dansgardefestival' });
    expect(resolveLink('https://www.vrolijkedrammers.nl/optocht')).toEqual({
      url: 'https://www.vrolijkedrammers.nl/optocht',
    });
    expect(resolveLink('https://example.com/x')).toEqual({ url: 'https://example.com/x' });
    expect(resolveLink('mailto:secretaris@vrolijkedrammers.nl')).toEqual({
      url: 'mailto:secretaris@vrolijkedrammers.nl',
    });
  });
});
