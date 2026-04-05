import axios from 'axios';
import { useEffect, useState } from 'react';
import type { FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { useAuth } from '../context/AuthContext';

type Cat = { id: string; name: string; slug: string; children: Cat[] };

type Attr = {
  id: string;
  attributeKey: string;
  displayName: string;
  dataType: string;
  isRequired: boolean;
  options: { valueKey: string; label: string }[];
};

type DetectResult = {
  rootCategoryId: string;
  rootName: string;
  subCategories: { id: string; name: string; slug: string }[];
  suggestedLeafCategoryId: string | null;
  confidence: number;
  usedMockProvider: boolean;
  suggestedTitle: string | null;
  suggestedDescription: string | null;
  suggestedPrice: number | null;
  suggestedAttributeValues: Record<string, string> | null;
};

type PartialSuggestResponse = { traceId: string; suggestions: string[] };

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

  const [suggestions, setSuggestions] = useState<string[]>([]);
  const [suggestLoading, setSuggestLoading] = useState(false);

  function refreshCategories() {
    api.get<Cat[]>('/api/categories').then((r) => setTree(r.data));
  }

  useEffect(() => {
    refreshCategories();
  }, []);

  useEffect(() => {
    if (!categoryId) {
      setAttrs([]);
      setAttrValues({});
      return;
    }
    api
      .get<{ attributes: Attr[] }>(`/api/categories/${categoryId}/attributes`)
      .then((r) => {
        setAttrs(r.data.attributes);
        setAttrValues((prev) => {
          const next: Record<string, string> = {};
          for (const a of r.data.attributes) {
            if (prev[a.attributeKey]) next[a.attributeKey] = prev[a.attributeKey];
          }
          return next;
        });
      });
  }, [categoryId]);

  /* ── Yazarken AI önerileri (debounce 600ms) ── */
  useEffect(() => {
    const trimmed = prompt.trim();
    if (trimmed.length < 2) {
      setSuggestions([]);
      setSuggestLoading(false);
      return;
    }

    const ac = new AbortController();
    const timer = window.setTimeout(() => {
      setSuggestLoading(true);
      api
        .post<PartialSuggestResponse>(
          '/api/ai/suggest-partial-listing',
          { partialText: trimmed },
          { signal: ac.signal },
        )
        .then((res) => setSuggestions(res.data.suggestions ?? []))
        .catch((err: unknown) => {
          if (axios.isAxiosError(err) && err.code === 'ERR_CANCELED') return;
          setSuggestions([]);
        })
        .finally(() => {
          if (!ac.signal.aborted) setSuggestLoading(false);
        });
    }, 600);

    return () => {
      clearTimeout(timer);
      ac.abort();
      setSuggestLoading(false);
    };
  }, [prompt]);

  function pickSuggestion(text: string) {
    setPrompt(text);
    setSuggestions([]);
  }

  /* ── AI ile İşle → kategori + özellikler + otomatik doldurma ── */
  async function runDetect(e: FormEvent) {
    e.preventDefault();
    setLoading(true);
    setSuggestions([]);
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
        categoryId,
        title,
        description,
        price: price ? Number(price) : null,
        currency: 'TRY',
        listingType,
        cityId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaa0001',
        districtId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaa0002',
        publish: true,
        attributes: attrs.map((a) => {
          const raw = attrValues[a.attributeKey] ?? '';
          let valueText: string | undefined;
          let valueInt: number | undefined;
          let valueDecimal: number | undefined;
          let valueBool: boolean | undefined;

          if (a.dataType === 'Bool') {
            valueBool = raw === 'true' ? true : raw === 'false' ? false : undefined;
          } else if (a.dataType === 'Int') {
            valueInt = raw ? parseInt(raw, 10) : undefined;
          } else if (a.dataType === 'Decimal' || a.dataType === 'Money') {
            valueDecimal = raw ? parseFloat(raw.replace(',', '.')) : undefined;
          } else {
            valueText = raw || undefined;
          }

          return {
            categoryAttributeId: a.id,
            valueText,
            valueInt,
            valueDecimal,
            valueBool,
          };
        }),
      };
      const { data } = await api.post<{ id: string }>('/api/listings', body);
      nav(`/listings/${data.id}`);
    } finally {
      setLoading(false);
    }
  }

  const showSuggestions = suggestLoading || suggestions.length > 0;

  return (
    <div className="create-flow">
      {/* ─── Üst kart: yazarken öneriler + AI ile İşle ─── */}
      <div className="card">
        <h2>Hızlı İlan Oluştur</h2>
        <p className="muted">
          İlanınızı kısaca tanımlayın; yapay zeka size olasılıklar sunar. Birini seçip
          &laquo;AI ile İşle&raquo; dediğinizde kategori ve özellikler otomatik gelir.
        </p>

        <form className="form" onSubmit={runDetect}>
          <label>
            Ne satıyor / kiralıyorsunuz?
            <textarea
              value={prompt}
              onChange={(e) => setPrompt(e.target.value)}
              rows={3}
              placeholder="Örn: 2012 model Fiat Egea, toptan gıda ürünleri, kiralık 3+1 daire…"
            />
          </label>

          {showSuggestions && (
            <div className="suggestion-chips" aria-live="polite">
              {suggestLoading ? (
                <p className="muted small suggestion-chips__title">Olası ilanlar düşünülüyor…</p>
              ) : (
                <>
                  <p className="muted small suggestion-chips__title">Bunu mu demek istediniz?</p>
                  <div className="suggestion-chips__list">
                    {suggestions.map((s, i) => (
                      <button
                        key={`${i}-${s.slice(0, 24)}`}
                        type="button"
                        className="chip"
                        onClick={() => pickSuggestion(s)}
                      >
                        {s}
                      </button>
                    ))}
                  </div>
                </>
              )}
            </div>
          )}

          <button className="btn primary" type="submit" disabled={loading || !prompt.trim()}>
            {loading ? 'İşleniyor…' : 'AI ile İşle'}
          </button>
        </form>
      </div>

      {/* ─── Alt kart: kategori seçimi + özellikler + ilan formu ─── */}
      {detect && (
        <div className="card">
          {/* Kategori seçimi — kullanıcı istediğini seçebilir */}
          <div className="category-picker">
            <h3>Kategori</h3>
            <p className="muted small">AI önerdi, ama istediğiniz kategoriyi seçebilirsiniz.</p>
            <select
              className="category-select"
              value={categoryId ?? ''}
              onChange={(e) => setCategoryId(e.target.value || null)}
            >
              <option value="">Kategori seçin</option>
              {tree.map((root) => (
                <optgroup key={root.id} label={root.name}>
                  {(root.children ?? []).map((c) => (
                    <option key={c.id} value={c.id}>
                      {c.name}
                    </option>
                  ))}
                </optgroup>
              ))}
            </select>
          </div>

          {/* Özellikler — seçilen kategoriye göre dinamik */}
          {attrs.length > 0 && (
            <div className="filter-section">
              <h4>Özellikler</h4>
              <div className="filter-grid">
                {attrs.map((a) => {
                  const val = attrValues[a.attributeKey] ?? '';
                  const setVal = (v: string) =>
                    setAttrValues({ ...attrValues, [a.attributeKey]: v });

                  const isBool = a.dataType === 'Bool';
                  const isNumeric = a.dataType === 'Int' || a.dataType === 'Decimal' || a.dataType === 'Money';
                  const hasOptions = a.options.length > 0;

                  let control: React.ReactNode;

                  if (isBool) {
                    control = (
                      <select value={val} onChange={(e) => setVal(e.target.value)}>
                        <option value="">Seçiniz</option>
                        <option value="true">Evet</option>
                        <option value="false">Hayır</option>
                      </select>
                    );
                  } else if (hasOptions) {
                    control = (
                      <select value={val} onChange={(e) => setVal(e.target.value)}>
                        <option value="">Seçiniz</option>
                        {a.options.map((o) => (
                          <option key={o.valueKey} value={o.valueKey}>
                            {o.label}
                          </option>
                        ))}
                      </select>
                    );
                  } else if (isNumeric) {
                    control = (
                      <input
                        type="number"
                        value={val}
                        onChange={(e) => setVal(e.target.value)}
                        placeholder={a.displayName}
                        inputMode="decimal"
                      />
                    );
                  } else {
                    control = (
                      <input
                        value={val}
                        onChange={(e) => setVal(e.target.value)}
                        placeholder={a.displayName}
                      />
                    );
                  }

                  return (
                    <label key={a.id} className="filter-item">
                      <span className="filter-item__label">
                        {a.displayName}
                        {a.isRequired && <span className="required-star">*</span>}
                      </span>
                      {control}
                    </label>
                  );
                })}
              </div>
            </div>
          )}

          {/* İlan detayları */}
          <form className="form" onSubmit={publish} style={{ marginTop: '1rem' }}>
            <h4>İlan Detayları</h4>
            <label>
              Başlık
              <input value={title} onChange={(e) => setTitle(e.target.value)} required maxLength={200} />
            </label>
            <label>
              Açıklama
              <textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={4} />
            </label>
            <label>
              Fiyat (TRY)
              <input
                value={price}
                onChange={(e) => setPrice(e.target.value)}
                inputMode="decimal"
                placeholder="950000"
              />
            </label>
            <button className="btn primary" type="submit" disabled={loading || !categoryId}>
              Yayına Gönder
            </button>
          </form>
        </div>
      )}
    </div>
  );
}
