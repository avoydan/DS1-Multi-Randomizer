import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { mkdir, mkdtemp, rm } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import test from 'node:test';
import { AppLogger, logStageFor, parseLogLines, readLogTail } from '../../dist/core/appLog.js';
import { OperationTracker, readOperationStore } from '../../dist/core/operations.js';
import { createRedactor, redactionContext } from '../../dist/core/redaction.js';

// Structured logging and operation records (docs/DIAGNOSTICS.md): stages, levels, correlation ids, rotation, timings, and nothing secret on disk.
const fresh = () => mkdtemp(path.join(os.tmpdir(), 'ds1tr-ops-'));

test('log lines are JSON with a stage, a level and the correlation id, and secrets are removed as they are written', async () => {
  const root = await fresh();
  try {
    const secret = 'synthetic-session-token-0123456789';
    const logger = new AppLogger(path.join(root, 'logs', 'backend.log'), { redact: createRedactor(redactionContext({ secrets: [secret], env: {} })) });
    logger.info('GAME_DETECTION', `checked with token=${secret} and ${secret}`, { correlationId: 'c-1' });
    logger.warning('FOG_PLAN', 'a retry happened', { correlationId: 'c-1', code: 'FOG_SEED_UNSOLVABLE' });
    logger.error('APPLY', 'multi\nline', { correlationId: 'c-2' });
    logger.debug('DATA_TOOL', 'hidden unless debugging');
    const text = readFileSync(logger.file, 'utf8');
    assert.ok(!text.includes(secret));
    const lines = parseLogLines(text);
    assert.deepEqual(lines.map((line) => [line.level, line.stage, line.correlationId]), [['info', 'GAME_DETECTION', 'c-1'], ['warning', 'FOG_PLAN', 'c-1'], ['error', 'APPLY', 'c-2']]);
    assert.equal(lines[1].code, 'FOG_SEED_UNSOLVABLE');
    assert.ok(!lines[2].message.includes('\n'), 'one line per event');
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('the log rotates at its size limit and keeps a bounded number of files', async () => {
  const root = await fresh();
  try {
    const logger = new AppLogger(path.join(root, 'a.log'), { maxBytes: 2000, keep: 2 });
    for (let index = 0; index < 200; index += 1) logger.info('SERVER', `line ${index} ${'x'.repeat(40)}`);
    assert.ok(existsSync(path.join(root, 'a.log.1')) && existsSync(path.join(root, 'a.log.2')) && !existsSync(path.join(root, 'a.log.3')));
    assert.ok(readFileSync(path.join(root, 'a.log'), 'utf8').length < 2600);
    assert.ok(readLogTail(path.join(root, 'a.log'), 300).length <= 300);
    assert.equal(readLogTail(path.join(root, 'missing.log'), 300), '');
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('events of the pipeline map to the named stages', () => {
  const both = { items: true, enemies: true };
  assert.equal(logStageFor('verify', undefined, both), 'GAME_DETECTION');
  assert.equal(logStageFor('import', undefined, both), 'SNAPSHOT');
  assert.equal(logStageFor('combined', undefined, both), 'ITEM_PLAN');
  assert.equal(logStageFor('combined', undefined, { items: false, enemies: true }), 'ENEMY_PLAN');
  assert.equal(logStageFor('combined', 'data-tool', both), 'DATA_TOOL');
  assert.equal(logStageFor('fog', undefined, both), 'FOG_PLAN');
  assert.equal(logStageFor('fog', 'fog-host', both), 'FOG_PLAN');
  assert.equal(logStageFor('validation', undefined, both), 'MERGE');
  assert.equal(logStageFor('package', undefined, both), 'PACKAGE');
});

test('an operation records stage timings, event counts and windows, and the last failure survives the next success', async () => {
  const root = await fresh();
  try {
    let clock = Date.parse('2026-10-07T10:00:00.000Z');
    const now = () => new Date(clock);
    const tracker = new OperationTracker(root, new AppLogger(path.join(root, 'logs', 'backend.log')), now);
    const workspace = path.join(root, 'runs', 'r1');
    await mkdir(workspace, { recursive: true });
    const failing = tracker.begin('generate', { seedCode: 'DS1TR1-A', profile: 'ds1tr-gen-v1', workspacePath: workspace });
    failing.event({ type: 'progress', stage: 'verify', progress: 0.1, message: 'verify' });
    clock += 2000;
    failing.event({ type: 'progress', stage: 'combined', progress: 0.2, message: 'plan' });
    failing.event({ type: 'log', stage: 'combined', level: 'warning', message: 'a warning' });
    clock += 5000;
    failing.event({ type: 'log', stage: 'combined', level: 'info', message: 'DsrDataTool patch-enemies', component: 'data-tool' });
    clock += 3000;
    const record = failing.finish({ ok: false, code: 'COMBINED_RETRIES_EXHAUSTED', stage: 'combined' });
    assert.deepEqual(record.timings, { GAME_DETECTION: 2000, ITEM_PLAN: 5000, DATA_TOOL: 3000 });
    assert.deepEqual(record.counts, { info: 1, warning: 1, error: 0, events: 4 });
    assert.equal(record.windows.ITEM_PLAN.events, 2);
    assert.equal(record.stage, 'ITEM_PLAN');
    assert.equal(record.code, 'COMBINED_RETRIES_EXHAUSTED');
    assert.deepEqual(record.warnings, ['a warning']);
    assert.match(readFileSync(path.join(workspace, 'generation.log'), 'utf8'), /\[DATA_TOOL\] DsrDataTool patch-enemies/u);

    const good = tracker.begin('generate', { seedCode: 'DS1TR1-B' });
    good.finish({ ok: true });
    const store = readOperationStore(tracker.file);
    assert.equal(store.generate.ok, true);
    assert.equal(store.lastFailure.code, 'COMBINED_RETRIES_EXHAUSTED', 'the failure stays available after a later success');
    assert.notEqual(good.correlationId, failing.correlationId);
    assert.match(failing.correlationId, /^[0-9a-f-]{36}$/u);
  } finally { await rm(root, { recursive: true, force: true }); }
});

test('the correlation id is a diagnostic only: the generation settings and the Seed Code never see it', async () => {
  // The id lives in operations.ts, appLog.ts and the server's response; it is not imported by the code that derives seeds or settings.
  const { readFile } = await import('node:fs/promises');
  const { fileURLToPath } = await import('node:url');
  const sources = await Promise.all(['seeds.ts', 'seedCode.ts', 'canonicalSettings.ts', 'generationProfiles.ts'].map((name) => readFile(fileURLToPath(new URL(`./${name}`, import.meta.url)), 'utf8')));
  assert.equal(sources.length, 4);
  for (const source of sources) assert.ok(source.length > 100 && !/correlation/iu.test(source));
});
