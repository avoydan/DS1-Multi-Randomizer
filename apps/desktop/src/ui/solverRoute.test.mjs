import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import path from 'node:path';
import test from 'node:test';
import { fileURLToPath } from 'node:url';

// The solver route is reachable right after a successful generation, from the applied world and from the history (docs/SOLVER_WITNESS.md). These are
// wiring checks over the sources (the component itself has no logic beyond showing what the backend returns); the rendering is tested with the contracts.
const desktop = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');
const read = (relative) => readFileSync(path.join(desktop, relative), 'utf8');

test('internal QA only: every entry point is rendered only when the backend says this is an internal QA build (docs/INTERNAL_QA.md)', () => {
  const app = read('src/App.tsx');
  assert.match(app, /api\.capabilities\(\)\.then\(\(capabilities\) => setInternalQa\(capabilities\.internalQa === true\)\)\.catch\(\(\) => setInternalQa\(false\)\)/u);
  assert.match(app, /const \[internalQa, setInternalQa\] = useState\(false\)/u, 'off until the backend says otherwise');
  assert.match(app, /\{internalQa && <button role="menuitem"[^\n]*solver\.action\.view/u);
  assert.match(app, /\{appliedEntry\?\.solverRouteAvailable !== undefined && <button role="menuitem"/u);
  assert.match(app, /\{internalQa && solverTarget && <SolverRoutePanel/u);
  assert.match(read('src/RunHistoryPanel.tsx'), /\{entry\.solverRouteAvailable !== undefined && <button role="menuitem"/u);
  // Nothing hides the action by styling alone, and no setting turns it on.
  for (const file of ['src/App.tsx', 'src/RunHistoryPanel.tsx', 'src/styles.css']) assert.doesNotMatch(read(file), /display:\s*none[^}]*solver|solver[^{]*\{[^}]*display:\s*none/u);
  for (const file of ['src/App.tsx', 'src/RunHistoryPanel.tsx']) assert.doesNotMatch(read(file), /internalQa[^\n]*(checkbox|localStorage|setSetting)/u);
  const backend = readFileSync(path.join(desktop, '..', 'backend', 'src', 'server.ts'), 'utf8');
  assert.match(backend, /url\.pathname === '\/api\/capabilities'/u);
});

test('in an internal QA build the "Solver route" action is offered after generation and opens the panel for the generated world', () => {
  const app = read('src/App.tsx');
  assert.match(app, /import \{ SolverRoutePanel \} from '\.\/SolverRoutePanel';/u);
  // In the generated state: enabled as soon as the run has a history entry (the hint sheet's rule).
  assert.match(app, /disabled=\{!generation\.historyId\} onClick=\{\(\) => \{ setRunMenuOpen\(false\); if \(generation\.historyId\) setSolverTarget\(generation\.historyId\); \}\}>\{t\('solver\.action\.view'\)\}/u);
  // In the applied state and in the history it follows the backend's availability flag, which exists only in internal QA.
  assert.match(app, /disabled=\{!appliedEntry\.solverRouteAvailable\}[^\n]*setSolverTarget\(appliedEntry\.id\)[^\n]*solver\.action\.view/u);
  assert.match(app, /onSolverRoute=\{\(entry\) => setSolverTarget\(entry\.id\)\}/u);
  assert.match(app, /\{internalQa && solverTarget && <SolverRoutePanel entryId=\{solverTarget\} onClose=\{\(\) => setSolverTarget\(null\)\} \/>\}/u);
  const history = read('src/RunHistoryPanel.tsx');
  assert.match(history, /disabled=\{!entry\.solverRouteAvailable\}[^\n]*onSolverRoute\(entry\)[^\n]*solver\.action\.view/u);
});


test('the panel shows the context, the route text, the optional proof tree, and can copy the route', () => {
  const panel = read('src/SolverRoutePanel.tsx');
  for (const key of ['solver.viewer.seedCode', 'solver.viewer.profile', 'solver.viewer.fogSeed', 'solver.viewer.attempt', 'solver.viewer.claim', 'solver.viewer.copy', 'solver.viewer.showTree']) {
    assert.ok(panel.includes(`'${key}'`), `${key} is used`);
  }
  assert.match(panel, /api\.solverRoute\(entryId\)/u);
  assert.match(panel, /renderSolverSteps\(document\)/u);
  assert.match(panel, /navigator\.clipboard\.writeText\(renderSolverRoute\(document, \{ includeTree: showTree \}\)\)/u);
  assert.match(panel, /event\.key === 'Escape'/u, 'the dialog closes with Escape like the hint sheet');
  // The renderer stays out of the game files and the randomization: it only calls the typed API.
  assert.doesNotMatch(panel, /node:|child_process|require\(/u);
});

test('the typed boundary and the backend route exist, and generation keeps the witness next to the run', () => {
  assert.match(read('src/api.ts'), /solverRoute: \(id: string\) => post<.*'\/solver-route\/get'/u);
  const server = readFileSync(path.join(desktop, '..', 'backend', 'src', 'server.ts'), 'utf8');
  assert.match(server, /url\.pathname === '\/api\/solver-route\/get'/u);
  assert.match(server, /await persistSolverWitness\(workspacePath, \{/u);
  assert.match(server, /solverRouteAvailable: await solverRouteAvailableFor\(entry, runRoot\)/u);
});

test('both languages carry every text the panel and the menus use', () => {
  for (const language of ['en', 'pl']) {
    const solver = JSON.parse(read(`src/locales/${language}/solver.json`));
    for (const key of ['view', 'unavailable']) assert.equal(typeof solver.action[key], 'string', `${language} action.${key}`);
    for (const key of ['title', 'close', 'loading', 'unavailable', 'warning', 'intro', 'seedCode', 'profile', 'fogSeed', 'attempt', 'attemptValue', 'claim', 'claimValue', 'route', 'tree', 'showTree', 'copy', 'copied', 'copyFailed']) {
      assert.equal(typeof solver.viewer[key], 'string', `${language} viewer.${key}`);
    }
  }
});
