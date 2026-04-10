import axios from 'axios';
import { useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { Link, useParams } from 'react-router-dom';
import { api, resolveImageUrl } from '../api/client';
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

type SalePrediction = {
  score: number; scoreLabel: string;
  estimatedDays: number; estimatedViews7d: number; estimatedMessages7d: number;
  priceTip: string | null; priceDelta: number | null; speedFactor: number | null;
  reasoning: string;
};

type PhotoAnalysis = {
  overallConditionScore: number;
  conditionLabel: string;
  detectedBrand: string | null;
  detectedModel: string | null;
  detectedYear: string | null;
  detectedColor: string | null;
  detectedBodyType: string | null;
  detectedFuelType: string | null;
  detectedTransmission: string | null;
  detectedEngineSize: string | null;
  detectedKmApprox: string | null;
  hasScratchOrDent: boolean;
  hasPaintDifference: boolean;
  hasGlassDamage: boolean;
  hasWheelOrTireDamage: boolean;
  hasRustOrCorrosion: boolean;
  hasBodyDeformation: boolean;
  interiorDamage: boolean;
  hasSeatWear: boolean;
  hasDashboardDamage: boolean;
  hasCeilingStain: boolean;
  suspectedTaxiOrRental: boolean;
  suspectedAccidentHistory: boolean;
  suspectedKmTampering: boolean;
  hasHiddenAreas: boolean;
  photoQualityScore: number;
  isProfessionalPhoto: boolean;
  brandMismatch: boolean;
  brandMismatchDetail: string | null;
  findings: string[];
  warnings: string[];
  summary: string;
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
  const [prediction, setPrediction] = useState<SalePrediction | null>(null);
  const [predLoading, setPredLoading] = useState(false);
  const scoreBarRef = useRef<HTMLDivElement>(null);
  const [photoAnalysis, setPhotoAnalysis] = useState<PhotoAnalysis | null>(null);
  const [photoLoading, setPhotoLoading] = useState(false);
  const [photoError, setPhotoError] = useState<string | null>(null);

  useEffect(() => {
    if (!id) return;
    setLoading(true);
    setPrediction(null);
    api.get<Detail>(`/api/listings/${id}`)
      .then((r) => { setDetail(r.data); setActiveImg(0); })
      .finally(() => setLoading(false));
    api.get<SimilarListing[]>(`/api/listings/${id}/similar?count=6`)
      .then((r) => setSimilar(r.data)).catch(() => {});
  }, [id]);

  // Tahmin sadece ilan sahibine yüklenir — detail yüklendikten sonra
  useEffect(() => {
    if (!id || !detail || !user) return;
    setPredLoading(true);
    api.get<SalePrediction>(`/api/listings/${id}/sale-prediction`)
      .then((r) => setPrediction(r.data))
      .catch((err) => { if (!axios.isAxiosError(err) || err.response?.status !== 403) console.warn(err); })
      .finally(() => setPredLoading(false));
  }, [id, detail?.id, user?.userId]);

  async function runPhotoAnalysis() {
    if (!id || photoLoading) return;
    setPhotoLoading(true);
    setPhotoError(null);
    setPhotoAnalysis(null);
    try {
      const r = await api.get<PhotoAnalysis>(`/api/listings/${id}/photo-analysis`);
      setPhotoAnalysis(r.data);
    } catch {
      setPhotoError('Analiz yapılamadı. Lütfen tekrar deneyin.');
    } finally {
      setPhotoLoading(false);
    }
  }

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
              <img src={resolveImageUrl(detail.imageUrls[activeImg])} alt={detail.title} />
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
                  <img src={resolveImageUrl(u)} alt="" />
                </button>
              ))}
            </div>
          )}

          {/* Fotoğraf Analizi */}
          {user && detail.imageUrls.length > 0 && (
            <div className="paa-wrap">
              {!photoAnalysis && !photoLoading && (
                <button className="paa-btn" type="button" onClick={runPhotoAnalysis}>
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <circle cx="11" cy="11" r="8"/><path d="m21 21-4.35-4.35"/>
                    <path d="M11 8v6M8 11h6"/>
                  </svg>
                  Fotoğrafları AI ile Analiz Et
                </button>
              )}
              {photoLoading && (
                <div className="paa-loading">
                  <span className="spc-spinner" />
                  <span>Fotoğraflar analiz ediliyor…</span>
                </div>
              )}
              {photoError && <p className="paa-error">{photoError}</p>}
              {photoAnalysis && (() => {
                const sc = photoAnalysis.overallConditionScore;
                const tier = sc >= 75 ? 'good' : sc >= 55 ? 'mid' : 'bad';
                const flag = (val: boolean, label: string, alert = false) => (
                  <span className={`paa-flag ${val ? (alert ? 'paa-flag--alert' : 'paa-flag--warn') : 'paa-flag--ok'}`}>
                    {val ? (alert ? '🚨' : '⚠️') : '✓'} {label}
                  </span>
                );
                const detected = [
                  photoAnalysis.detectedBrand && `${photoAnalysis.detectedBrand}`,
                  photoAnalysis.detectedModel,
                  photoAnalysis.detectedYear,
                  photoAnalysis.detectedColor,
                  photoAnalysis.detectedBodyType,
                  photoAnalysis.detectedFuelType,
                  photoAnalysis.detectedTransmission,
                  photoAnalysis.detectedEngineSize,
                  photoAnalysis.detectedKmApprox,
                ].filter(Boolean);
                return (
                  <div className={`paa-card paa-card--${tier}`}>
                    {/* MARKA UYUMSUZLUĞU — en üstte, kırmızı */}
                    {photoAnalysis.brandMismatch && (
                      <div className="paa-mismatch-alert">
                        <span className="paa-mismatch-icon">🚨</span>
                        <div>
                          <strong>FOTOĞRAF İLANLA UYUŞMUYOR!</strong>
                          <p>{photoAnalysis.brandMismatchDetail ?? 'Fotoğraftaki araç beyan edilen marka/modelden farklı.'}</p>
                        </div>
                      </div>
                    )}
                    {/* Header */}
                    <div className="paa-header">
                      <span className="paa-icon">🔍</span>
                      <div>
                        <span className="paa-title">AI Ekspertiz Analizi</span>
                        <span className={`paa-badge paa-badge--${tier}`}>{photoAnalysis.conditionLabel}</span>
                      </div>
                      <div className="paa-score">{sc}<span>/100</span></div>
                    </div>
                    <div className="paa-bar-wrap">
                      <div className="paa-bar" style={{ '--paa-pct': `${sc}%` } as React.CSSProperties} />
                    </div>

                    {/* Fotoğraf kalitesi */}
                    <div className="paa-photo-q">
                      <span>📸 Fotoğraf kalitesi: <b>{photoAnalysis.photoQualityScore}/100</b></span>
                      {photoAnalysis.isProfessionalPhoto && <span className="paa-pro-badge">Profesyonel</span>}
                    </div>

                    {/* Tespit edilen araç bilgileri */}
                    {detected.length > 0 && (
                      <div className="paa-detected">
                        <div className="paa-section-title">🚗 Fotoğraftan Tespit</div>
                        <div className="paa-detected-grid">
                          {photoAnalysis.detectedBrand && <div className="paa-det-item"><span>Marka</span><b>{photoAnalysis.detectedBrand}</b></div>}
                          {photoAnalysis.detectedModel && <div className="paa-det-item"><span>Model</span><b>{photoAnalysis.detectedModel}</b></div>}
                          {photoAnalysis.detectedYear && <div className="paa-det-item"><span>Yıl</span><b>{photoAnalysis.detectedYear}</b></div>}
                          {photoAnalysis.detectedColor && <div className="paa-det-item"><span>Renk</span><b>{photoAnalysis.detectedColor}</b></div>}
                          {photoAnalysis.detectedBodyType && <div className="paa-det-item"><span>Kasa</span><b>{photoAnalysis.detectedBodyType}</b></div>}
                          {photoAnalysis.detectedFuelType && <div className="paa-det-item"><span>Yakıt</span><b>{photoAnalysis.detectedFuelType}</b></div>}
                          {photoAnalysis.detectedTransmission && <div className="paa-det-item"><span>Vites</span><b>{photoAnalysis.detectedTransmission}</b></div>}
                          {photoAnalysis.detectedEngineSize && <div className="paa-det-item"><span>Motor</span><b>{photoAnalysis.detectedEngineSize}</b></div>}
                          {photoAnalysis.detectedKmApprox && <div className="paa-det-item"><span>KM</span><b>{photoAnalysis.detectedKmApprox}</b></div>}
                        </div>
                      </div>
                    )}

                    {/* Dış durum */}
                    <div className="paa-section-title">🔧 Dış Durum</div>
                    <div className="paa-flags">
                      {flag(photoAnalysis.hasScratchOrDent, 'Çizik/Göçük')}
                      {flag(photoAnalysis.hasPaintDifference, 'Boya Farkı')}
                      {flag(photoAnalysis.hasGlassDamage, 'Cam Hasarı')}
                      {flag(photoAnalysis.hasWheelOrTireDamage, 'Jant/Lastik')}
                      {flag(photoAnalysis.hasRustOrCorrosion, 'Pas/Korozyon')}
                      {flag(photoAnalysis.hasBodyDeformation, 'Kaporta Ezik')}
                    </div>

                    {/* İç mekan */}
                    <div className="paa-section-title">🪑 İç Mekan</div>
                    <div className="paa-flags">
                      {flag(photoAnalysis.interiorDamage, 'İç Hasar')}
                      {flag(photoAnalysis.hasSeatWear, 'Koltuk Yıpranma')}
                      {flag(photoAnalysis.hasDashboardDamage, 'Gösterge Hasarı')}
                      {flag(photoAnalysis.hasCeilingStain, 'Tavan Lekesi')}
                    </div>

                    {/* Şüpheli durumlar */}
                    <div className="paa-section-title">🚨 Şüpheli Durumlar</div>
                    <div className="paa-flags">
                      {flag(photoAnalysis.suspectedTaxiOrRental, 'Taksi/Kiralık', true)}
                      {flag(photoAnalysis.suspectedAccidentHistory, 'Kaza Geçmişi', true)}
                      {flag(photoAnalysis.suspectedKmTampering, 'KM Müdahalesi', true)}
                      {flag(photoAnalysis.hasHiddenAreas, 'Gizlenen Alan', true)}
                    </div>

                    {/* Tespitler */}
                    {photoAnalysis.findings.length > 0 && (
                      <>
                        <div className="paa-section-title">📋 Tespitler</div>
                        <ul className="paa-list paa-list--findings">
                          {photoAnalysis.findings.map((f, i) => <li key={i}>{f}</li>)}
                        </ul>
                      </>
                    )}
                    {photoAnalysis.warnings.length > 0 && (
                      <ul className="paa-list paa-list--warnings">
                        {photoAnalysis.warnings.map((w, i) => <li key={i}>⚠️ {w}</li>)}
                      </ul>
                    )}

                    <p className="paa-summary">{photoAnalysis.summary}</p>
                    <button className="paa-retry" type="button" onClick={runPhotoAnalysis}>Yeniden Analiz Et</button>
                  </div>
                );
              })()}
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

          {/* ── Sale Prediction Card (only owner) ── */}
          {(predLoading || prediction) && (
            <div className={`spc-card ${prediction ? `spc-card--${prediction.scoreLabel === 'Yüksek' ? 'high' : prediction.scoreLabel === 'Orta' ? 'mid' : 'low'}` : 'spc-card--loading'}`}>
              {predLoading && !prediction ? (
                <div className="spc-loading">
                  <span className="spc-spinner" />
                  <span>Satılma ihtimali hesaplanıyor…</span>
                </div>
              ) : prediction ? (
                <>
                  <div className="spc-header">
                    <div className="spc-title-row">
                      <span className="spc-icon">🔥</span>
                      <span className="spc-title">Satılma İhtimali Skoru</span>
                      <span className={`spc-label spc-label--${prediction.scoreLabel === 'Yüksek' ? 'high' : prediction.scoreLabel === 'Orta' ? 'mid' : 'low'}`}>
                        {prediction.scoreLabel}
                      </span>
                    </div>
                    <div className="spc-score-row">
                      <span className="spc-score-num">{prediction.score}</span>
                      <span className="spc-score-max">/100</span>
                    </div>
                    <div className="spc-bar-wrap">
                      <div
                        ref={scoreBarRef}
                        className="spc-bar"
                        style={{ '--spc-pct': `${prediction.score}%` } as React.CSSProperties}
                      />
                    </div>
                  </div>

                  <div className="spc-stats">
                    <div className="spc-stat">
                      <span className="spc-stat__icon">📅</span>
                      <div>
                        <span className="spc-stat__val">{prediction.estimatedDays} gün</span>
                        <span className="spc-stat__lbl">tahmini satış süresi</span>
                      </div>
                    </div>
                    <div className="spc-stat">
                      <span className="spc-stat__icon">👁</span>
                      <div>
                        <span className="spc-stat__val">{prediction.estimatedViews7d}</span>
                        <span className="spc-stat__lbl">7 günde görüntüleme</span>
                      </div>
                    </div>
                    <div className="spc-stat">
                      <span className="spc-stat__icon">💬</span>
                      <div>
                        <span className="spc-stat__val">{prediction.estimatedMessages7d}</span>
                        <span className="spc-stat__lbl">7 günde mesaj</span>
                      </div>
                    </div>
                  </div>

                  {prediction.priceTip && (
                    <div className="spc-tip">
                      <span className="spc-tip__icon">💡</span>
                      <span className="spc-tip__text">{prediction.priceTip}</span>
                    </div>
                  )}

                  <p className="spc-reasoning">{prediction.reasoning}</p>
                </>
              ) : null}
            </div>
          )}

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
                      ? <img src={resolveImageUrl(x.primaryImageUrl)} alt={x.title} loading="lazy" />
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
