import { useEffect, useRef, useState } from 'react';
import type { FormEvent, KeyboardEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';

type Cat = { id: string; name: string; slug: string; children: Cat[] };

const POPULAR = [
  'İstanbul kiralık 2+1 daire',
  'Sıfır km otomatik SUV',
  'iPhone 15 Pro',
  'Bahçeli müstakil ev',
  'MacBook Pro M3',
  'Mazda CX-5 dizel',
];

const CAT_ICONS: Record<string, string> = {
  otomobil: '🚗',
  emlak: '🏠',
  elektronik: '💻',
  'ev-yasam': '🛋️',
  'is-makineleri': '🏗️',
  hayvanlar: '🐾',
  moda: '👗',
  spor: '⚽',
};

function getCatIcon(slug: string) {
  for (const key of Object.keys(CAT_ICONS)) {
    if (slug.toLowerCase().includes(key)) return CAT_ICONS[key];
  }
  return '📦';
}

export function SearchPage() {
  const nav = useNavigate();
  const [query, setQuery] = useState('');
  const [categories, setCategories] = useState<Cat[]>([]);
  const inputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    api.get<Cat[]>('/api/categories').then((r) => setCategories(r.data)).catch(() => {});
    inputRef.current?.focus();
  }, []);

  function go(q: string) {
    const trimmed = q.trim();
    if (!trimmed) return;
    const qs = new URLSearchParams({ q: trimmed, searchMode: 'hybrid' });
    nav({ pathname: '/listings', search: qs.toString() });
  }

  function onSubmit(e: FormEvent) {
    e.preventDefault();
    go(query);
  }

  function onKey(e: KeyboardEvent<HTMLInputElement>) {
    if (e.key === 'Enter') go(query);
  }

  return (
    <div className="sp-page">
      {/* ── Hero ── */}
      <section className="sp-hero">
        <div className="sp-hero__inner">
          <p className="sp-hero__eyebrow">Yapay Zeka Destekli Arama</p>
          <h1 className="sp-hero__title">
            Aradığını doğal dilde yaz,<br />
            <span className="sp-hero__title-accent">anında bul.</span>
          </h1>

          <form className="sp-bar" onSubmit={onSubmit}>
            <div className="sp-bar__wrap">
              <span className="sp-bar__icon" aria-hidden="true">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
                  <circle cx="11" cy="11" r="8" />
                  <line x1="21" y1="21" x2="16.65" y2="16.65" />
                </svg>
              </span>
              <input
                ref={inputRef}
                className="sp-bar__input"
                type="text"
                value={query}
                onChange={(e) => setQuery(e.target.value)}
                onKeyDown={onKey}
                placeholder="Örn: Ankara'da 3+1 kiralık daire, 2022 otomatik BMW, iPhone 15…"
                autoComplete="off"
                spellCheck={false}
              />
              {query && (
                <button
                  type="button"
                  className="sp-bar__clear"
                  onClick={() => { setQuery(''); inputRef.current?.focus(); }}
                  aria-label="Temizle"
                >
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                    <line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" />
                  </svg>
                </button>
              )}
            </div>
            <button className="sp-bar__btn" type="submit" disabled={!query.trim()}>
              Ara
            </button>
          </form>

          {/* Popular searches */}
          <div className="sp-popular">
            <span className="sp-popular__label">Popüler:</span>
            <div className="sp-popular__tags">
              {POPULAR.map((p) => (
                <button key={p} type="button" className="sp-tag" onClick={() => go(p)}>
                  {p}
                </button>
              ))}
            </div>
          </div>
        </div>

        {/* Decorative blobs */}
        <div className="sp-hero__blob sp-hero__blob--1" aria-hidden="true" />
        <div className="sp-hero__blob sp-hero__blob--2" aria-hidden="true" />
      </section>

      {/* ── Categories ── */}
      {categories.length > 0 && (
        <section className="sp-cats">
          <div className="sp-cats__inner">
            <h2 className="sp-cats__title">Kategorilere Göz At</h2>
            <div className="sp-cats__grid">
              {categories.map((cat) => (
                <button
                  key={cat.id}
                  type="button"
                  className="sp-cat-card"
                  onClick={() => nav(`/listings?categoryId=${cat.id}`)}
                >
                  <span className="sp-cat-card__icon">{getCatIcon(cat.slug)}</span>
                  <span className="sp-cat-card__name">{cat.name}</span>
                  {cat.children?.length > 0 && (
                    <span className="sp-cat-card__count">{cat.children.length} alt kategori</span>
                  )}
                </button>
              ))}
            </div>
          </div>
        </section>
      )}

      {/* ── How it works ── */}
      <section className="sp-how">
        <div className="sp-how__inner">
          <h2 className="sp-how__title">Nasıl Çalışır?</h2>
          <div className="sp-how__steps">
            {[
              { n: '01', head: 'Doğal Dilde Yaz', body: 'Ne aradığını nasıl konuşuyorsan öyle yaz. Marka, model, bütçe, konum — hepsini bir cümlede.' },
              { n: '02', head: 'AI Anlasın', body: 'Yapay zekamız niyetini çıkarsar, alakalı ilanları anlamsal olarak eşleştirir.' },
              { n: '03', head: 'En İyi Eşleşmeler', body: 'Anlam bazlı sıralama ile en uygun ilanları en üstte görürsün.' },
            ].map((s) => (
              <div key={s.n} className="sp-how__step">
                <div className="sp-how__step-num">{s.n}</div>
                <h3 className="sp-how__step-head">{s.head}</h3>
                <p className="sp-how__step-body">{s.body}</p>
              </div>
            ))}
          </div>
        </div>
      </section>
    </div>
  );
}
