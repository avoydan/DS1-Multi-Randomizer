export const GPL_LICENSE_URL = 'https://www.gnu.org/licenses/gpl-3.0.html';

/** Only the application's own document may navigate inside its Electron window. */
export function isAllowedRendererNavigation(targetUrl: string, applicationUrl: string): boolean {
  try {
    const target = new URL(targetUrl);
    const application = new URL(applicationUrl);
    if (target.protocol !== application.protocol || target.host !== application.host) return false;
    if (application.protocol === 'file:') {
      return target.pathname.toLowerCase() === application.pathname.toLowerCase()
        && target.search === application.search;
    }
    return target.origin === application.origin;
  } catch {
    return false;
  }
}

/** The sole user-facing external page opened from an Electron child-window request. */
export function isAllowedExternalWindow(targetUrl: string): boolean {
  return targetUrl === GPL_LICENSE_URL;
}

/** Denies every Electron child window and delegates only the exact license link to the host browser. */
export function handleWindowOpenRequest(
  targetUrl: string,
  openExternal: (url: string) => void,
): { action: 'deny' } {
  if (isAllowedExternalWindow(targetUrl)) openExternal(targetUrl);
  return { action: 'deny' };
}
