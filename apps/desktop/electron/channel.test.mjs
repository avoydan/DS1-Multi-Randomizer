import assert from 'node:assert/strict';
import test from 'node:test';
import { isDevToolsShortcut, parseBuildChannel, parseNexusPageUrl, parseRuntimeBuildInfo } from '../dist-electron/channel.js';

test('a packaged app is public unless its build information says it is an internal build (the safe side is to show less)', () => {
  assert.equal(parseBuildChannel(null), 'public');
  assert.equal(parseBuildChannel('not json'), 'public');
  assert.equal(parseBuildChannel('{"schema":"ds1tr-build-info-1","mode":"development"}'), 'public', 'a build made before the channel existed is public');
  assert.equal(parseBuildChannel('{"channel":"public"}'), 'public');
  assert.equal(parseBuildChannel('{"channel":"internal"}'), 'internal');
  assert.equal(parseBuildChannel('{"channel":"INTERNAL"}'), 'public');
  assert.equal(parseBuildChannel('{"channel":true}'), 'public');
});

test('build distribution metadata defaults valid legacy records to GitHub and fails closed when metadata is invalid', () => {
  const legacy = parseRuntimeBuildInfo('{"schema":"ds1tr-build-info-1","channel":"public","version":"0.38.4"}');
  assert.deepEqual(legacy, { channel: 'public', distribution: 'github', nexusPageUrl: null });
  assert.deepEqual(parseRuntimeBuildInfo('{"schema":"ds1tr-build-info-1","channel":"internal"}'), { channel: 'internal', distribution: 'github', nexusPageUrl: null });
  assert.equal(parseRuntimeBuildInfo('{"schema":"ds1tr-build-info-1","channel":"public","distribution":"nexus"}').distribution, 'nexus');
  assert.equal(parseRuntimeBuildInfo('{"schema":"ds1tr-build-info-1","distribution":"github"}').distribution, 'github');
  for (const text of [null, 'not json', '{}', '{"schema":"wrong","distribution":"github"}', '{"schema":"ds1tr-build-info-1","distribution":null}', '{"schema":"ds1tr-build-info-1","distribution":"Nexus"}']) {
    assert.equal(parseRuntimeBuildInfo(text).distribution, 'unavailable', String(text));
  }
});

test('Nexus page metadata accepts only the canonical DSR page on the official HTTPS origin', () => {
  assert.equal(parseNexusPageUrl('https://www.nexusmods.com/darksoulsremastered/mods/1234'), 'https://www.nexusmods.com/darksoulsremastered/mods/1234');
  for (const url of [
    'http://www.nexusmods.com/darksoulsremastered/mods/1234',
    'https://nexusmods.com/darksoulsremastered/mods/1234',
    'https://www.nexusmods.com/darksoulsremastered/mods/1234?x=1',
    'https://www.nexusmods.com/darksoulsremastered/mods/1234#page',
    'https://www.nexusmods.com/othergame/mods/1234',
    'https://evil.example/darksoulsremastered/mods/1234',
  ]) assert.equal(parseNexusPageUrl(url), null, url);
  assert.equal(parseRuntimeBuildInfo('{"schema":"ds1tr-build-info-1","distribution":"nexus","nexusPageUrl":"https://evil.example/darksoulsremastered/mods/1234"}').nexusPageUrl, null);
});

test('the optional shortcut is exactly Ctrl+Shift+Alt+F12', () => {
  const base = { type: 'keyDown', key: 'F12', control: true, shift: true, alt: true };
  assert.equal(isDevToolsShortcut(base), true);
  for (const change of [{ control: false }, { shift: false }, { alt: false }, { key: 'F11' }, { type: 'keyUp' }, { meta: true }]) assert.equal(isDevToolsShortcut({ ...base, ...change }), false, JSON.stringify(change));
});
