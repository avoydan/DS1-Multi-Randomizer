import assert from 'node:assert/strict';
import test from 'node:test';
import { createRedactor, findLeaks, redactJson } from '../../dist/core/redaction.js';

// Redaction (docs/SUPPORT_BUNDLE.md): synthetic secrets and personal values are planted in realistic text and JSON; none of them may survive, and the data
// a support person needs (Seed Codes, hashes, versions, timestamps) must.
const context = {
  userName: 'Adam',
  hostName: 'DESKTOP-ZX81Q',
  directories: [
    { path: 'C:\\Users\\Adam\\AppData\\Roaming\\DS1 Triple Randomizer', label: '<USER_DATA>' },
    { path: 'D:\\Games\\Steam\\steamapps\\common\\DARK SOULS REMASTERED', label: '<GAME_DIR>' },
    { path: 'C:\\Users\\Adam\\AppData\\Local\\Temp', label: '<TEMP>' },
    { path: 'C:\\Users\\Adam', label: 'C:\\Users\\<USER>' },
  ],
  secrets: ['6f1c2d9e-aaaa-4bbb-8ccc-0123456789ab'],
};
const redact = createRedactor(context);

const planted = [
  'ANTHROPIC_API_KEY=sk-ant-api03-ABCDEFGHIJKLMNOPQRSTUVWX',
  'password=hunter2hunter2',
  'token=abcd1234efgh5678',
  'Authorization: Bearer abcdefghijklmnop12345',
  'sessionToken: "s3ss10n-t0k3n value with spaces"',
  '"password": "my secret passphrase"',
  'X-DS1TR-Token: 6f1c2d9e-aaaa-4bbb-8ccc-0123456789ab',
  'the bare token 6f1c2d9e-aaaa-4bbb-8ccc-0123456789ab appeared alone',
  'https://user:p4ssw0rd@example.org/path?token=zzzzzzzzzzzz',
  'Cookie: sid=abc123; theme=dark',
  'ghp_abcdefghijklmnopqrstuvwxyz0123456789',
  'eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.abcdefghijk',
  'C:\\Users\\Adam\\Documents\\cert\\signing.pfx',
  'CSC_KEY_PASSWORD=correct-horse-battery',
  'MAC 00:1A:2B:3C:4D:5E and {4D36E972-E325-11CE-BFC1-08002BE10318} and 192.168.1.20 and fe80::1ff:fe23:4567:890a',
  'mail me at adam@example.org',
];

test('every planted secret and identifier is gone from the text', () => {
  const out = redact(planted.join('\n'));
  for (const needle of ['sk-ant-api03', 'hunter2', 'abcd1234efgh', 'abcdefghijklmnop12345', 's3ss10n', 'my secret passphrase', '6f1c2d9e', 'p4ssw0rd', 'zzzzzzzzzzzz', 'sid=abc123',
    'ghp_abcdef', 'eyJhbGci', 'signing.pfx', 'correct-horse', '00:1A:2B', '4D36E972', '192.168.1.20', 'fe80::', 'adam@example.org']) {
    assert.ok(!out.includes(needle), `${needle} must be redacted`);
  }
  assert.ok(out.includes('ANTHROPIC_API_KEY=<REDACTED>'));
  assert.ok(out.includes('Bearer <REDACTED>'));
  assert.deepEqual(findLeaks(out, context), []);
});

