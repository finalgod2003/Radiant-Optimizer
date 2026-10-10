import { useId, useState, type ReactNode } from 'react';
import {
  ArrowRight,
  Check,
  ChevronDown,
  CircleCheck,
  Cpu,
  Eye,
  History,
  Monitor,
  Power,
  ScanLine,
  ShieldCheck,
  SlidersHorizontal,
  type LucideIcon,
} from 'lucide-react';

import { Link } from '@/core/i18n/navigation';

export type FeatureItem = {
  title: string;
  description: string;
  icon: LucideIcon;
  detail: string;
};

export function PageIntro({
  eyebrow,
  title,
  description,
  children,
}: {
  eyebrow: string;
  title: string;
  description: string;
  children?: ReactNode;
}) {
  return (
    <section className="op-page-intro op-width">
      <p className="op-eyebrow">
        <span />
        {eyebrow}
      </p>
      <h1>{title}</h1>
      <p className="op-lead">{description}</p>
      {children}
    </section>
  );
}

export function FeatureList({ items }: { items: FeatureItem[] }) {
  return (
    <div className="op-feature-list">
      {items.map((item, index) => (
        <article key={item.title} className="op-feature-row">
          <span className="op-index">{String(index + 1).padStart(2, '0')}</span>
          <item.icon size={25} strokeWidth={1.5} />
          <div>
            <h3>{item.title}</h3>
            <p>{item.description}</p>
          </div>
          <span className="op-feature-detail">{item.detail}</span>
        </article>
      ))}
    </div>
  );
}

export function SupportMatrix({
  title,
  columns,
  rows,
  note,
}: {
  title: string;
  columns: [string, string, string];
  rows: { name: string; source: string; scope: string }[];
  note: string;
}) {
  return (
    <div className="op-support-matrix">
      <table>
        <caption>{title}</caption>
        <thead>
          <tr>
            {columns.map((column) => (
              <th key={column} scope="col">
                {column}
              </th>
            ))}
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr key={row.name}>
              <th scope="row">{row.name}</th>
              <td>{row.source}</td>
              <td>{row.scope}</td>
            </tr>
          ))}
        </tbody>
      </table>
      <p>{note}</p>
    </div>
  );
}

export function WorkflowPreview({
  name,
  labels,
  steps,
  settings,
}: {
  name: string;
  labels: {
    preview: string;
    title: string;
    local: string;
    settings: string;
    supported: string;
    note: string;
    pending: string;
    plan: string;
    backup: string;
  };
  steps: { title: string; description: string; status: string }[];
  settings: string[];
}) {
  const [active, setActive] = useState(0);
  const id = useId();
  const icons = [ScanLine, Eye, History];
  const ActiveIcon = icons[active];
  return (
    <div className="op-preview-wrap">
      <div className="op-preview-caption">
        <span>{labels.preview}</span>
        <span className="op-caption-line" />
        <span>01 — 03</span>
      </div>
      <div className="op-preview">
        <div className="op-preview-bar">
          <span>
            <span className="op-dot" />
            {name}
          </span>
          <Monitor size={15} />
        </div>
        <div className="op-preview-body">
          <div className="op-preview-title">
            <div>
              <p>{labels.local}</p>
              <h2>{labels.title}</h2>
            </div>
            <Cpu size={29} strokeWidth={1.4} />
          </div>
          <div
            className="op-workflow-tabs"
            role="tablist"
            aria-label={labels.title}
          >
            {steps.map((step, index) => {
              const Icon = icons[index];
              return (
                <button
                  key={step.title}
                  id={`${id}-tab-${index}`}
                  role="tab"
                  type="button"
                  aria-selected={active === index}
                  aria-controls={`${id}-panel`}
                  tabIndex={active === index ? 0 : -1}
                  onClick={() => setActive(index)}
                  onKeyDown={(event) => {
                    let next = index;
                    if (event.key === 'ArrowRight')
                      next = (index + 1) % steps.length;
                    else if (event.key === 'ArrowLeft')
                      next = (index + steps.length - 1) % steps.length;
                    else if (event.key === 'Home') next = 0;
                    else if (event.key === 'End') next = steps.length - 1;
                    else return;
                    event.preventDefault();
                    setActive(next);
                    document.getElementById(`${id}-tab-${next}`)?.focus();
                  }}
                >
                  <Icon size={16} />
                  <span>{step.title}</span>
                </button>
              );
            })}
          </div>
          <div
            id={`${id}-panel`}
            role="tabpanel"
            aria-labelledby={`${id}-tab-${active}`}
            tabIndex={0}
            className="op-workflow-panel"
          >
            <div className="op-workflow-status">
              <ActiveIcon size={18} />
              <span>{steps[active].status}</span>
            </div>
            <p>{steps[active].description}</p>
            <div className="op-preview-setting-head">
              <span>{labels.settings}</span>
              <span>{labels.supported}</span>
            </div>
            {settings.map((setting, index) => (
              <div className="op-preview-setting" key={setting}>
                <span>
                  {index === 0 ? (
                    <Power size={15} />
                  ) : (
                    <SlidersHorizontal size={15} />
                  )}
                  {setting}
                </span>
                <span>
                  {active === 2 ? <History size={14} /> : <Check size={14} />}
                </span>
              </div>
            ))}
          </div>
          <div className="op-preview-foot">
            <ShieldCheck size={14} />
            <span>{labels.backup}</span>
            <span className="op-dot" />
          </div>
        </div>
      </div>
      <p className="op-preview-note">{labels.note}</p>
    </div>
  );
}

export function FaqList({
  items,
}: {
  items: { question: string; answer: string }[];
}) {
  return (
    <div className="op-faq">
      {items.map((item) => (
        <details key={item.question}>
          <summary>
            {item.question}
            <ChevronDown size={18} />
          </summary>
          <p>{item.answer}</p>
        </details>
      ))}
    </div>
  );
}

export function ProductCta({
  title,
  description,
  download,
  features,
}: {
  title: string;
  description: string;
  download: string;
  features: string;
}) {
  return (
    <section className="op-bottom-cta op-width">
      <div>
        <p className="op-eyebrow">
          <CircleCheck size={15} />
          WINDOWS / x64
        </p>
        <h2>{title}</h2>
        <p>{description}</p>
      </div>
      <div className="op-actions">
        <Link href="/download" className="op-button">
          {download}
          <ArrowRight size={17} />
        </Link>
        <Link href="/features" className="op-text-link">
          {features}
          <ArrowRight size={16} />
        </Link>
      </div>
    </section>
  );
}

export function QueryNotice({
  text,
  action,
  onAction,
  error = false,
}: {
  text: string;
  action?: string;
  onAction?: () => void;
  error?: boolean;
}) {
  return (
    <div
      className={`op-notice ${error ? 'op-notice-error' : ''}`}
      role={error ? 'alert' : 'status'}
    >
      <p>{text}</p>
      {action && onAction && (
        <button type="button" className="op-text-link" onClick={onAction}>
          {action}
          <ArrowRight size={15} />
        </button>
      )}
    </div>
  );
}
