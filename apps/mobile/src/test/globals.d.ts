/** Zie jest.setup.ts: de afmetingen die `useWindowDimensions` in tests teruggeeft. */
declare global {
  var __testWindow: { width: number; height: number; scale: number; fontScale: number };
}

export {};
