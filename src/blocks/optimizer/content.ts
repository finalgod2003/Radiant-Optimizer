import {
  Activity,
  Cpu,
  Gamepad2,
  Gauge,
  History,
  Keyboard,
  Network,
  SlidersHorizontal,
} from 'lucide-react';

import { m } from '@/paraglide/messages.js';
import type { FeatureItem } from '@/components/optimizer/product-sections';

export function optimizerFeatures(): FeatureItem[] {
  return [
    {
      icon: Cpu,
      title: m['optimizer.feature.hardware.title'](),
      description: m['optimizer.feature.hardware.description'](),
      detail: m['optimizer.feature.hardware.detail'](),
    },
    {
      icon: Gamepad2,
      title: m['optimizer.feature.games.title'](),
      description: m['optimizer.feature.games.description'](),
      detail: m['optimizer.feature.games.detail'](),
    },
    {
      icon: Gauge,
      title: m['optimizer.feature.monitoring.title'](),
      description: m['optimizer.feature.monitoring.description'](),
      detail: m['optimizer.feature.monitoring.detail'](),
    },
    {
      icon: SlidersHorizontal,
      title: m['optimizer.feature.settings.title'](),
      description: m['optimizer.feature.settings.description'](),
      detail: m['optimizer.feature.settings.detail'](),
    },
    {
      icon: History,
      title: m['optimizer.feature.restore.title'](),
      description: m['optimizer.feature.restore.description'](),
      detail: m['optimizer.feature.restore.detail'](),
    },
    {
      icon: Activity,
      title: m['optimizer.feature.process.title'](),
      description: m['optimizer.feature.process.description'](),
      detail: m['optimizer.feature.process.detail'](),
    },
    {
      icon: Network,
      title: m['optimizer.feature.network.title'](),
      description: m['optimizer.feature.network.description'](),
      detail: m['optimizer.feature.network.detail'](),
    },
    {
      icon: Keyboard,
      title: m['optimizer.feature.input.title'](),
      description: m['optimizer.feature.input.description'](),
      detail: m['optimizer.feature.input.detail'](),
    },
  ];
}

export function optimizerFaqs() {
  return [
    {
      question: m['optimizer.faq.performance.q'](),
      answer: m['optimizer.faq.performance.a'](),
    },
    {
      question: m['optimizer.faq.browser.q'](),
      answer: m['optimizer.faq.browser.a'](),
    },
    {
      question: m['optimizer.faq.recovery.q'](),
      answer: m['optimizer.faq.recovery.a'](),
    },
    {
      question: m['optimizer.faq.premium.q'](),
      answer: m['optimizer.faq.premium.a'](),
    },
    {
      question: m['optimizer.faq.games.q'](),
      answer: m['optimizer.faq.games.a'](),
    },
    {
      question: m['optimizer.faq.capture.q'](),
      answer: m['optimizer.faq.capture.a'](),
    },
  ];
}
