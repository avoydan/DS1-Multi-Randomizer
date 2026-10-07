import assert from 'node:assert/strict';
import test from 'node:test';
import { parseSaveRequest, safeFileName, saveBytes } from '../dist-electron/saveFile.js';

// The renderer can ask the main process to save a document, but never chooses the path or the file type outside a short list.
test('file names are reduced to a safe name with the extension of their kind', () => {
  assert.equal(safeFileName('DS1TR-06NJDCP6-hints-full.html', 'html'), 'DS1TR-06NJDCP6-hints-full.html');
  assert.equal(safeFileName('..\\..\\Windows\\evil.exe', 'txt'), 'evil.txt');
  assert.equal(safeFileName('C:\\Users\\x\\a:b*c?.json', 'json'), 'a_b_c_.json');
  assert.equal(safeFileName('', 'zip'), 'DS1TR-export.zip');
  assert.equal(safeFileName(undefined, 'html'), 'DS1TR-export.html');
  assert.equal(safeFileName('report.html.exe', 'html'), 'report.html.html');
  assert.ok(safeFileName('x'.repeat(500), 'txt').length <= 124);
});

test('only the known kinds with string content are accepted', () => {
  assert.deepEqual(parseSaveRequest({ kind: 'html', defaultName: 'a.html', content: '<p>x</p>' }), { kind: 'html', defaultName: 'a.html', content: '<p>x</p>' });
  for (const bad of [null, 'x', 7, { kind: 'exe', defaultName: 'a', content: 'x' }, { kind: 'html', defaultName: 'a' }, { kind: 'html', defaultName: 'a', content: 5 }, { content: 'x' }]) assert.equal(parseSaveRequest(bad), null);
});

test('text is written as UTF-8 and a zip arrives as base64', () => {
  assert.equal(saveBytes({ kind: 'txt', defaultName: 'a.txt', content: 'zażółć' }).toString('utf8'), 'zażółć');
  assert.deepEqual([...saveBytes({ kind: 'zip', defaultName: 'a.zip', content: Buffer.from([80, 75, 3, 4]).toString('base64') })], [80, 75, 3, 4]);
});
