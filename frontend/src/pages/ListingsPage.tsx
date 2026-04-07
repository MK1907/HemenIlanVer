import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router-dom';
import { api } from '../api/client';

type ListingSummary = {
  id: string;
  title: string;
  price: number | null;
  currency: string;
  cityName: string;
  districtName?: string | null;
  categoryName: string;
  createdAt: string;
  primaryImageUrl?: string | null;
  viewCount: number;
};

type Paged = {
  items: ListingSummary[];
  page: number;
  pageSize: number;
  totalCount: number;
};

const SORT_OPTIONS = [
  { value: '', label: 'Varsayılan' },
  { value: 'price_asc', label: 'Fiyat: Düşükten Yükseğe' },
  { value: 'price_desc', label: 'Fiyat: Yüksekten Düşüğe' },
  { value: 'date_desc', label: 'En Yeni' },
  { value: 'date_asc', label: 'En Eski' },
];

export function ListingsPage() {
  const [params, setParams] = useSearchParams();
  const nav = useNavigate();
  const [data, setData] = useState<Paged | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [searchInput, setSearchInput] = useState(params.get('q') ?? '');

  const currentPage = Number(params.get('page') ?? '1');
  const currentSort = params.get('sort') ?? '';
  const currentQ = params.get('q') ?? '';

  useEffect(() => {
    setLoading(true);
    setError(null);
    const qs = new URLSearchParams();
    ['categoryId', 'cityId', 'minPrice', 'maxPrice', 'q', 'sort', 'filterModel', 'filterGear', 'searchMode'].forEach((k) => {
      const v = params.get(k);
      if (v) qs.set(k, v);
    });
    qs.set('page', String(currentPage));
    qs.set('pageSize', '20');
    api.get<Paged>(`/api/listings?${qs.toString()}`)
      .then((r) => setData(r.data))
      .catch(() => setError('İlanlar yüklenemedi.'))
      .finally(() => setLoading(false));
  }, [params]);

  function onSearch(e: FormEvent) {
    e.preventDefault();
    const next = new URLSearchParams(params);
    if (searchInput.trim()) next.set('q', searchInput.trim());
    else next.delete('q');
    next.set('page', '1');
    next.set('searchMode', 'hybrid');
    setParams(next);
  }

  function setSort(v: string) {
    const next = new URLSearchParams(params);
    if (v) next.set('sort', v); else next.delete('sort');
    next.set('page', '1');
    setParams(next);
  }

  function goPage(p: number) {
    const next = new URLSearchParams(params);
    next.set('page', String(p));
    setParams(next);
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  const totalPages = data ? Math.ceil(data.totalCount / data.pageSize) : 1;

  function formatPrice(price: number | null, currency: string) {
    if (price == null) return 'Fiyat sorunuz';
    return price.toLocaleString('tr-TR') + ' ' + currency;
  }

  function timeAgo(iso: string) {
    const diff = Date.now() - new Date(iso).getTime();
    const mins = Math.floor(diff / 60000);
    if (mins < 60) return `${mins} dk önce`;
    const hrs = Math.floor(mins / 60);
    if (hrs < 24) return `${hrs} sa önce`;
    return `${Math.floor(hrs / 24)} gün önce`;
  }

  return (
    <div className="lp-page">

      {/* ── Top bar ── */}
      <div className="lp-topbar">
        <div className="lp-topbar__inner">
          <form className="lp-search" onSubmit={onSearch}>
            <span className="lp-search__icon" aria-hidden="true">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
                <circle cx="11" cy="11" r="8" /><line x1="21" y1="21" x2="16.65" y2="16.65" />
              </svg>
            </span>
            <input
              className="lp-search__input"
              type="text"
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              placeholder="İlan ara… marka, model, konum"
              autoComplete="off"
            />
            <button className="lp-search__btn" type="submit">Ara</button>
          </form>

          <div className="lp-topbar__right">
            <label className="lp-sort">
              <span className="lp-sort__label">Sırala</span>
              <select className="lp-sort__select" value={currentSort} onChange={(e) => setSort(e.target.value)}>
                {SORT_OPTIONS.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}
              </select>
            </label>
            <button className="lp-create-btn" type="button" onClick={() => nav('/create')}>
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round">
                <line x1="12" y1="5" x2="12" y2="19" /><line x1="5" y1="12" x2="19" y2="12" />
              </svg>
              İlan Ver
            </button>
          </div>
        </div>
      </div>

      {/* ── Content ── */}
      <div className="lp-content">

        {/* Results header */}
        <div className="lp-results-head">
          {currentQ && (
            <div className="lp-query-tag">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" width="14" height="14">
                <circle cx="11" cy="11" r="8" /><line x1="21" y1="21" x2="16.65" y2="16.65" />
              </svg>
              <span>"{currentQ}"</span>
              <button type="button" onClick={() => { setSearchInput(''); const n = new URLSearchParams(params); n.delete('q'); n.delete('searchMode'); setParams(n); }}>
                ×
              </button>
            </div>
          )}
          <p className="lp-count">
            {loading ? 'Yükleniyor…' : data ? `${data.totalCount.toLocaleString('tr-TR')} ilan bulundu` : ''}
          </p>
        </div>

        {error && <div className="lp-empty"><p className="lp-empty__text">⚠ {error}</p></div>}

        {/* Grid */}
        {loading ? (
          <div className="lp-grid">
            {Array.from({ length: 8 }).map((_, i) => (
              <div key={i} className="lp-skeleton" />
            ))}
          </div>
        ) : data && data.items.length > 0 ? (
          <div className="lp-grid">
            {data.items.map((x) => (
              <Link key={x.id} to={`/listings/${x.id}`} className="lp-card">
                <div className="lp-card__thumb">
                  {x.primaryImageUrl
                    ? <img src={x.primaryImageUrl} alt={x.title} loading="lazy" />
                    : <span className="lp-card__no-img">📷</span>
                  }
                  <span className="lp-card__cat">{x.categoryName}</span>
                </div>
                <div className="lp-card__body">
                  <h3 className="lp-card__title">{x.title}</h3>
                  <p className="lp-card__location">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                      <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/><circle cx="12" cy="10" r="3"/>
                    </svg>
                    {x.cityName}{x.districtName ? ` / ${x.districtName}` : ''}
                  </p>
                  <div className="lp-card__foot">
                    <span className="lp-card__price">{formatPrice(x.price, x.currency)}</span>
                    <span className="lp-card__time">{timeAgo(x.createdAt)}</span>
                  </div>
                </div>
              </Link>
            ))}
          </div>
        ) : !loading && (
          <div className="lp-empty">
            <span className="lp-empty__icon">🔍</span>
            <p className="lp-empty__text">Aradığınız kriterlere uygun ilan bulunamadı.</p>
            <button type="button" className="lp-empty__btn" onClick={() => nav('/create')}>
              İlk ilanı sen ver
            </button>
          </div>
        )}

        {/* Pagination */}
        {totalPages > 1 && (
          <div className="lp-pagination">
            <button className="lp-page-btn" disabled={currentPage <= 1} onClick={() => goPage(currentPage - 1)}>
              ‹ Önceki
            </button>
            <div className="lp-page-nums">
              {Array.from({ length: Math.min(totalPages, 7) }, (_, i) => {
                let p: number;
                if (totalPages <= 7) p = i + 1;
                else if (currentPage <= 4) p = i + 1;
                else if (currentPage >= totalPages - 3) p = totalPages - 6 + i;
                else p = currentPage - 3 + i;
                return (
                  <button
                    key={p}
                    className={`lp-page-num ${p === currentPage ? 'lp-page-num--active' : ''}`}
                    onClick={() => goPage(p)}
                  >
                    {p}
                  </button>
                );
              })}
            </div>
            <button className="lp-page-btn" disabled={currentPage >= totalPages} onClick={() => goPage(currentPage + 1)}>
              Sonraki ›
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
