import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api } from '../api/client';
import { useAuth } from '../context/AuthContext';

type Detail = {
  id: string; title: string; description: string;
  price: number | null; currency: string; listingType: string;
  categoryName: string; cityName?: string | null; districtName?: string | null;
  attributes: Record<string, string>; imageUrls: string[];
  viewCount: number; createdAt?: string;
};

type SimilarListing = {
  id: string; title: string; price: number | null; currency: string;
  cityName: string; districtName?: string | null; categoryName: string;
  primaryImageUrl?: string | null;
};

const ATTR_LABELS: Record<string, string> = {
  brand: 'Marka', model: 'Model', year: 'Yıl', km: 'Kilometre',
  fuel: 'Yakıt', gear: 'Vites', bodyType: 'Kasa Tipi',
  color: 'Renk', damage: 'Hasar Durumu',
};

function labelFor(key: string) {
  return ATTR_LABELS[key] ?? key.charAt(0).toUpperCase() + key.slice(1);
}

export function ListingDetailPage() {
  const { id } = useParams();
  const { user } = useAuth();
  const [detail, setDetail] = useState<Detail | null>(null);
  const [loading, setLoading] = useState(true);
  const [msg, setMsg] = useState('');
  const [sent, setSent] = useState(false);
  const [similar, setSimilar] = useState<SimilarListing[]>([]);
  const [activeImg, setActiveImg] = useState(0);

  useEffect(() => {
    if (!id) return;
    setLoading(true);
    api.get<Detail>(`/api/listings/${id}`)
      .then((r) => { setDetail(r.data); setActiveImg(0); })
      .finally(() => setLoading(false));
    api.get<SimilarListing[]>(`/api/listings/${id}/similar?count=6`)
      .then((r) => setSimilar(r.data)).catch(() => {});
  }, [id]);

  async function sendMessage(e: FormEvent) {
    e.preventDefault();
    if (!id || !msg.trim()) return;
    await api.post('/api/messages', { listingId: id, body: msg });
    setMsg(''); setSent(true);
  }

  if (loading) return (
    <div className="ldp-loading">
      <div className="ldp-skeleton-hero" />
    </div>
  );

  if (!detail) return (
    <div className="ldp-loading"><p>İlan bulunamadı.</p></div>
  );

  const filledAttrs = Object.entries(detail.attributes).filter(([, v]) => v && v.trim());

  return (
    <div className="ldp-page">

      {/* Breadcrumb */}
      <div className="ldp-breadcrumb">
        <div className="ldp-breadcrumb__inner">
          <Link to="/listings">İlanlar</Link>
          <span>›</span>
          <span>{detail.categoryName}</span>
          <span>›</span>
          <span className="ldp-breadcrumb__current">{detail.title}</span>
        </div>
      </div>

      <div className="ldp-inner">

        {/* ── Left column: gallery ── */}
        <div className="ldp-gallery">
          <div className="ldp-gallery__main">
            {detail.imageUrls.length > 0 ? (
              <img src={detail.imageUrls[activeImg]} alt={detail.title} />
            ) : (
              <div className="ldp-gallery__empty">
                <span>📷</span>
                <p>Görsel eklenmemiş</p>
              </div>
            )}
          </div>
          {detail.imageUrls.length > 1 && (
            <div className="ldp-gallery__thumbs">
              {detail.imageUrls.map((u, i) => (
                <button
                  key={u} type="button"
                  className={`ldp-gallery__thumb ${i === activeImg ? 'ldp-gallery__thumb--active' : ''}`}
                  onClick={() => setActiveImg(i)}
                >
                  <img src={u} alt="" />
                </button>
              ))}
            </div>
          )}
        </div>

        {/* ── Right column: info ── */}
        <div className="ldp-info">

          {/* Header */}
          <div className="ldp-info__header">
            <div className="ldp-info__tags">
              <span className="ldp-tag ldp-tag--cat">{detail.categoryName}</span>
              <span className="ldp-tag ldp-tag--type">{{
                  Satilik: 'Satılık', Kiralik: 'Kiralık',
                  DevrenSatilik: 'Devren Satılık', DevrenKiralik: 'Devren Kiralık',
                  HizmetVeriyor: 'Hizmet Veriyor', HizmetAriyor: 'Hizmet Arıyor',
                }[detail.listingType] ?? detail.listingType}</span>
            </div>
            <h1 className="ldp-info__title">{detail.title}</h1>
            <div className="ldp-info__meta">
              {detail.cityName && (
                <span className="ldp-meta-item">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z"/><circle cx="12" cy="10" r="3"/>
                  </svg>
                  {detail.cityName}{detail.districtName ? ` / ${detail.districtName}` : ''}
                </span>
              )}
              <span className="ldp-meta-item">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M1 12s4-8 11-8 11 8 11 8-4 8-11 8-11-8-11-8z"/><circle cx="12" cy="12" r="3"/>
                </svg>
                {detail.viewCount} görüntüleme
              </span>
            </div>
          </div>

          {/* Price */}
          <div className="ldp-price-box">
            <span className="ldp-price__amount">
              {detail.price != null
                ? detail.price.toLocaleString('tr-TR')
                : 'Fiyat sorunuz'}
            </span>
            {detail.price != null && <span className="ldp-price__currency">{detail.currency}</span>}
          </div>

          {/* Attributes */}
          {filledAttrs.length > 0 && (
            <div className="ldp-attrs">
              <h3 className="ldp-attrs__title">Özellikler</h3>
              <div className="ldp-attrs__grid">
                {filledAttrs.map(([k, v]) => (
                  <div key={k} className="ldp-attr">
                    <span className="ldp-attr__key">{labelFor(k)}</span>
                    <span className="ldp-attr__val">{v}</span>
                  </div>
                ))}
              </div>
            </div>
          )}

          {/* Description */}
          {detail.description && (
            <div className="ldp-desc">
              <h3 className="ldp-desc__title">Açıklama</h3>
              <p className="ldp-desc__body">{detail.description}</p>
            </div>
          )}

          {/* Contact */}
          <div className="ldp-contact">
            <h3 className="ldp-contact__title">Satıcıya Ulaş</h3>
            {user ? (
              <form onSubmit={sendMessage} className="ldp-contact__form">
                <textarea
                  className="ldp-contact__textarea"
                  value={msg}
                  onChange={(e) => setMsg(e.target.value)}
                  rows={3}
                  placeholder="Merhaba, ilanınız hâlâ güncel mi?"
                />
                {sent ? (
                  <div className="ldp-contact__sent">✓ Mesaj gönderildi!</div>
                ) : (
                  <button className="ldp-contact__btn" type="submit" disabled={!msg.trim()}>
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
                      <path d="M22 2L11 13"/><path d="M22 2L15 22l-4-9-9-4 20-7z"/>
                    </svg>
                    Mesaj Gönder
                  </button>
                )}
              </form>
            ) : (
              <div className="ldp-contact__login">
                <p>Mesaj göndermek için giriş yapmalısınız.</p>
                <Link to="/login" className="ldp-contact__login-btn">Giriş Yap</Link>
              </div>
            )}
          </div>

        </div>
      </div>

      {/* Similar listings */}
      {similar.length > 0 && (
        <div className="ldp-similar">
          <div className="ldp-similar__inner">
            <h2 className="ldp-similar__title">Benzer İlanlar</h2>
            <div className="ldp-similar__grid">
              {similar.map((x) => (
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
                      <span className="lp-card__price">
                        {x.price != null ? `${x.price.toLocaleString('tr-TR')} ${x.currency}` : 'Fiyat sorunuz'}
                      </span>
                    </div>
                  </div>
                </Link>
              ))}
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
