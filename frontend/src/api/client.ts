import axios from 'axios';

const baseURL = import.meta.env.VITE_API_URL ?? 'http://localhost:8080';

export const api = axios.create({
  baseURL,
  headers: { 'Content-Type': 'application/json' },
});

api.interceptors.request.use((config) => {
  const token = localStorage.getItem('accessToken');
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

api.interceptors.response.use(
  (res) => res,
  (error) => {
    if (!axios.isAxiosError(error) || error.response?.status !== 401) {
      return Promise.reject(error);
    }

    const url = error.config?.url ?? '';
    if (url.includes('/api/auth/login') || url.includes('/api/auth/register')) {
      return Promise.reject(error);
    }

    const path = window.location.pathname;
    if (path.startsWith('/login') || path.startsWith('/register')) {
      return Promise.reject(error);
    }

    localStorage.removeItem('user');
    localStorage.removeItem('accessToken');
    localStorage.removeItem('refreshToken');

    const back = `${window.location.pathname}${window.location.search}`;
    const safe =
      back.startsWith('/') && !back.startsWith('//') ? encodeURIComponent(back) : encodeURIComponent('/');
    window.location.assign(`/login?returnUrl=${safe}`);
    return Promise.reject(error);
  }
);
