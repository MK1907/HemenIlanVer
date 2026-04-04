import { useEffect, useState, type FormEvent } from 'react';
import { Link, useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { IconBrain, IconCheck, IconMapPin, IconMegaphone, IconPaw, IconTarget } from '../components/icons';

type ListingSummary = {
  id: string;
  title: string;
  price: number | null;
  currency: string;
  cityName: string;
  districtName?: string | null;
  viewCount: number;
};

export function HomePage() {
  const nav = useNavigate();
  const [listHint, setListHint] = useState('İlanlar yükleniyor…');
  const [searchQ, setSearchQ] = useState('');
  const [items, setItems] = useState<ListingSummary[]>([]);
  const [total, setTotal] = useState(0);

  useEffect(() => {
    api
      .get<{ items: ListingSummary[]; totalCount: number }>('/api/listings?pageSize=6&page=1')
      .then((r) => {
        setItems(r.data.items);
        setTotal(r.data.totalCount);
        setListHint('Tüm ilanlar listeleniyor.');
      })
      .catch(() => {
        setListHint('Liste şu an yüklenemedi. API bağlantısını kontrol edin.');
      });
  }, []);

  function onSearch(e: FormEvent) {
    e.preventDefault();
    const q = searchQ.trim();
    nav(q ? `/listings?q=${encodeURIComponent(q)}` : '/listings');
  }

  return (
    <div className="home">
      <section className="home-hero">
        <p className="home-hero__kicker">AKILLI EŞLEŞTİRME · DOĞAL DİL · ANINDA İLETİŞİM</p>
        <h1 className="home-hero__title">Aradığın ilanı saniyeler içinde bul.</h1>
        <p className="home-hero__lead">Yapay zeka destekli arama ve güvenli mesajlaşma, tek ekranda.</p>

        <button type="button" className="home-cat-pill">
          <IconPaw className="home-cat-pill__icon" />
          Evcil Hayvan
        </button>
      </section>

      <section className="home-features">
        <article className="home-feature-card">
          <div className="home-feature-card__icon home-feature-card__icon--brain">
            <IconBrain />
          </div>
          <h3>Akıllı Arama</h3>
          <p>Doğal dilde yazın; yapay zeka niyetinizi çıkarsın.</p>
          <Link to="/search" className="home-feature-card__ex">
            Örn: Bahçelievler kiralık 2+1 daire
          </Link>
        </article>
        <article className="home-feature-card">
          <div className="home-feature-card__icon home-feature-card__icon--target">
            <IconTarget />
          </div>
          <h3>Doğru Filtreleme</h3>
          <p>Marka, model ve özellikleri netleştirin.</p>
          <Link to="/search" className="home-feature-card__ex">
            Örn: 2022 Skoda Octavia kırmızı otomatik
          </Link>
        </article>
        <article className="home-feature-card">
          <div className="home-feature-card__icon home-feature-card__icon--pin">
            <IconMapPin />
          </div>
          <h3>Konum + Bütçe</h3>
          <p>Şehir ve fiyat aralığını birlikte verin.</p>
          <Link to="/search" className="home-feature-card__ex">
            Örn: Ankara ev taşıma 12 bin TL
          </Link>
        </article>
        <article className="home-feature-card">
          <div className="home-feature-card__icon home-feature-card__icon--check">
            <IconCheck />
          </div>
          <h3>Hızlı Eşleşme</h3>
          <p>İlan metni ile sorgunuzu eşleştiririz.</p>
          <Link to="/search" className="home-feature-card__ex">
            Örn: Scottish kedi
          </Link>
        </article>
      </section>

      <section className="home-search-panel">
        <h2 className="home-search-panel__label">Akıllı Arama</h2>
        <div className="home-search-panel__status">{listHint}</div>
        <form className="home-search-panel__form" onSubmit={onSearch}>
          <input
            type="search"
            className="home-search-panel__input"
            placeholder="Örn: Acil kiralık 2+1 daire Beşiktaş"
            value={searchQ}
            onChange={(e) => setSearchQ(e.target.value)}
            aria-label="Arama"
          />
          <button type="submit" className="home-search-panel__submit">
            İlan Ara
          </button>
        </form>
      </section>

      <section className="home-results">
        <div className="home-results__head">
          <h2>Sonuçlar</h2>
          <span className="home-results__count">{total} ilan listeleniyor</span>
        </div>
        <div className="home-results__grid">
          {items.length === 0 ? (
            <p className="home-results__empty">Henüz ilan yok veya yükleniyor…</p>
          ) : (
            items.slice(0, 2).map((x) => (
              <Link key={x.id} to={`/listings/${x.id}`} className="home-listing-card">
                <div className="home-listing-card__visual">
                  <IconMegaphone className="home-listing-card__logo-icon" />
                  <span>Hemen İlan Ver</span>
                </div>
                <div className="home-listing-card__topline">
                  <span className="home-listing-card__badge">
                    {x.price != null ? `${x.price.toLocaleString('tr-TR', { minimumFractionDigits: 2 })} ${x.currency}` : 'Belirtilmedi'}
                  </span>
                </div>
                <h3 className="home-listing-card__title">{x.title}</h3>
                <p className="home-listing-card__desc">
                  {`${x.cityName}${x.districtName ? ` ${x.districtName}` : ''} bölgesinde ilan.`}
                </p>
                <p className="home-listing-card__meta">
                  {x.cityName}
                  {x.districtName ? ` / ${x.districtName}` : ''} • {x.viewCount} görüntüleme
                </p>
              </Link>
            ))
          )}
        </div>
      </section>
    </div>
  );
}
