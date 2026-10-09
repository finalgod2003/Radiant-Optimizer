import { useEffect, useRef, useState } from 'react';
import type { Game } from '@/data/games';
import {
  ArrowUpRight,
  ExternalLink,
  LoaderCircle,
  Maximize2,
  Minimize2,
  Play,
  RotateCcw,
  Square,
} from 'lucide-react';

import { GameArtwork } from './game-card';

export type PlayerLabels = {
  launch: string;
  reload: string;
  stop: string;
  fullscreen: string;
  exitFullscreen: string;
  original: string;
  hint: string;
  loading: string;
  slow: string;
  fullscreenError: string;
  before: string;
  privacy: string;
  note: string;
  visit: string;
};

export function GamePlayer({
  game,
  labels,
}: {
  game: Game;
  labels: PlayerLabels;
}) {
  const [active, setActive] = useState(false);
  const [loaded, setLoaded] = useState(false);
  const [slow, setSlow] = useState(false);
  const [revision, setRevision] = useState(0);
  const [fullscreen, setFullscreen] = useState(false);
  const [error, setError] = useState('');
  const container = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!active) return;
    setLoaded(false);
    setSlow(false);
    const timer = window.setTimeout(() => setSlow(true), 15000);
    return () => window.clearTimeout(timer);
  }, [active, revision]);
  useEffect(() => {
    const update = () =>
      setFullscreen(document.fullscreenElement === container.current);
    document.addEventListener('fullscreenchange', update);
    return () => document.removeEventListener('fullscreenchange', update);
  }, []);
  async function toggleFullscreen() {
    try {
      setError('');
      if (document.fullscreenElement) await document.exitFullscreen();
      else if (container.current?.requestFullscreen)
        await container.current.requestFullscreen();
      else setError(labels.fullscreenError);
    } catch {
      setError(labels.fullscreenError);
    }
  }
  async function stopGame() {
    setActive(false);
    if (document.fullscreenElement === container.current) {
      try {
        await document.exitFullscreen();
      } catch {
        setError(labels.fullscreenError);
      }
    }
  }
  return (
    <div className="player-shell" ref={container}>
      <div className="player-toolbar">
        <span className="player-name">
          <span className="status-dot" />
          {game.title}
        </span>
        <div>
          {active && (
            <>
              <button
                onClick={() => setRevision((value) => value + 1)}
                aria-label={labels.reload}
                title={labels.reload}
              >
                <RotateCcw size={17} />
              </button>
              <button
                onClick={stopGame}
                aria-label={labels.stop}
                title={labels.stop}
              >
                <Square size={16} />
              </button>
            </>
          )}
          {(active || fullscreen) && (
            <button
              onClick={toggleFullscreen}
              aria-label={
                fullscreen ? labels.exitFullscreen : labels.fullscreen
              }
              title={fullscreen ? labels.exitFullscreen : labels.fullscreen}
            >
              {fullscreen ? <Minimize2 size={17} /> : <Maximize2 size={17} />}
            </button>
          )}
          <a
            href={game.url}
            target="_blank"
            rel="noopener noreferrer"
            aria-label={labels.original}
            title={labels.original}
          >
            <ExternalLink size={17} />
            <span>{labels.original}</span>
          </a>
        </div>
      </div>
      <div className="player-stage">
        {active ? (
          <>
            <iframe
              key={`${game.id}-${revision}`}
              src={game.url}
              title={game.title}
              allow="autoplay; fullscreen; gamepad"
              allowFullScreen
              sandbox="allow-scripts allow-same-origin allow-pointer-lock allow-forms allow-downloads"
              referrerPolicy="strict-origin-when-cross-origin"
              onLoad={() => setLoaded(true)}
            />
            {!loaded && (
              <div className="player-loading">
                <LoaderCircle className="spin" size={24} />
                {labels.loading}
              </div>
            )}
          </>
        ) : (
          <>
            <GameArtwork game={game} eager />
            <div className="player-start">
              <span className="player-start-icon">
                {game.mode === 'embed' ? (
                  <Play size={26} fill="currentColor" />
                ) : (
                  <ArrowUpRight size={28} />
                )}
              </span>
              <h2>{game.title}</h2>
              <p>{game.mode === 'embed' ? labels.before : labels.note}</p>
              {game.mode === 'embed' ? (
                <button
                  className="button button-primary"
                  onClick={() => setActive(true)}
                >
                  <Play size={16} fill="currentColor" />
                  {labels.launch}
                </button>
              ) : (
                <a
                  className="button button-primary"
                  href={game.url}
                  target="_blank"
                  rel="noopener noreferrer"
                >
                  {labels.visit}
                  <ArrowUpRight size={17} />
                </a>
              )}
              <small>{labels.privacy}</small>
            </div>
          </>
        )}
      </div>
      <div className="player-help">
        <span>{active ? labels.hint : labels.note}</span>
        {active && slow && (
          <a href={game.url} target="_blank" rel="noopener noreferrer">
            {labels.slow}
            <ArrowUpRight size={14} />
          </a>
        )}
        {error && (
          <span role="status" className="field-error">
            {error}
          </span>
        )}
      </div>
    </div>
  );
}
