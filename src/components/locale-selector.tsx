import { ChevronDown, Globe, Languages } from 'lucide-react';

import { localeNames, localeShortNames } from '@/config/locale';
import { cn } from '@/lib/utils';
import { getLocale, locales, setLocale } from '@/paraglide/runtime.js';
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu';

export function LocaleSelector({
  variant = 'icon',
  className,
  label = 'Language',
}: {
  variant?: 'icon' | 'pill' | 'compact';
  className?: string;
  label?: string;
}) {
  const locale = getLocale();

  function handleSwitch(newLocale: string) {
    if (newLocale === locale) return;
    // Writes the locale cookie and reloads on the localized URL.
    setLocale(newLocale as typeof locale);
  }

  return (
    <DropdownMenu>
      <DropdownMenuTrigger
        aria-label={`${label}: ${localeNames[locale] || locale}`}
        className={cn(
          'inline-flex shrink-0 items-center whitespace-nowrap transition-colors outline-none focus-visible:ring-2 focus-visible:ring-current',
          variant === 'icon'
            ? 'text-muted-foreground hover:bg-accent hover:text-accent-foreground size-8 justify-center rounded-md'
            : 'h-10 gap-2 rounded-md border px-3 text-sm',
          className
        )}
      >
        {variant === 'icon' ? (
          <>
            <Languages className="size-4" />
            <span className="sr-only">{label}</span>
          </>
        ) : (
          <>
            <Globe className="size-4" />
            <span lang={locale}>
              {(variant === 'compact'
                ? localeShortNames[locale]
                : localeNames[locale]) || locale}
            </span>
            <ChevronDown className="size-4 opacity-70" />
          </>
        )}
      </DropdownMenuTrigger>
      <DropdownMenuContent align="end" className="min-w-48" aria-label={label}>
        <DropdownMenuRadioGroup value={locale} onValueChange={handleSwitch}>
          {locales.map((loc) => (
            <DropdownMenuRadioItem key={loc} value={loc} lang={loc}>
              {localeNames[loc] || loc}
            </DropdownMenuRadioItem>
          ))}
        </DropdownMenuRadioGroup>
      </DropdownMenuContent>
    </DropdownMenu>
  );
}
