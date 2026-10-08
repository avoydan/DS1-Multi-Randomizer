import type { ReactNode } from 'react';

/** Small line icons drawn inline (no icon font, no network). They are decoration: the text next to each one carries the meaning. */
const paths: Record<string, ReactNode> = {
  game: <><rect x="3" y="7" width="18" height="11" rx="4" /><path d="M8 10.5v4M6 12.5h4M16 11h.01M18 14h.01" /></>,
  preset: <><path d="M7 3h7l4 4v14H7z" /><path d="M14 3v4h4M10 12h5M10 16h5" /></>,
  seed: <><circle cx="8" cy="15" r="4" /><path d="M11 12l9-9M16 7l2 2M14 9l2 2" /></>,
  transfer: <><path d="M7 4v16M7 4L4 7M7 4l3 3M17 20V4M17 20l-3-3M17 20l3-3" /></>,
  gear: <><circle cx="12" cy="12" r="3" /><path d="M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M4.9 19.1L7 17M17 7l2.1-2.1" /></>,
  info: <><circle cx="12" cy="12" r="9" /><path d="M12 11v6M12 7.5h.01" /></>,
  star: <path d="M12 3l2.7 5.6 6.1.8-4.5 4.2 1.1 6-5.4-3-5.4 3 1.1-6L3.2 9.4l6.1-.8z" />,
  sliders: <><path d="M4 7h10M18 7h2M4 17h2M10 17h10" /><circle cx="16" cy="7" r="2" /><circle cx="8" cy="17" r="2" /></>,
  chevron: <path d="M6 9l6 6 6-6" />,
  items: <><path d="M4 9h16v11H4zM3 5h18v4H3z" /><path d="M12 5v15" /></>,
  enemies: <><path d="M5 19L19 5M14 5h5v5M5 5l14 14M5 14v5h5" /></>,
  bosses: <><path d="M5 13a7 7 0 0114 0v3l-2 1v3H7v-3l-2-1z" /><path d="M9.5 13h.01M14.5 13h.01M11 17h2" /></>,
  fog: <><path d="M5 21V10a7 7 0 0114 0v11" /><path d="M9 21v-8a3 3 0 016 0v8" /></>,
  keys: <><circle cx="8" cy="15" r="4" /><path d="M11 12l9-9M16 7l2 2" /></>,
  other: <><circle cx="12" cy="12" r="3" /><path d="M12 3v2M12 19v2M3 12h2M19 12h2" /></>,
  doc: <><path d="M7 3h7l4 4v14H7z" /><path d="M10 12h5M10 16h5" /></>,
  crown: <><path d="M3 8l4.5 4L12 5l4.5 7L21 8l-2 11H5z" /></>,
  dice: <><rect x="4" y="4" width="16" height="16" rx="3" /><path d="M8.5 8.5h.01M15.5 8.5h.01M12 12h.01M8.5 15.5h.01M15.5 15.5h.01" /></>,
  warning: <><path d="M12 3l10 18H2z" /><path d="M12 10v5M12 18h.01" /></>,
  close: <path d="M6 6l12 12M18 6L6 18" />,
  back: <path d="M19 12H5M11 6l-6 6 6 6" />,
  arrow: <path d="M5 12h14M13 6l6 6-6 6" />,
  question: <><path d="M9.2 9a3 3 0 115 2c-1 .8-2 1.2-2 3" /><path d="M12 17.5h.01" /></>,
};

export type IconName = keyof typeof paths;

export function Icon({ name, size = 20 }: { name: IconName; size?: number }) {
  return (
    <svg className="icon" width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true" focusable="false">
      {paths[name]}
    </svg>
  );
}
