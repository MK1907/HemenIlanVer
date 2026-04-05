/** Mevcut route (pathname + search) için güvenli iç yol. */
export function safeInternalPath(pathnameSearch: string): string {
  if (!pathnameSearch.startsWith('/') || pathnameSearch.startsWith('//')) return '/';
  return pathnameSearch;
}

/** Sadece uygulama içi göreli yollar; açık yönlendirme riskine karşı. */
export function safeReturnUrl(raw: string | null): string {
  if (!raw) return '/';
  try {
    const decoded = decodeURIComponent(raw);
    if (decoded.startsWith('/') && !decoded.startsWith('//')) return decoded;
  } catch {
    /* ignore */
  }
  return '/';
}
