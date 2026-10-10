// Locale display names for the language switcher UI.
// Locales themselves are defined in project.inlang/settings.json and
// exposed at runtime via @/paraglide/runtime.js (locales, baseLocale).
export const localeNames: Record<string, string> = {
  en: 'English',
  zh: '简体中文',
  'zh-TW': '繁體中文',
  ja: '日本語',
  ko: '한국어',
  de: 'Deutsch',
  fr: 'Français',
  es: 'Español',
  'pt-BR': 'Português (Brasil)',
};

export const localeShortNames: Record<string, string> = {
  en: 'EN',
  zh: '简中',
  'zh-TW': '繁中',
  ja: '日本語',
  ko: '한국어',
  de: 'DE',
  fr: 'FR',
  es: 'ES',
  'pt-BR': 'PT',
};
