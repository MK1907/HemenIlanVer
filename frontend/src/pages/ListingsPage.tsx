import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router-dom';
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

export function ListingsPage() {
  const [params] = useSearchParams();
  const [data, setData] = useState<Paged | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const qs = new URLSearchParams();
    const categoryId = params.get('categoryId');
    const cityId = params.get('cityId');
    const minPrice = params.get('minPrice');
    const maxPrice = params.get('maxPrice');
    const q = params.get('q');
    const sort = params.get('sort');
    const filterModel = params.get('filterModel');
    const filterGear = params.get('filterGear');
    const searchMode = params.get('searchMode');
    if (categoryId) qs.set('categoryId', categoryId);
    if (cityId) qs.set('cityId', cityId);
    if (minPrice) qs.set('minPrice', minPrice);
    if (maxPrice) qs.set('maxPrice', maxPrice);
    if (q) qs.set('q', q);
    if (filterModel) qs.set('filterModel', filterModel);
    if (filterGear) qs.set('filterGear', filterGear);
    if (sort) qs.set('sort', sort);
    if (searchMode) qs.set('searchMode', searchMode);
    qs.set('page', params.get('page') ?? '1');
    qs.set('pageSize', '20');

    api
      .get<Paged>(`/api/listings?${qs.toString()}`)
      .then((r) => setData(r.data))
      .catch(() => setError('İlanlar yüklenemedi.'));
  }, [params]);

  if (error) return <p className="error">{error}</p>;
  if (!data) return <p>Yükleniyor…</p>;

  return (
    <div className="listings">
      <div className="listings-header">
        <h2>İlanlar</h2>
        <p className="muted">{data.totalCount} sonuç</p>
      </div>
      <div className="listing-grid">
        {data.items.map((x) => (
          <Link key={x.id} to={`/listings/${x.id}`} className="listing-card">
            <div className="thumb">{x.primaryImageUrl ? <img src={x.primaryImageUrl} alt="" /> : <span>Görsel yok</span>}</div>
            <div className="body">
              <h3>{x.title}</h3>
              <p className="meta">
                {x.cityName}
                {x.districtName ? ` / ${x.districtName}` : ''} · {x.categoryName}
              </p>
              <p className="price">
                {x.price != null ? `${x.price.toLocaleString('tr-TR')} ${x.currency}` : 'Fiyat sorunuz'}
              </p>
            </div>
          </Link>
        ))}
      </div>
    </div>
  );
}
