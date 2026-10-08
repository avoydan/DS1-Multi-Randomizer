import { useCallback } from 'react';
import { useTranslation } from 'react-i18next';

/**
 * A translation lookup by a key that is only known at run time (a setting id, a reason id). The key set is checked by the tests (ui/settingsCoverage.test.mjs),
 * not by the compiler, because the ids come from the registry.
 */
export type Tx = (key: string, options?: Record<string, unknown>) => string;

export function useTx(): { tx: Tx; exists: (key: string) => boolean } {
  const { t, i18n } = useTranslation();
  const tx = useCallback<Tx>((key, options) => t(key as never, options as never) as unknown as string, [t]);
  const exists = useCallback((key: string) => i18n.exists(key), [i18n]);
  return { tx, exists };
}

export const settingLabelKey = (id: string): string => `settings.${id}.label`;
export const settingChoiceKey = (id: string, value: string): string => `settings.${id}.options.${value}`;
export const helpKey = (id: string, part: 'short' | 'what' | 'affects' | 'limits' | 'example'): string => `help.settings.${id}.${part}`;
export const helpChoiceKey = (id: string, value: string): string => `help.settings.${id}.choices.${value}`;