test('paths are normalized in every spelling, keeping the tail and the separator style', () => {
  const out = redact([
    'C:\\Users\\Adam\\AppData\\Roaming\\DS1 Triple Randomizer\\state\\runs\\abc',
    'c:/users/adam/appdata/roaming/ds1 triple randomizer/state',
    '{"path":"C:\\\\Users\\\\Adam\\\\AppData\\\\Roaming\\\\DS1 Triple Randomizer\\\\logs\\\\app.log"}',
    'file:///C:/Users/Adam/AppData/Local/Temp/x',
    'D:\\Games\\Steam\\steamapps\\common\\DARK SOULS REMASTERED\\param\\GameParam\\GameParam.parambnd.dcx',
    'C:\\Users\\Someone Else\\Desktop\\a.txt',
    '/home/bob/.config/x',
  ].join('\n'));
  assert.ok(out.includes('<USER_DATA>\\state\\runs\\abc'));
  assert.ok(out.includes('<USER_DATA>/state'));
  assert.ok(out.includes('"path":"<USER_DATA>\\\\logs\\\\app.log"'), out);
  assert.ok(out.includes('<TEMP>/x'));
  assert.ok(out.includes('<GAME_DIR>\\param\\GameParam\\GameParam.parambnd.dcx'));
  assert.ok(out.includes('C:\\Users\\<USER>\\Desktop\\a.txt'));
  assert.ok(out.includes('/home/<USER>/.config/x'));
  assert.ok(!/adam/iu.test(out) && !out.includes('Someone Else') && !out.includes('bob'));
  JSON.parse(`{"p":"${'C:\\\\Users\\\\Adam\\\\x'.replace(/Adam/u, 'Adam')}"}`);
  assert.doesNotThrow(() => JSON.parse(redact('{"path":"C:\\\\Users\\\\Adam\\\\AppData\\\\Roaming\\\\DS1 Triple Randomizer\\\\logs\\\\app.log","n":1}')), 'redacted JSON stays valid JSON');
});

test('the user name and host name are replaced as whole words only (an item called Adamantite is not touched)', () => {
  const out = redact('Hello Adam, this is DESKTOP-ZX81Q. Adamantite Twin Greatsword; adam!');
  assert.equal(out, 'Hello <USER>, this is <HOST>. Adamantite Twin Greatsword; <USER>!');
  assert.deepEqual(findLeaks('Adamantite Twin Greatsword', context), [], 'the leak check uses the same word boundaries');
  assert.deepEqual(findLeaks('see C:\\Users\\Adam\\x', context), ['user name', 'folder C:\\Users\\<USER>']);
});

test('what a support person needs survives: Seed Codes, hashes, versions, timestamps, run ids', () => {
  const keep = [
    'DS1TR1-06NJDCP6-ABCD-EFGH-2G', 'a'.repeat(64), '0123456789abcdef'.repeat(4), '2026-10-07T18:30:00.000Z', 'run 3f2b8c1e-1111-4222-8333-444455556666', 'desktop 0.31.0 backend 0.43.0',
    'ds1tr-gen-v1', 'FOG_RETRIES_EXHAUSTED', 'at 18:30:00 the stage PACKAGE ended',
  ];
  assert.equal(redact(keep.join('\n')), keep.join('\n'));
});

test('a user name inside a Seed Code is not corrupted', () => {
  const tricky = createRedactor({ ...context, userName: 'AAAA' });
  assert.equal(tricky('code DS1TR1-AAAA-BBBB-AAAA and AAAA'), 'code DS1TR1-AAAA-BBBB-AAAA and <USER>');
});

test('redactJson redacts values and keys, keeps structure, and leaves version strings alone', () => {
  const out = redactJson({
    path: 'C:\\Users\\Adam\\x', 'C:\\Users\\Adam': 1, nested: [{ apiKey: 'sk-1234567890abcdefghijkl', ok: true, n: 3 }], fileVersion: '0.1.0.0', note: 'ip 10.0.0.1',
  }, redact);
  assert.equal(out.path, 'C:\\Users\\<USER>\\x');
  assert.deepEqual(Object.keys(out).slice(0, 2), ['path', 'C:\\Users\\<USER>']);
  assert.equal(out.nested[0].apiKey, '<REDACTED>');
  assert.equal(out.nested[0].ok, true);
  assert.equal(out.fileVersion, '0.1.0.0');
  assert.equal(out.note, 'ip <IP>');
});

test('the redactor counts replacements without exposing values', () => {
  const local = createRedactor(context);
  local('password=abcdefgh and token=zzzzzzzz');
  assert.equal(local.count(), 2);
});
