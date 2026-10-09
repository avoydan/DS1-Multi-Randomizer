import assert from 'node:assert/strict';
import test from 'node:test';
import { ACTIVITY_ROUTES, ActivityTracker } from '../../dist/core/activity.js';

// What the backend is doing to files right now (docs/AUTO_UPDATE.md): the desktop shell asks before it restarts to install an update.

test('the tracker is idle, busy while an activity runs, and idle again when it ends (ending twice is harmless)', () => {
  const tracker = new ActivityTracker(() => new Date('2026-10-10T10:00:00.000Z'));
  assert.deepEqual(tracker.snapshot(), { busy: false, active: [] });
  const endGenerate = tracker.begin('generate');
  const endApply = tracker.begin('apply');
  assert.equal(tracker.snapshot().busy, true);
  assert.deepEqual(tracker.snapshot().active.map((entry) => entry.kind), ['generate', 'apply']);
  assert.equal(tracker.snapshot().active[0].since, '2026-10-10T10:00:00.000Z');
  endGenerate();
  endGenerate();
  assert.equal(tracker.snapshot().busy, true, 'the apply still runs');
  endApply();
  assert.equal(tracker.snapshot().busy, false);
});

test('a snapshot is a copy: changing it changes nothing', () => {
  const tracker = new ActivityTracker();
  tracker.begin('restore');
  tracker.snapshot().active.length = 0;
  assert.equal(tracker.snapshot().busy, true);
});

test('the routes that change or read game files for a long time count as activity; questions and decoding do not', () => {
  for (const route of ['/api/runs/generate', '/api/randomize', '/api/game/activate', '/api/game/restore', '/api/game/import', '/api/world-data/prepare', '/api/support/bundle', '/api/history/delete']) {
    assert.ok(ACTIVITY_ROUTES.has(route), route);
  }
  for (const route of ['/api/health', '/api/activity', '/api/game/state', '/api/seed/decode', '/api/log', '/api/settings/save', '/api/installation/validate', '/api/diagnostics']) {
    assert.equal(ACTIVITY_ROUTES.has(route), false, route);
  }
});
