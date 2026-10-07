import 'i18next';
import type { resources } from './resources';

declare module 'i18next' {
  interface CustomTypeOptions {
    defaultNS: 'translation';
    // English is the canonical resource: keys are typed from it.
    resources: (typeof resources)['en'];
    returnNull: false;
  }
}
