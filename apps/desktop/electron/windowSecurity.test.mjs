import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { fileURLToPath } from 'node:url';
import { GPL_LICENSE_URL, handleWindowOpenRequest, isAllowedExternalWindow, isAllowedRendererNavigation } from './windowSecurity.ts';

test('only the exact GPL license link is handed to the system browser', () => {
  assert.equal(GPL_LICENSE_URL, 'https://www.gnu.org/licenses/gpl-3.0.html');
  assert.equal(isAllowedExternalWindow(GPL_LICENSE_URL), true);
  assert.equal(isAllowedExternalWindow('https://example.com/'), false);
  assert.equal(isAllowedExternalWindow('https://www.gnu.org/licenses/gpl-3.0.html?redirect=1'), false);
  assert.equal(isAllowedExternalWindow('http://www.gnu.org/licenses/gpl-3.0.html'), false);
});

test('all child windows are denied, while only the GPL license URL is opened externally', () => {
  const opened = [];
  assert.deepEqual(handleWindowOpenRequest(GPL_LICENSE_URL, (url) => opened.push(url)), { action: 'deny' });
  assert.deepEqual(handleWindowOpenRequest('https://example.com/', (url) => opened.push(url)), { action: 'deny' });
  assert.deepEqual(opened, [GPL_LICENSE_URL]);
});

test('packaged renderer can stay on its app document but not navigate to other content', () => {
  const applicationUrl = 'file:///C:/Program%20Files/DS1%20Multi%20Randomizer/dist/index.html';
  assert.equal(isAllowedRendererNavigation(applicationUrl, applicationUrl), true);
  assert.equal(isAllowedRendererNavigation(`${applicationUrl}#settings`, applicationUrl), true);
  assert.equal(isAllowedRendererNavigation('file:///C:/Program%20Files/DS1%20Multi%20Randomizer/other.html', applicationUrl), false);
  assert.equal(isAllowedRendererNavigation('https://www.gnu.org/licenses/gpl-3.0.html', applicationUrl), false);
  assert.equal(isAllowedRendererNavigation('javascript:alert(1)', applicationUrl), false);
});

test('development renderer is limited to the configured local origin', () => {
  const applicationUrl = 'http://127.0.0.1:5173/';
  assert.equal(isAllowedRendererNavigation('http://127.0.0.1:5173/settings', applicationUrl), true);
  assert.equal(isAllowedRendererNavigation('http://localhost:5173/', applicationUrl), false);
  assert.equal(isAllowedRendererNavigation('https://example.com/', applicationUrl), false);
});

test('main window installs popup and navigation guards before loading renderer content', () => {
  const mainPath = fileURLToPath(new URL('./main.ts', import.meta.url));
  const source = readFileSync(mainPath, 'utf8');
  const windowOpenGuard = source.indexOf('win.webContents.setWindowOpenHandler');
  const navigationGuard = source.indexOf("win.webContents.on('will-navigate'");
  const redirectGuard = source.indexOf("win.webContents.on('will-redirect'");
  const firstRendererLoad = source.indexOf('await win.loadURL');
  assert.ok(windowOpenGuard >= 0 && windowOpenGuard < firstRendererLoad);
  assert.ok(navigationGuard >= 0 && navigationGuard < firstRendererLoad);
  assert.ok(redirectGuard >= 0 && redirectGuard < firstRendererLoad);
  assert.match(source, /setWindowOpenHandler\(\(\{ url \}\) => handleWindowOpenRequest\(url/u);
  assert.match(source, /shell\.openExternal\(externalUrl\)/u);
});
