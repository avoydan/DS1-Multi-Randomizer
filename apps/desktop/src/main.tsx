import React from 'react';
import ReactDOM from 'react-dom/client';
import { App } from './App';
import { initI18n, type AppLanguage } from './i18n';
import './styles.css';

/** The language is resolved by the main process (saved choice, else the operating system) and applied before the first render. */
async function bootstrap(): Promise<void> {
  let language: AppLanguage = 'en';
  try { language = (await window.desktop?.language())?.resolved ?? 'en'; } catch { language = 'en'; }
  await initI18n(language);
  ReactDOM.createRoot(document.getElementById('root')!).render(
    <React.StrictMode>
      <App />
    </React.StrictMode>,
  );
}

void bootstrap();
