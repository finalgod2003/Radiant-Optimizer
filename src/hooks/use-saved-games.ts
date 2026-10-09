import { useEffect, useState } from 'react';
import { games } from '@/data/games';

const KEY = 'radiant:saved-games:v1';
const validIds = new Set(games.map((game) => game.id));

function parseSaved(raw: string | null): string[] {
  try {
    const value: unknown = JSON.parse(raw ?? '[]');
    return Array.isArray(value)
      ? [
          ...new Set(
            value.filter(
              (id): id is string => typeof id === 'string' && validIds.has(id)
            )
          ),
        ]
      : [];
  } catch {
    return [];
  }
}

export function useSavedGames() {
  const [saved, setSaved] = useState<string[]>([]);
  const [ready, setReady] = useState(false);
  useEffect(() => {
    try {
      setSaved(parseSaved(localStorage.getItem(KEY)));
    } catch {
      /* Session-only bookmarks remain usable. */
    }
    setReady(true);
    const sync = (event: StorageEvent) => {
      if (event.key === KEY) setSaved(parseSaved(event.newValue));
    };
    window.addEventListener('storage', sync);
    return () => window.removeEventListener('storage', sync);
  }, []);
  useEffect(() => {
    if (!ready) return;
    try {
      localStorage.setItem(KEY, JSON.stringify(saved));
    } catch {
      /* Private browsing or full storage. */
    }
  }, [ready, saved]);
  return {
    saved,
    toggle: (id: string) =>
      setSaved((current) =>
        current.includes(id)
          ? current.filter((item) => item !== id)
          : [...current, id]
      ),
  };
}
