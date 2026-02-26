import en from './en.json';

const locales = { en };
let current = 'en';
let strings = locales.en;

export function setLocale(locale) {
  if (locales[locale]) {
    current = locale;
    strings = locales[locale];
    localStorage.setItem('locale', locale);
    document.documentElement.lang = locale;
  }
}

export function getLocale() { return current; }
export function getAvailableLocales() { return Object.keys(locales); }

/**
 * Translate a dotted key path, with optional positional replacements.
 * t('toast.requestFailed', 404) => "Request failed (404)"
 * t('confirm.removeGameMsg', 'CS2') => 'Remove "CS2" from selected games?'
 */
export function t(key, ...args) {
  const parts = key.split('.');
  let val = strings;
  for (const p of parts) {
    if (val == null || typeof val !== 'object') return key;
    val = val[p];
  }
  if (val == null) return key;
  if (Array.isArray(val)) return val;
  if (typeof val !== 'string') return key;
  if (args.length === 0) return val;
  return val.replace(/\{(\d+)\}/g, (_, i) => args[i] ?? '');
}

/** Get a random tip */
export function randomTip() {
  const tips = t('tips');
  return Array.isArray(tips) ? tips[Math.floor(Math.random() * tips.length)] : '';
}

// Init from localStorage
const saved = localStorage.getItem('locale');
if (saved && locales[saved]) setLocale(saved);