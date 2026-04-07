import axios from 'axios';
import { useEffect, useRef, useState } from 'react';
import type { FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { useAuth } from '../context/AuthContext';

type Cat = { id: string; name: string; slug: string; children: Cat[] };
type AttrOption = { id: string; valueKey: string; label: string; parentOptionId: string | null };
type Attr = {
  id: string; attributeKey: string; displayName: string; dataType: string;
  isRequired: boolean; parentAttributeId: string | null; options: AttrOption[];
};
type DetectResult = {
  rootCategoryId: string; rootName: string;
  subCategories: { id: string; name: string; slug: string }[];
  suggestedLeafCategoryId: string | null; confidence: number;
  usedMockProvider: boolean; suggestedTitle: string | null;
  suggestedDescription: string | null; suggestedPrice: number | null;
  suggestedAttributeValues: Record<string, string> | null;
};

export function CreateListingPage() {
  const { user } = useAuth();
  const nav = useNavigate();
  const [tree, setTree] = useState<Cat[]>([]);
  const [categoryId, setCategoryId] = useState<string | null>(null);
  const [attrs, setAttrs] = useState<Attr[]>([]);
  const [prompt, setPrompt] = useState('');
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [price, setPrice] = useState<string>('');
  const [listingType] = useState('Satilik');
  const [attrValues, setAttrValues] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(false);
  const [detect, setDetect] = useState<DetectResult | null>(null);
  const [detectError, setDetectError] = useState<string | null>(null);
  const [attrRefreshKey, setAttrRefreshKey] = useState(0);
  const [enriching, setEnriching] = useState(false);
  const pollTimers = useRef<number[]>([]);
  const enrichPollCount = useRef(0);
  const detailRef = useRef<HTMLDivElement>(null);

  function refreshCategories() {
    api.get<Cat[]>('/api/categories').then((r) => setTree(r.data));
  }

  useEffect(() => { refreshCategories(); }, []);

  useEffect(() => {
    if (!categoryId) { setAttrs([]); setAttrValues({}); return; }
    api.get<{ attributes: Attr[] }>(`/api/categories/${categoryId}/attributes`).then((r) => {
      setAttrs(r.data.attributes);
      setAttrValues((prev) => {
        const next: Record<string, string> = {};
        for (const a of r.data.attributes) {
          const prevVal = prev[a.attributeKey];
          if (!prevVal) continue;
          const matchedOpt = a.options.find(
            (o) => o.valueKey.toLowerCase() === prevVal.toLowerCase() ||
                   o.label.toLowerCase() === prevVal.toLowerCase(),
          );
          next[a.attributeKey] = matchedOpt ? matchedOpt.valueKey : prevVal;
        }
        return next;
      });
      const hasEmptyEnum = r.data.attributes.some((a) => a.dataType === 'Enum' && a.options.length === 0);
      if (!hasEmptyEnum) setEnriching(false);
    });
  }, [categoryId, attrRefreshKey]);

  useEffect(() => () => { pollTimers.current.forEach(clearTimeout); }, []);

  useEffect(() => {
    if (!enriching) { enrichPollCount.current = 0; return; }
    if (enrichPollCount.current >= 15) { setEnriching(false); return; }
    const t = window.setTimeout(() => {
      enrichPollCount.current += 1;
      setAttrRefreshKey((k) => k + 1);
    }, 4000);
    return () => clearTimeout(t);
  }, [enriching, attrRefreshKey]);

  async function runDetect(e: FormEvent) {
    e.preventDefault();
    setLoading(true);
    setDetectError(null);
    try {
      const { data } = await api.post<DetectResult>('/api/ai/detect-listing-category', { prompt });
      setDetect(data);
      refreshCategories();
      if (data.suggestedLeafCategoryId) setCategoryId(data.suggestedLeafCategoryId);
      else setCategoryId(null);
      if (data.suggestedTitle) setTitle(data.suggestedTitle);
      if (data.suggestedDescription) setDescription(data.suggestedDescription);
      if (data.suggestedPrice) setPrice(String(data.suggestedPrice));
      if (data.suggestedAttributeValues) setAttrValues(data.suggestedAttributeValues);
      pollTimers.current.forEach(clearTimeout);
      pollTimers.current = [];
      enrichPollCount.current = 0;
      setEnriching(true);
      setAttrRefreshKey((k) => k + 1);
      setTimeout(() => detailRef.current?.scrollIntoView({ behavior: 'smooth', block: 'start' }), 100);
    } catch (err: unknown) {
      if (axios.isAxiosError(err) && err.response?.status === 422) {
        const msg = (err.response.data as { error?: string })?.error;
        setDetectError(msg ?? 'Girilen metin bir ilan tanımlamıyor. Lütfen daha açık yazın.');
      } else {
        setDetectError('Bir hata oluştu, lütfen tekrar deneyin.');
      }
      setDetect(null);
    } finally {
      setLoading(false);
    }
  }

  async function publish(e: FormEvent) {
    e.preventDefault();
    if (!user || !categoryId) return;
    setLoading(true);
    try {
      const body = {
        categoryId, title, description,
        price: price ? Number(price) : null,
        currency: 'TRY', listingType,
        cityId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaa0001',
        districtId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaa0002',
        publish: true,
        attributes: attrs.map((a) => {
          const raw = attrValues[a.attributeKey] ?? '';
          let valueText: string | undefined;
          let valueInt: number | undefined;
          let valueDecimal: number | undefined;
          let valueBool: boolean | undefined;
          if (a.dataType === 'Bool') valueBool = raw === 'true' ? true : raw === 'false' ? false : undefined;
          else if (a.dataType === 'Int') valueInt = raw ? parseInt(raw, 10) : undefined;
          else if (a.dataType === 'Decimal' || a.dataType === 'Money') valueDecimal = raw ? parseFloat(raw.replace(',', '.')) : undefined;
          else valueText = raw || undefined;
          return { categoryAttributeId: a.id, valueText, valueInt, valueDecimal, valueBool };
        }),
      };
      const { data } = await api.post<{ id: string }>('/api/listings', body);
      nav(`/listings/${data.id}`);
    } finally {
      setLoading(false);
    }
  }

  /* ── attr helpers ── */
  function buildControl(a: Attr): React.ReactNode {
    const val = attrValues[a.attributeKey] ?? '';
    const setVal = (v: string) => {
      const next = { ...attrValues, [a.attributeKey]: v };
      attrs.filter((c) => c.parentAttributeId === a.id).forEach((c) => { next[c.attributeKey] = ''; });
      setAttrValues(next);
    };

    if (a.dataType === 'Bool') {
      return (
        <select value={val} onChange={(e) => setVal(e.target.value)}>
          <option value="">Seçiniz</option>
          <option value="true">Evet</option>
          <option value="false">Hayır</option>
        </select>
      );
    }

    let filteredOptions = a.options;
    if (a.parentAttributeId) {
      const parentAttr = attrs.find((p) => p.id === a.parentAttributeId);
      if (parentAttr) {
        const parentVal = attrValues[parentAttr.attributeKey] ?? '';
        const hasParentLinks = a.options.some((o) => o.parentOptionId);
        if (parentVal && hasParentLinks) {
          const parentOpt = parentAttr.options.find((o) => o.valueKey === parentVal || o.label === parentVal);
          if (parentOpt) {
            filteredOptions = a.options.filter((o) => o.parentOptionId === parentOpt.id || !o.parentOptionId);
          }
        }
      }
    }

    if (a.dataType === 'Enum' && filteredOptions.length === 0 && enriching) {
      return <select disabled><option>Yükleniyor…</option></select>;
    }

    if (a.dataType === 'Int' || a.dataType === 'Decimal' || a.dataType === 'Money') {
      return (
        <input
          type="text" inputMode="decimal" value={val}
          onChange={(e) => setVal(e.target.value)}
          placeholder={a.dataType === 'Money' ? 'TRY' : ''}
        />
      );
    }

    const optionKeys = new Set(filteredOptions.map((o) => o.valueKey));
    const showExtraVal = val && !optionKeys.has(val);
    return (
      <select value={val} onChange={(e) => setVal(e.target.value)}>
        <option value="">Seçiniz</option>
        {showExtraVal && <option value={val}>{val}</option>}
        {filteredOptions.map((o) => (
          <option key={o.valueKey} value={o.valueKey}>{o.label}</option>
        ))}
      </select>
    );
  }

  const selectedCatName = (() => {
    for (const root of tree) {
      for (const c of root.children ?? []) {
        if (c.id === categoryId) return `${root.name} › ${c.name}`;
      }
    }
    return null;
  })();

  return (
    <div className="cl-page">

      {/* ── Step 1: AI Prompt ── */}
      <section className="cl-hero">
        <div className="cl-hero__inner">
          <div className="cl-steps">
            <div className={`cl-step ${!detect ? 'cl-step--active' : 'cl-step--done'}`}>
              <span className="cl-step__dot">{detect ? '✓' : '1'}</span>
              <span className="cl-step__label">İlanı Tanımla</span>
            </div>
            <div className="cl-step__line" />
            <div className={`cl-step ${detect && !title ? 'cl-step--active' : detect && title ? 'cl-step--done' : ''}`}>
              <span className="cl-step__dot">2</span>
              <span className="cl-step__label">Detayları Düzenle</span>
            </div>
            <div className="cl-step__line" />
            <div className="cl-step">
              <span className="cl-step__dot">3</span>
              <span className="cl-step__label">Yayına Al</span>
            </div>
          </div>

          <h1 className="cl-hero__title">Yeni İlan Ver</h1>
          <p className="cl-hero__sub">Ne satmak veya kiralamak istediğinizi yazın — yapay zeka gerisini halleder.</p>

          <form className="cl-prompt-form" onSubmit={runDetect}>
            <div className="cl-prompt-wrap">
              <div className="cl-prompt-icon" aria-hidden="true">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round">
                  <path d="M12 2a10 10 0 1 0 10 10A10 10 0 0 0 12 2z" opacity=".3"/>
                  <path d="M12 8v4l3 3"/>
                  <circle cx="12" cy="12" r="10"/>
                  <path d="M9.09 9a3 3 0 0 1 5.83 1c0 2-3 3-3 3"/>
                  <line x1="12" y1="17" x2="12.01" y2="17"/>
                </svg>
              </div>
              <textarea
                className="cl-prompt-input"
                value={prompt}
                onChange={(e) => setPrompt(e.target.value)}
                rows={3}
                placeholder="Örn: 2023 BMW 320i M Sport, 86 bin km, otomatik, beyaz, hasar yok…"
              />
            </div>

            {detectError && (
              <div className="cl-error">
                <span>⚠</span> {detectError}
              </div>
            )}

            <button className="cl-ai-btn" type="submit" disabled={loading || !prompt.trim()}>
              {loading ? (
                <>
                  <span className="cl-spinner" /> Analiz ediliyor…
                </>
              ) : (
                <>
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round">
                    <polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"/>
                  </svg>
                  AI ile Analiz Et
                </>
              )}
            </button>
          </form>
        </div>
      </section>

      {/* ── Step 2 & 3: Details ── */}
      {detect && (
        <div className="cl-detail" ref={detailRef}>
          <div className="cl-detail__inner">

            {/* Left column */}
            <div className="cl-col cl-col--left">

              {/* Category card */}
              <div className="cl-card">
                <div className="cl-card__head">
                  <span className="cl-card__icon">🏷</span>
                  <div>
                    <h3 className="cl-card__title">Kategori</h3>
                    <p className="cl-card__sub">AI önerdi, istediğinizi seçebilirsiniz</p>
                  </div>
                </div>
                {selectedCatName && (
                  <div className="cl-cat-badge">
                    <span>✓</span> {selectedCatName}
                  </div>
                )}
                <select
                  className="cl-select"
                  value={categoryId ?? ''}
                  onChange={(e) => setCategoryId(e.target.value || null)}
                >
                  <option value="">Kategori seçin</option>
                  {tree.map((root) => (
                    <optgroup key={root.id} label={root.name}>
                      {(root.children ?? []).map((c) => (
                        <option key={c.id} value={c.id}>{c.name}</option>
                      ))}
                    </optgroup>
                  ))}
                </select>
              </div>

              {/* Attributes card */}
              {attrs.length > 0 && (
                <div className="cl-card">
                  <div className="cl-card__head">
                    <span className="cl-card__icon">⚙️</span>
                    <div>
                      <h3 className="cl-card__title">Özellikler</h3>
                      <p className="cl-card__sub">
                        {enriching ? (
                          <span className="cl-enriching">
                            <span className="cl-spinner cl-spinner--sm" /> Seçenekler güncelleniyor…
                          </span>
                        ) : 'AI tarafından dolduruldu, düzenleyebilirsiniz'}
                      </p>
                    </div>
                  </div>
                  <div className="cl-attr-grid">
                    {attrs.map((a) => (
                      <label key={a.id} className="cl-attr-item">
                        <span className="cl-attr-label">
                          {a.displayName}
                          {a.isRequired && <span className="cl-required">*</span>}
                        </span>
                        {buildControl(a)}
                      </label>
                    ))}
                  </div>
                </div>
              )}
            </div>

            {/* Right column */}
            <div className="cl-col cl-col--right">
              <div className="cl-card cl-card--sticky">
                <div className="cl-card__head">
                  <span className="cl-card__icon">📝</span>
                  <div>
                    <h3 className="cl-card__title">İlan Detayları</h3>
                    <p className="cl-card__sub">AI ön doldurdu, kontrol edip düzeltin</p>
                  </div>
                </div>

                <form className="cl-form" onSubmit={publish}>
                  <div className="cl-field">
                    <label className="cl-field__label" htmlFor="cl-title">Başlık</label>
                    <input
                      id="cl-title"
                      className="cl-input"
                      value={title}
                      onChange={(e) => setTitle(e.target.value)}
                      required maxLength={200}
                      placeholder="İlan başlığı"
                    />
                  </div>

                  <div className="cl-field">
                    <label className="cl-field__label" htmlFor="cl-desc">Açıklama</label>
                    <textarea
                      id="cl-desc"
                      className="cl-input cl-input--textarea"
                      value={description}
                      onChange={(e) => setDescription(e.target.value)}
                      rows={5}
                      placeholder="İlanınızı detaylı açıklayın…"
                    />
                  </div>

                  <div className="cl-field">
                    <label className="cl-field__label" htmlFor="cl-price">
                      Fiyat
                      <span className="cl-field__currency">TRY</span>
                    </label>
                    <input
                      id="cl-price"
                      className="cl-input"
                      value={price}
                      onChange={(e) => setPrice(e.target.value)}
                      inputMode="decimal"
                      placeholder="0,00"
                    />
                  </div>

                  <button
                    className="cl-publish-btn"
                    type="submit"
                    disabled={loading || !categoryId || !title.trim()}
                  >
                    {loading ? (
                      <><span className="cl-spinner" /> Yayına alınıyor…</>
                    ) : (
                      <>
                        <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.2" strokeLinecap="round" strokeLinejoin="round">
                          <path d="M22 2L11 13"/><path d="M22 2L15 22l-4-9-9-4 20-7z"/>
                        </svg>
                        Yayına Gönder
                      </>
                    )}
                  </button>

                  {!categoryId && (
                    <p className="cl-hint">Yayına göndermek için kategori seçin.</p>
                  )}
                </form>
              </div>
            </div>

          </div>
        </div>
      )}
    </div>
  );
}
