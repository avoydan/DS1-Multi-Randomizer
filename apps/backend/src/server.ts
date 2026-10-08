import http from 'node:http';
import { randomUUID } from 'node:crypto';
import { access, mkdir, readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { URL } from 'node:url';
import type { RandomizerConfig } from '@ds1tr/contracts';
import { validateInstallation } from './core/installation.js';
import { createPipelinePlan } from './core/pipeline.js';
import { generateRun, type GenerationRequest } from './core/generationPipeline.js';
import { InvalidConfigurationError, validateRandomizerConfig } from './core/configValidation.js';
import { exportPreset, importPreset } from './core/preset.js';
import { GameStateAuthority, defaultStateRoot } from './core/gameState.js';
import { describeEnemyPool } from './adapters/enemyPool.js';
import { discoverGameInstallations } from './core/gameDiscovery.js';
import { userMessageFor } from './core/userMessages.js';
import { prepareWorldData } from './adapters/worldData.js';
import { RunHistory } from './core/runHistory.js';
import { loadPersistedSettings, savePersistedSettings } from './core/settingsStore.js';
import { prepareRun, type RunRequestBody } from './core/seededRun.js';
import { decodeSeedCode } from './core/seedCode.js';
import { smokeTestWorlds } from './core/smokeTests.js';
import { supportedGenerationProfiles } from './core/generationProfiles.js';
import { defaultWorldDataCacheRoot } from './adapters/fogPrerequisites.js';
import { hintsAvailableFor, hintsForEntry, persistHints } from './core/hintStore.js';
import { AppLogger, LOG_STAGES, type LogLevel, type LogStage } from './core/appLog.js';
import { OperationTracker } from './core/operations.js';
import { createRedactor, redactionContext, redactJson } from './core/redaction.js';
import { assembleSupportBundle, gatherDiagnostics, supportRedaction, type SupportDependencies } from './core/support.js';
import { writeFile } from 'node:fs/promises';

const HOST = process.env.DS1TR_HOST ?? '127.0.0.1';
const PORT = Number(process.env.DS1TR_PORT ?? 43177);
const TOKEN = process.env.DS1TR_API_TOKEN;
// The single backup/restore authority is also the only trusted pristine source.
const authority = new GameStateAuthority({ stateRoot: defaultStateRoot() });
const generationDependencies = {
  getPristineSource: (gamePath: string) => authority.getPristineSource(gamePath),
};
const runRoot = path.join(defaultStateRoot(), 'runs');
const history = new RunHistory(defaultStateRoot());
// Support logging (docs/DIAGNOSTICS.md): structured lines with a stage and a correlation id, secrets removed as they are written.
const logger = new AppLogger(path.join(defaultStateRoot(), 'logs', 'backend.log'), { redact: createRedactor(redactionContext({ directories: [] })) });
const operations = new OperationTracker(defaultStateRoot(), logger);
/**
 * Where the spoiler log of a run is. A combined-only package holds it; a world randomizer package does not, so the log of the
 * item/enemy stage (the last combined attempt of the run folder) is used. Nothing is copied into the package.
 */
async function findSpoiler(workspacePath: string, packagePath: string): Promise<string | null> {
  const exists = (file: string) => access(file).then(() => true, () => false);
  const inPackage = path.join(packagePath, 'spoiler.txt');
  if (await exists(inPackage)) return inPackage;
  try {
    const attempts = (await readdir(workspacePath)).filter((name) => /^combined-\d+$/u.test(name)).sort((a, b) => Number(b.slice(9)) - Number(a.slice(9)));
    for (const attempt of attempts) {
      const seeds = (await readdir(path.join(workspacePath, attempt))).filter((name) => name.startsWith('seed_'));
      for (const seed of seeds) {
        const candidate = path.join(workspacePath, attempt, seed, 'spoiler.txt');
        if (await exists(candidate)) return candidate;
      }
    }
  } catch { /* no spoiler log */ }
  return null;
}

const APP_VERSION = '0.44.0';
const runDependencies = { ...generationDependencies, appVersion: APP_VERSION };
const supportDependencies: SupportDependencies = {
  stateRoot: defaultStateRoot(), runRoot, userDataDir: process.env.DS1TR_USER_DATA ?? null, appLogPath: process.env.DS1TR_APP_LOG ?? null,
  backendLogPath: logger.file, runtimeRoot: process.env.DS1TR_RUNTIME_ROOT ?? null, appVersion: APP_VERSION, env: process.env,
  listHistory: () => history.list(), detectState: (gamePath) => authority.detectState(gamePath),
};

function send(res: http.ServerResponse, status: number, body: unknown): void {
  res.writeHead(status, {
    'Content-Type': 'application/json; charset=utf-8',
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Headers': 'Content-Type, X-DS1TR-Token',
    'Access-Control-Allow-Methods': 'GET,POST,OPTIONS',
  });
  res.end(JSON.stringify(body));
}

async function readJson<T>(req: http.IncomingMessage): Promise<T> {
  const chunks: Buffer[] = [];
  for await (const chunk of req) chunks.push(Buffer.from(chunk));
  const raw = Buffer.concat(chunks).toString('utf8');
  return JSON.parse(raw || '{}') as T;
}

const server = http.createServer(async (req, res) => {
  // The packaged renderer has a null file origin; Vite uses its local dev origin.
  // Reject ordinary web pages before they can call game-mutating endpoints.
  const origin = req.headers.origin;
  if (origin && origin !== 'null' && origin !== 'http://127.0.0.1:5173') {
    return send(res, 403, { error: 'Origin not allowed' });
  }
  if (req.method === 'OPTIONS') return send(res, 204, null);
  if (TOKEN && req.headers['x-ds1tr-token'] !== TOKEN) return send(res, 403, { error: 'Invalid desktop session token.' });

  try {
    const url = new URL(req.url ?? '/', `http://${HOST}:${PORT}`);

    if (req.method === 'GET' && url.pathname === '/api/health') {
      return send(res, 200, { ok: true, service: 'ds1-triple-randomizer-backend', version: APP_VERSION });
    }

    if (req.method === 'POST' && url.pathname === '/api/installation/validate') {
      const body = await readJson<{ gamePath?: string }>(req);
      return send(res, 200, await validateInstallation(body.gamePath ?? ''));
    }

    if (req.method === 'POST' && url.pathname === '/api/game/detect') {
      return send(res, 200, { installations: await discoverGameInstallations() });
    }

    if (req.method === 'POST' && url.pathname === '/api/world-data/prepare') {
      const body = await readJson<{ gamePath?: string }>(req);
      const pristine = body.gamePath ? await authority.getPristineSource(body.gamePath) : null;
      if (!pristine) {
        return send(res, 409, { ok: false, code: 'PRISTINE_IMPORT_REQUIRED', userMessage: 'Dark Souls Remastered has not been checked yet.', technical: 'No verified pristine snapshot for this installation.' });
      }
      const result = await prepareWorldData({ cacheRoot: defaultWorldDataCacheRoot(), pristineDirectory: pristine.directory });
      return send(res, result.ok ? 200 : 409, result.ok ? { ok: true } : result);
    }

    if (req.method === 'POST' && url.pathname === '/api/pipeline/plan') {
      const config = await readJson<RandomizerConfig>(req);
      return send(res, 200, await createPipelinePlan(config));
    }

    if (req.method === 'POST' && url.pathname === '/api/enemy-pool') {
      const body = await readJson<{ gamePath?: string }>(req);
      const pristine = body.gamePath ? await authority.getPristineSource(body.gamePath) : null;
      const result = await describeEnemyPool(pristine);
      return send(res, result.ok ? 200 : 409, result);
    }

    if (req.method === 'POST' && url.pathname === '/api/preset/export') {
      const config = await readJson<RandomizerConfig>(req);
      validateRandomizerConfig(config);
      return send(res, 200, exportPreset(config));
    }

    if (req.method === 'POST' && url.pathname === '/api/preset/import') {
      const body = await readJson<{ text?: string }>(req);
      const result = importPreset(typeof body.text === 'string' ? body.text : '');
      return send(res, result.ok ? 200 : 400, result);
    }

    if (req.method === 'POST' && url.pathname === '/api/game/state') {
      const body = await readJson<{ gamePath?: string }>(req);
      return send(res, 200, await authority.detectState(body.gamePath ?? ''));
    }

    if (req.method === 'POST' && url.pathname === '/api/game/import') {
      const body = await readJson<{ gamePath?: string }>(req);
      const result = await authority.importPristine(body.gamePath ?? '');
      return send(res, result.ok ? 200 : 409, result);
    }

    if (req.method === 'POST' && url.pathname === '/api/game/activate') {
      const body = await readJson<{ gamePath?: string; packagePath?: string }>(req);
      if (!body.gamePath || !body.packagePath) return send(res, 400, { error: 'Game and package paths are required.' });
      const operation = operations.begin('apply');
      const result = await authority.activate(body.gamePath, body.packagePath);
      if (result.ok) await history.markApplied(body.packagePath).catch(() => undefined);
      if (result.ok) operation.finish({ ok: true });
      else { operation.note('error', 'APPLY', `${result.code}: ${result.message}`, result.code); operation.finish({ ok: false, code: result.code }); }
      return send(res, result.ok ? 200 : 409, { ...result, correlationId: operation.correlationId });
    }

    if (req.method === 'POST' && url.pathname === '/api/game/restore') {
      const body = await readJson<{ gamePath?: string }>(req);
      if (!body.gamePath) return send(res, 400, { error: 'Game path is required.' });
      const operation = operations.begin('restore');
      const result = await authority.restore(body.gamePath);
      if (result.ok) await history.markRestored().catch(() => undefined);
      if (result.ok) operation.finish({ ok: true });
      else { operation.note('error', 'RESTORE', `${result.code}: ${result.message}`, result.code); operation.finish({ ok: false, code: result.code }); }
      return send(res, result.ok ? 200 : 409, { ...result, correlationId: operation.correlationId });
    }

    if (req.method === 'POST' && url.pathname === '/api/seed/decode') {
      const body = await readJson<{ seedCode?: string }>(req);
      const decoded = decodeSeedCode(typeof body.seedCode === 'string' ? body.seedCode : '');
      return send(res, decoded.ok ? 200 : 400, decoded.ok
        ? { ok: true, seedCode: decoded.seedCode, profile: decoded.envelope.profile, masterSeed: decoded.envelope.masterSeed, settings: decoded.envelope.settings }
        : { ok: false, code: decoded.code, message: decoded.message, technical: decoded.technical });
    }

    if (req.method === 'POST' && url.pathname === '/api/smoke/list') {
      return send(res, 200, smokeTestWorlds());
    }

    if (req.method === 'POST' && url.pathname === '/api/history/list') {
      const entries = await history.list();
      return send(res, 200, { entries: await Promise.all(entries.map(async (entry) => ({ ...entry, hintsAvailable: await hintsAvailableFor(entry, runRoot).catch(() => false) }))), supportedProfiles: supportedGenerationProfiles() });
    }

    if (req.method === 'POST' && url.pathname === '/api/history/delete') {
      const body = await readJson<{ id?: string }>(req);
      return send(res, 200, { ok: typeof body.id === 'string' && await history.remove(body.id) });
    }

    if (req.method === 'POST' && url.pathname === '/api/history/spoiler') {
      const body = await readJson<{ id?: string }>(req);
      const entry = typeof body.id === 'string' ? await history.get(body.id) : null;
      if (!entry?.spoilerPath) return send(res, 200, { ok: false, code: 'SPOILER_NONE', message: 'This world has no spoiler log.' });
      try {
        return send(res, 200, { ok: true, text: await readFile(entry.spoilerPath, 'utf8') });
      } catch {
        return send(res, 200, { ok: false, code: 'SPOILER_UNAVAILABLE', message: 'The spoiler log is no longer available. Use Regenerate files to create it again.' });
      }
    }

    if (req.method === 'POST' && url.pathname === '/api/hints/get') {
      const body = await readJson<{ id?: string }>(req);
      const entry = typeof body.id === 'string' ? await history.get(body.id) : null;
      const document = entry ? await hintsForEntry(entry, runRoot, APP_VERSION).catch(() => null) : null;
      return send(res, 200, document ? { ok: true, document } : { ok: false, code: 'HINTS_UNAVAILABLE' });
    }

    if (req.method === 'POST' && url.pathname === '/api/diagnostics') {
      const body = await readJson<{ gamePath?: string; language?: string; worldId?: string }>(req);
      const gamePath = typeof body.gamePath === 'string' ? body.gamePath : undefined;
      const diagnostics = await gatherDiagnostics(supportDependencies, { gamePath, language: body.language, worldId: typeof body.worldId === 'string' ? body.worldId : undefined });
      // Everything the interface shows or copies has been through the same redaction as the support bundle.
      return send(res, 200, { ok: true, diagnostics: redactJson(diagnostics, createRedactor(supportRedaction(supportDependencies, gamePath))) });
    }

    if (req.method === 'POST' && url.pathname === '/api/support/bundle') {
      const body = await readJson<{ gamePath?: string; language?: string; worldId?: string; summaryText?: unknown; readmeText?: unknown }>(req);
      const bundle = await assembleSupportBundle(supportDependencies, {
        gamePath: typeof body.gamePath === 'string' ? body.gamePath : undefined, language: body.language, worldId: typeof body.worldId === 'string' ? body.worldId : undefined,
        summaryText: typeof body.summaryText === 'string' ? body.summaryText : '', readmeText: typeof body.readmeText === 'string' ? body.readmeText : '',
      });
      if (!bundle.ok) {
        logger.error('APP', `support bundle refused: ${bundle.code}`, { code: bundle.code });
        return send(res, 200, { ok: false, code: bundle.code });
      }
      logger.info('APP', `support bundle made: ${bundle.report.entries.length} files, ${bundle.report.totalBytes} bytes, ${bundle.report.redactions} redactions`);
      return send(res, 200, { ok: true, fileName: bundle.fileName, contentBase64: bundle.archive.toString('base64'), report: bundle.report });
    }

    // The interface reports its own problems here so they carry the same correlation id as the backend's lines.
    if (req.method === 'POST' && url.pathname === '/api/log') {
      const body = await readJson<{ level?: string; stage?: string; message?: unknown; correlationId?: unknown; code?: unknown }>(req);
      const level: LogLevel = body.level === 'error' || body.level === 'warning' || body.level === 'debug' ? body.level : 'info';
      const stage: LogStage = (LOG_STAGES as readonly string[]).includes(body.stage ?? '') ? body.stage as LogStage : 'APP';
      logger.log(level, stage, typeof body.message === 'string' ? body.message.slice(0, 2000) : '', {
        source: 'renderer', ...(typeof body.correlationId === 'string' ? { correlationId: body.correlationId.slice(0, 64) } : {}), ...(typeof body.code === 'string' ? { code: body.code.slice(0, 64) } : {}),
      });
      return send(res, 200, { ok: true });
    }

    if (req.method === 'POST' && url.pathname === '/api/settings/load') {
      const loaded = await loadPersistedSettings(defaultStateRoot());
      return send(res, 200, loaded);
    }

    if (req.method === 'POST' && url.pathname === '/api/settings/save') {
      const body = await readJson<{ settings?: unknown; generateSpoilerLog?: boolean }>(req);
      try {
        await savePersistedSettings(defaultStateRoot(), { settings: body.settings as never, generateSpoilerLog: body.generateSpoilerLog === true });
      } catch {
        return send(res, 400, { ok: false });
      }
      return send(res, 200, { ok: true });
    }

    if (req.method === 'POST' && url.pathname === '/api/runs/generate') {
      const body = await readJson<RunRequestBody>(req);
      const prepared = prepareRun(body);
      res.writeHead(200, {
        'Content-Type': 'text/event-stream; charset=utf-8',
        'Cache-Control': 'no-cache',
        'Access-Control-Allow-Origin': 'null',
      });
      if (!prepared.ok) {
        const refused = operations.begin('generate');
        refused.note('warning', 'SEED', `the request was refused: ${prepared.code}`, prepared.code);
        refused.finish({ ok: false, code: prepared.code, stage: 'seed' });
        res.write(`event: result\ndata: ${JSON.stringify({ ok: false, code: prepared.code, message: prepared.message, stage: 'seed', technical: prepared.technical, correlationId: refused.correlationId })}\n\n`);
        return res.end();
      }
      const workspacePath = path.join(runRoot, randomUUID());
      await mkdir(workspacePath, { recursive: true });
      const operation = operations.begin('generate', {
        seedCode: prepared.seedCode, profile: prepared.profile.id, workspacePath,
        plan: { items: prepared.config.items.enabled, enemies: prepared.config.enemies.enabled },
      });
      try {
        const result = await generateRun({ config: prepared.config, workspacePath, profile: prepared.profile, seedCode: prepared.seedCode }, (event) => {
          operation.event(event);
          res.write(`event: ${event.type}\ndata: ${JSON.stringify(event)}\n\n`);
        }, runDependencies);
        if (result.ok) {
          const spoilerPath = await findSpoiler(workspacePath, result.packagePath);
          // The hint data is built once, from the placements the generator just wrote, and kept next to the run (never in the package).
          const hintsPath = await persistHints(workspacePath, {
            seedCode: prepared.seedCode, profile: prepared.profile.id, createdAt: new Date().toISOString(), appVersion: APP_VERSION, settings: prepared.envelope.settings,
          }).catch(() => null);
          // The same world generated again (Load seed, Regenerate files) refreshes its entry; it is not a new run.
          const entry = await history.addOrRefresh({
            seedCode: prepared.seedCode, profile: prepared.profile.id, masterSeed: prepared.config.masterSeed, settings: prepared.envelope.settings,
            packagePath: result.packagePath, manifestPath: result.manifestPath, spoilerPath, hintsPath,
          }).catch(() => null);
          operation.set({ historyId: entry?.id ?? null, packagePath: result.packagePath });
          operation.finish({ ok: true });
          res.write(`event: result\ndata: ${JSON.stringify({ ...result, seedCode: prepared.seedCode, profile: prepared.profile.id, masterSeed: prepared.config.masterSeed, historyId: entry?.id ?? null, settings: prepared.envelope.settings, correlationId: operation.correlationId })}\n\n`);
        } else {
          // The manifest of a failed attempt (attempts, retry reasons, validation notes) is kept next to the run so a support bundle can carry it.
          await writeFile(path.join(workspacePath, 'failed-manifest.json'), JSON.stringify(result.manifest, null, 2)).catch(() => undefined);
          operation.finish({ ok: false, code: result.code, stage: result.stage });
          res.write(`event: result\ndata: ${JSON.stringify({ ...result, correlationId: operation.correlationId })}\n\n`);
        }
      } catch (error) {
        operation.note('error', 'SERVER', `unexpected failure: ${String(error)}`, 'PIPELINE_FAILED');
        operation.finish({ ok: false, code: 'PIPELINE_FAILED', stage: 'generation' });
        res.write(`event: result\ndata: ${JSON.stringify({ ok: false, code: 'PIPELINE_FAILED', message: userMessageFor('PIPELINE_FAILED', String(error)), stage: 'generation', correlationId: operation.correlationId })}\n\n`);
      }
      return res.end();
    }

    if (req.method === 'POST' && url.pathname === '/api/randomize') {
      const request = await readJson<GenerationRequest>(req);
      validateRandomizerConfig(request.config);
      if (req.headers.accept?.includes('text/event-stream')) {
        res.writeHead(200, {
          'Content-Type': 'text/event-stream; charset=utf-8',
          'Cache-Control': 'no-cache',
          'Access-Control-Allow-Origin': 'null',
        });
        let result;
        try {
          result = await generateRun(request, (event) => {
            res.write(`event: ${event.type}\ndata: ${JSON.stringify(event)}\n\n`);
          }, generationDependencies);
        } catch (error) {
          result = { ok: false, code: 'PIPELINE_FAILED',
            message: error instanceof Error ? error.message : String(error) };
        }
        res.write(`event: result\ndata: ${JSON.stringify(result)}\n\n`);
        return res.end();
      }
      const result = await generateRun(request, undefined, generationDependencies);
      return send(res, result.ok ? 200 : 409, result);
    }

    return send(res, 404, { error: 'Not found' });
  } catch (error) {
    if (error instanceof InvalidConfigurationError) {
      return send(res, 400, { error: 'Invalid configuration', problems: error.problems });
    }
    return send(res, 500, { error: error instanceof Error ? error.message : String(error) });
  }
});

server.listen(PORT, HOST, () => {
  const address = server.address();
  console.log(`[backend] http://${HOST}:${typeof address === 'object' && address ? address.port : PORT}`);
});
process.on('message', (message) => {
  if (message === 'shutdown') server.close(() => process.exit(0));
});
