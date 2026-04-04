import { useState } from 'react';
import type { FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { api } from '../api/client';

type SearchExtract = {
  categoryId?: string | null;
  cityId?: string | null;
  cityName?: string | null;
  minPrice?: number | null;
  maxPrice?: number | null;
  sortPreference?: string | null;
  filters: Record<string, string | null | undefined>;
};

export function SearchPage() {
  const nav = useNavigate();
  const [prompt, setPrompt] = useState("İstanbul'da 1 milyon altı otomatik Egea arıyorum, kilometresi düşük olsun");
  const [loading, setLoading] = useState(false);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setLoading(true);
    try {
      const { data } = await api.post<SearchExtract>('/api/ai/search-extract', { prompt, categoryId: null });
      const qs = new URLSearchParams();
      if (data.categoryId) qs.set('categoryId', data.categoryId);
      if (data.cityId) qs.set('cityId', data.cityId);
      if (data.minPrice != null) qs.set('minPrice', String(data.minPrice));
      if (data.maxPrice != null) qs.set('maxPrice', String(data.maxPrice));
      if (data.sortPreference) qs.set('sort', data.sortPreference);
      const filters = data.filters ?? {};
      const filterModel = filters.model;
      const filterGear = filters.gear;
      if (filterModel) qs.set('filterModel', filterModel);
      if (filterGear) qs.set('filterGear', filterGear);
      const textBits = Object.entries(filters)
        .filter(([k, v]) => k !== 'model' && k !== 'gear' && v)
        .map(([, v]) => v as string)
        .join(' ')
        .trim();
      const qParts: string[] = [];
      if (textBits) qParts.push(textBits);
      if (!data.cityId && data.cityName) qParts.push(data.cityName);
      const q = qParts.join(' ').trim();
      if (q) qs.set('q', q);
      nav({ pathname: '/listings', search: qs.toString() });
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="card">
      <h2>Akıllı arama</h2>
      <p className="muted">Ne aradığını doğal dilde yaz; filtreleri çıkarıp ilan listesine yönlendirelim.</p>
      <form className="form" onSubmit={onSubmit}>
        <label>
          Arama cümleniz
          <textarea value={prompt} onChange={(e) => setPrompt(e.target.value)} rows={5} />
        </label>
        <button className="btn primary" type="submit" disabled={loading}>
          {loading ? 'İşleniyor…' : 'Ara'}
        </button>
      </form>
    </div>
  );
}
