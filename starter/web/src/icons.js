/**
 * Palette and context pad icons as data URLs.
 *
 * Inline, so the bundle stays one file and nothing is fetched at runtime (the
 * WebView2 host serves the bundle from disk, and a missing icon file is an
 * empty square without an error).
 */
const SVG = {
  step: '<rect x="3" y="6" width="18" height="12" rx="2" fill="none" stroke="#222" stroke-width="1.6"/>',
  store: '<ellipse cx="12" cy="12" rx="8" ry="8" fill="none" stroke="#222" stroke-width="1.6"/>',
  flow: '<path d="M4 12h13" stroke="#222" stroke-width="1.6"/><path d="M15 8l5 4-5 4z" fill="#222"/>',
  hand: '<path d="M8 12V6a1.5 1.5 0 0 1 3 0v5M11 11V4.5a1.5 1.5 0 0 1 3 0V11M14 11V6a1.5 1.5 0 0 1 3 0v8a6 6 0 0 1-6 6h-1a6 6 0 0 1-5-3l-2.5-4a1.5 1.5 0 0 1 2.5-1.5L8 13" fill="none" stroke="#222" stroke-width="1.4"/>',
  lasso: '<rect x="4" y="4" width="16" height="16" fill="none" stroke="#222" stroke-width="1.4" stroke-dasharray="3 2"/>',
  delete: '<path d="M6 7h12M9 7V5h6v2M8 7l1 12h6l1-12" fill="none" stroke="#222" stroke-width="1.6"/>',
  value: '<text x="12" y="16" text-anchor="middle" font-size="12" font-family="sans-serif" fill="#222">123</text>',
};

export function icon(name) {
  const body = SVG[name] || '';
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24">${body}</svg>`;
  return 'data:image/svg+xml;charset=utf-8,' + encodeURIComponent(svg);
}
