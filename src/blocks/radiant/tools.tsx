import { m } from '@/paraglide/messages.js';
import { Calculators } from '@/components/radiant/calculators';

export function GamingTools() {
  return (
    <section id="tools" className="tools-section">
      <div className="site-width">
        <div className="section-heading">
          <div>
            <p className="eyebrow">
              <span className="section-number">02 /</span>
              {m['radiant.tools.eyebrow']()}
            </p>
            <h2>{m['radiant.tools.title']()}</h2>
            <p className="section-description">
              {m['radiant.tools.description']()}
            </p>
          </div>
          <span className="free-tag">{m['radiant.tools.free']()}</span>
        </div>
        <Calculators
          labels={{
            live: m['radiant.tools.live'](),
            fpsTitle: m['radiant.tools.fps'](),
            fpsDescription: m['radiant.tools.fpsdesc'](),
            fpsLabel: m['radiant.tools.fpslabel'](),
            perFrame: m['radiant.tools.perframe'](),
            fpsNote: m['radiant.tools.fpsnote'](),
            sensitivityTitle: m['radiant.tools.sens'](),
            sensitivityDescription: m['radiant.tools.sensdesc'](),
            oldDpi: m['radiant.tools.olddpi'](),
            newDpi: m['radiant.tools.newdpi'](),
            currentSensitivity: m['radiant.tools.currentsens'](),
            newSensitivity: m['radiant.tools.newsens'](),
            sensitivityNote: m['radiant.tools.sensnote'](),
            invalid: m['radiant.tools.invalid'](),
          }}
        />
      </div>
    </section>
  );
}
