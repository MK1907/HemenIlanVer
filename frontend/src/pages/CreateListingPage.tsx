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
};

type PartialSuggestResponse = { traceId: string; suggestions: string[] };

export function CreateListingPage() {
  const { user } = useAuth();
  const nav = useNavigate();
  const [tree, setTree] = useState<Cat[]>([]);
  const [categoryId, setCategoryId] = useState<string | null>(null);
  const [attrs, setAttrs] = useState<Attr[]>([]);
  const [prompt, setPrompt] = useState(
    'VIP Özel Ders Öğretmeninden LGS-YKS Türkçe & Edebiyat Özel Ders'
  );
  const [title, setTitle] = useState('');
  const [description, setDescription] = useState('');
  const [price, setPrice] = useState<string>('');
  const [listingType] = useState('Satilik');
  const [attrValues, setAttrValues] = useState<Record<string, string>>({});
  const [loading, setLoading] = useState(false);
  const [detect, setDetect] = useState<DetectResult | null>(null);
  const [partialSuggestions, setPartialSuggestions] = useState<string[]>([]);
  const [partialSuggestLoading, setPartialSuggestLoading] = useState(false);

  function refreshCategories() {
    api.get<Cat[]>('/api/categories').then((r) => setTree(r.data));
  }

  useEffect(() => {
    refreshCategories();
  }, []);

  useEffect(() => {
    if (!categoryId) {
      setAttrs([]);
      return;
    }
    api.get<{ attributes: Attr[] }>(`/api/categories/${categoryId}/attributes`).then((r) => setAttrs(r.data.attributes));
  }, [categoryId]);

  useEffect(() => {
    const trimmed = prompt.trim();
    if (trimmed.length < 2) {
      setPartialSuggestions([]);
      setPartialSuggestLoading(false);
      return;
    }

    const ac = new AbortController();
    const timer = window.setTimeout(() => {
      setPartialSuggestLoading(true);
      api
        .post<PartialSuggestResponse>('/api/ai/suggest-partial-listing', { partialText: trimmed }, { signal: ac.signal })
        .then((res) => {
          setPartialSuggestions(res.data.suggestions ?? []);
        })
        .catch((err: unknown) => {
          if (axios.isAxiosError(err) && err.code === 'ERR_CANCELED') return;
          setPartialSuggestions([]);
        })
        .finally(() => {
          if (!ac.signal.aborted) setPartialSuggestLoading(false);
        });
    }, 450);

    return () => {
      clearTimeout(timer);
      ac.abort();
      setPartialSuggestLoading(false);
    };
  }, [prompt]);

  async function runDetect(e: FormEvent) {
    e.preventDefault();
    setLoading(true);
    try {
      const { data } = await api.post<DetectResult>('/api/ai/detect-listing-category', { prompt });
      setDetect(data);
      refreshCategories();
      if (data.suggestedLeafCategoryId) setCategoryId(data.suggestedLeafCategoryId);
      else setCategoryId(null);
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
          let valueText: string | undefined = raw;
          let valueInt: number | undefined;
          let valueDecimal: number | undefined;
          if (a.dataType === 'Int') {
            valueInt = raw ? parseInt(raw, 10) : undefined;
            valueText = undefined;
          } else if (a.dataType === 'Decimal' || a.dataType === 'Money') {
            valueDecimal = raw ? parseFloat(raw.replace(',', '.')) : undefined;
            valueText = undefined;
          }
          return {
            categoryAttributeId: a.id,
            valueText,
            valueInt,
            valueDecimal,
            valueBool: undefined as boolean | undefined,
          };
        }),
      };
      const { data } = await api.post<{ id: string }>('/api/listings', body);
      nav(`/listings/${data.id}`);
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="create-flow">
      <div className="card">
        <h2>Hızlı ilan — AI kategori</h2>
        <p className="muted">
          Ne sattığınızı veya kiraladığınızı yazın. Yapay zeka uygun ana/alt kategoriyi ve gerekirse yeni filtre alanlarını
          veritabanına ekler; alt kategoriyi ve form değerlerini siz seçip doldurursunuz.
        </p>
        <form className="form" onSubmit={runDetect}>
          <label>
            Ne satıyor / kiralıyorsunuz? (doğal dil)
            <textarea value={prompt} onChange={(e) => setPrompt(e.target.value)} rows={6} />
          </label>
          {(partialSuggestLoading || partialSuggestions.length > 0) && (
            <div className="suggestion-chips" aria-live="polite">
              <p className="muted small suggestion-chips__title">
                {partialSuggestLoading ? 'Olası yönler düşünülüyor…' : 'Yazdıklarınıza göre olası yönler (tıklayınca metne eklenir):'}
              </p>
              {!partialSuggestLoading && partialSuggestions.length > 0 && (
                <div className="suggestion-chips__list">
                  {partialSuggestions.map((s, i) => (
                    <button
                      key={`${i}-${s.slice(0, 24)}`}
                      type="button"
                      className="chip"
                      onClick={() =>
                        setPrompt((prev) => {
                          const p = prev.trimEnd();
                          return p ? `${p} ${s}` : s;
                        })
                      }
                    >
                      {s}
                    </button>
                  ))}
                </div>
              )}
            </div>
          )}
          <button className="btn primary" type="submit" disabled={loading || !prompt.trim()}>
            {loading ? 'İşleniyor…' : 'Kategori ve filtreleri bul (AI)'}
          </button>
        </form>

        {detect && (
          <div className="muted" style={{ marginTop: '1rem' }}>
            <p>
              {detect.rootName} ana kategorisi · Güven: {(detect.confidence * 100).toFixed(0)}%
              {detect.usedMockProvider ? ' (mock)' : ''}
            </p>
            <p className="small">Alt kategoriler: {detect.subCategories.map((s) => s.name).join(', ') || '—'}</p>
          </div>
        )}

        <form className="form" style={{ marginTop: '1.25rem' }}>
          <label>
            Alt kategori (form alanları buna göre yüklenir)
            <select value={categoryId ?? ''} onChange={(e) => setCategoryId(e.target.value || null)}>
              <option value="">Seçin</option>
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
          </label>
          <p className="muted small">AI net alt kategori önerdiyse seçim otomatik gelir; değiştirmek serbest.</p>
        </form>
      </div>

      <div className="card">
        <h3>Form</h3>
        <p className="muted small">İlan tipi: {listingType}</p>
        <form className="form" onSubmit={publish}>
          <label>
            Başlık
            <input value={title} onChange={(e) => setTitle(e.target.value)} required maxLength={200} />
          </label>
          <label>
            Açıklama
            <textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={6} />
          </label>
          <label>
            Fiyat (TRY)
            <input value={price} onChange={(e) => setPrice(e.target.value)} inputMode="decimal" placeholder="950000" />
          </label>
          {attrs.map((a) => (
            <label key={a.id}>
              {a.displayName}
              {a.options.length > 0 ? (
                <select value={attrValues[a.attributeKey] ?? ''} onChange={(e) => setAttrValues({ ...attrValues, [a.attributeKey]: e.target.value })}>
                  <option value="">—</option>
                  {a.options.map((o) => (
                    <option key={o.valueKey} value={o.valueKey}>
                      {o.label}
                    </option>
                  ))}
                </select>
              ) : (
                <input
                  value={attrValues[a.attributeKey] ?? ''}
                  onChange={(e) => setAttrValues({ ...attrValues, [a.attributeKey]: e.target.value })}
                />
              )}
            </label>
          ))}
          <button className="btn primary" type="submit" disabled={loading || !categoryId}>
            Yayına gönder
          </button>
        </form>
        <p className="muted small">Şehir/ilçe MVP’de örnek İstanbul/Ataşehir sabit; prod’da seçim eklenir.</p>
      </div>
    </div>
  );
}
