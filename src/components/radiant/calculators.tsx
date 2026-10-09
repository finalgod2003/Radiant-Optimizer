import { useState } from 'react';
import { Activity, ArrowRight, MousePointer2 } from 'lucide-react';

import { convertSensitivity, frameTime } from '@/lib/gaming-tools';

export type CalculatorLabels = {
  live: string;
  fpsTitle: string;
  fpsDescription: string;
  fpsLabel: string;
  perFrame: string;
  fpsNote: string;
  sensitivityTitle: string;
  sensitivityDescription: string;
  oldDpi: string;
  newDpi: string;
  currentSensitivity: string;
  newSensitivity: string;
  sensitivityNote: string;
  invalid: string;
};

export function Calculators({ labels }: { labels: CalculatorLabels }) {
  const [fps, setFps] = useState('144');
  const [oldDpi, setOldDpi] = useState('800');
  const [newDpi, setNewDpi] = useState('1600');
  const [sensitivity, setSensitivity] = useState('0.4');
  const duration = frameTime(Number(fps));
  const converted = convertSensitivity(
    Number(sensitivity),
    Number(oldDpi),
    Number(newDpi)
  );
  return (
    <div className="tool-grid">
      <article className="tool-card">
        <div className="tool-heading">
          <span className="tool-icon">
            <Activity size={22} />
          </span>
          <span className="live-label">
            <i />
            {labels.live}
          </span>
        </div>
        <h3>{labels.fpsTitle}</h3>
        <p>{labels.fpsDescription}</p>
        <div className="fps-controls">
          <label htmlFor="target-fps">{labels.fpsLabel}</label>
          <div className="unit-input">
            <input
              id="target-fps"
              type="number"
              min="1"
              step="any"
              inputMode="decimal"
              value={fps}
              onChange={(e) => setFps(e.target.value)}
              aria-invalid={duration === null}
            />
            <span>FPS</span>
          </div>
          <div className="fps-presets">
            {[60, 144, 240, 360].map((value) => (
              <button
                key={value}
                aria-pressed={fps === String(value)}
                onClick={() => setFps(String(value))}
              >
                {value}
                <small>FPS</small>
              </button>
            ))}
          </div>
        </div>
        <div className="tool-result" aria-live="polite">
          <div>
            <output>{duration === null ? '—' : duration.toFixed(2)}</output>
            <span>{labels.perFrame}</span>
          </div>
          <div className="frame-visual" aria-hidden="true">
            {Array.from({ length: 12 }, (_, i) => (
              <i
                key={i}
                style={{
                  height: `${16 + ((i * 13) % 35)}px`,
                  opacity: duration === null ? 0.2 : 0.35 + i / 20,
                }}
              />
            ))}
          </div>
        </div>
        {duration === null && <p className="field-error">{labels.invalid}</p>}
        <p className="tool-note">{labels.fpsNote}</p>
      </article>
      <article className="tool-card">
        <div className="tool-heading">
          <span className="tool-icon blue">
            <MousePointer2 size={22} />
          </span>
          <span className="live-label">
            <i />
            {labels.live}
          </span>
        </div>
        <h3>{labels.sensitivityTitle}</h3>
        <p>{labels.sensitivityDescription}</p>
        <div className="dpi-fields">
          <label>
            {labels.oldDpi}
            <input
              type="number"
              min="1"
              step="any"
              inputMode="decimal"
              value={oldDpi}
              onChange={(e) => setOldDpi(e.target.value)}
            />
          </label>
          <ArrowRight size={18} />
          <label>
            {labels.newDpi}
            <input
              type="number"
              min="1"
              step="any"
              inputMode="decimal"
              value={newDpi}
              onChange={(e) => setNewDpi(e.target.value)}
            />
          </label>
        </div>
        <label className="sensitivity-field">
          {labels.currentSensitivity}
          <input
            type="number"
            min="0.000001"
            step="any"
            inputMode="decimal"
            value={sensitivity}
            onChange={(e) => setSensitivity(e.target.value)}
            aria-invalid={converted === null}
          />
        </label>
        <div className="tool-result" aria-live="polite">
          <div>
            <output>
              {converted === null
                ? '—'
                : Number(converted.toPrecision(6)).toString()}
            </output>
            <span>{labels.newSensitivity}</span>
          </div>
          <span className="edpi">
            {converted === null
              ? '—'
              : Number((Number(sensitivity) * Number(oldDpi)).toPrecision(6))}
            <small>eDPI</small>
          </span>
        </div>
        {converted === null && <p className="field-error">{labels.invalid}</p>}
        <p className="tool-note">{labels.sensitivityNote}</p>
      </article>
    </div>
  );
}
