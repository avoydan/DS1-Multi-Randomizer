import { useEffect, useState } from 'react';
import { useTranslation } from 'react-i18next';
import { applyLanguage, type LanguageSetting } from './i18n';

/**
 * Language: Automatic / English / Polski. The choice is saved by the main process; switching re-renders immediately. The language never
 * reaches a world: it is not in the Seed Code or the generation settings.
 */
export function LanguageSelect() {
  const { t } = useTranslation();
  const [setting, setSetting] = useState<LanguageSetting>('auto');

  useEffect(() => {
    void window.desktop?.language().then((info) => setSetting(info.setting)).catch(() => undefined);
  }, []);

  const change = async (value: LanguageSetting) => {
    setSetting(value);
    const info = await window.desktop?.setLanguage(value);
    await applyLanguage(info?.resolved ?? (value === 'pl' ? 'pl' : 'en'));
  };

  return (
    <label className="language-select">
      <span>{t('common.language.label')}</span>
      <select value={setting} aria-label={t('common.language.label')} onChange={(event) => void change(event.target.value as LanguageSetting)}>
        <option value="auto">{t('common.language.auto')}</option>
        <option value="en">{t('common.language.english')}</option>
        <option value="pl">{t('common.language.polish')}</option>
      </select>
    </label>
  );
}
