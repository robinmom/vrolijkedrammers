import AsyncStorage from '@react-native-async-storage/async-storage';
import { setSessionForTest } from '../auth/session';
import { flushPhotos, pendingPhotoUri, pendingPhotos, queuePhoto } from '../features/juryPhotos';

/** Fase 22e: één foto per wagen, bewaard op het toestel tot er internet is. */

const reply = (status: number) => () => new Response(status === 201 ? JSON.stringify({ id: 'f-1' }) : null, { status });

beforeEach(async () => {
  (globalThis.fetch as jest.Mock).mockClear();
  setSessionForTest('signedIn');
  await AsyncStorage.clear();
});

describe("Juryfoto's (fase 22e)", () => {
  it('bewaart één foto per wagen; een nieuwe vervangt de vorige', async () => {
    expect(await queuePhoto('u-1', 'p-1', 'r-5', 'file:///tmp/a.jpg')).toBe(1);
    expect(await queuePhoto('u-1', 'p-1', 'r-5', 'file:///tmp/b.jpg')).toBe(1);
    expect(await queuePhoto('u-1', 'p-1', 'r-7', 'file:///tmp/c.jpg')).toBe(2);
    expect(await pendingPhotos('u-1', 'p-1')).toBe(2);
    // De voorvertoning wijst naar het bewaarde bestand van die inzending.
    expect(await pendingPhotoUri('u-1', 'p-1', 'r-5')).toContain('r-5-');
    expect(await pendingPhotoUri('u-1', 'p-1', 'r-6')).toBeNull();
  });

  it('verstuurt de wachtrij en laat een foto staan als het netwerk hapert', async () => {
    await queuePhoto('u-1', 'p-1', 'r-5', 'file:///tmp/a.jpg');
    (globalThis.fetch as jest.Mock).mockImplementation(reply(503));
    expect(await flushPhotos('u-1', 'p-1')).toBe(1);

    (globalThis.fetch as jest.Mock).mockImplementation(reply(201));
    expect(await flushPhotos('u-1', 'p-1')).toBe(0);
    const request = (globalThis.fetch as jest.Mock).mock.calls.at(-1)![0] as string;
    expect(request).toContain('/api/v1/jury/parades/p-1/entries/r-5/photo');
  });

  it('gooit een foto weg die de server definitief weigert, zodat hij niet blijft hangen', async () => {
    await queuePhoto('u-1', 'p-1', 'r-5', 'file:///tmp/a.jpg');
    // 409: de jurering is al ingediend; opnieuw proberen helpt niet.
    (globalThis.fetch as jest.Mock).mockImplementation(reply(409));
    expect(await flushPhotos('u-1', 'p-1')).toBe(0);
    expect(await pendingPhotoUri('u-1', 'p-1', 'r-5')).toBeNull();
  });

  it('houdt de wachtrij per gebruiker apart', async () => {
    await queuePhoto('u-1', 'p-1', 'r-5', 'file:///tmp/a.jpg');
    expect(await pendingPhotos('u-2', 'p-1')).toBe(0);
  });
});
