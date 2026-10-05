import http from 'node:http';
import { randomUUID } from 'node:crypto';
import { mkdir } from 'node:fs/promises';
import path from 'node:path';
import { URL } from 'node:url';
import type { RandomizerConfig } from '@ds1tr/contracts';
import { validateInstallation } from './core/installation.js';
import { createPipelinePlan } from './core/pipeline.js';
import { generateRun, type GenerationRequest } from './core/generationPipeline.js';
import { InvalidConfigurationError, validateRandomizerConfig } from './core/configValidation.js';
import { exportPreset, importPreset } from './core/preset.js';
import { GameStateAuthority, defaultStateRoot } from './core/gameState.js';

const HOST = process.env.DS1TR_HOST ?? '127.0.0.1';
const PORT = Number(process.env.DS1TR_PORT ?? 43177);
const TOKEN = process.env.DS1TR_API_TOKEN;
// The single backup/restore authority is also the only trusted pristine source.
const authority = new GameStateAuthority({ stateRoot: defaultStateRoot() });
const generationDependencies = {
  getPristineSource: (gamePath: string) => authority.getPristineSource(gamePath),
};
const runRoot = path.join(defaultStateRoot(), 'runs');

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
      return send(res, 200, { ok: true, service: 'ds1-triple-randomizer-backend', version: '0.24.0' });
    }

    if (req.method === 'POST' && url.pathname === '/api/installation/validate') {
      const body = await readJson<{ gamePath?: string }>(req);
      return send(res, 200, await validateInstallation(body.gamePath ?? ''));
    }

    if (req.method === 'POST' && url.pathname === '/api/pipeline/plan') {
      const config = await readJson<RandomizerConfig>(req);
      return send(res, 200, await createPipelinePlan(config));
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
      const result = await authority.activate(body.gamePath, body.packagePath);
      return send(res, result.ok ? 200 : 409, result);
    }

    if (req.method === 'POST' && url.pathname === '/api/game/restore') {
      const body = await readJson<{ gamePath?: string }>(req);
      if (!body.gamePath) return send(res, 400, { error: 'Game path is required.' });
      const result = await authority.restore(body.gamePath);
      return send(res, result.ok ? 200 : 409, result);
    }

    if (req.method === 'POST' && url.pathname === '/api/runs/generate') {
      const config = await readJson<RandomizerConfig>(req);
      validateRandomizerConfig(config);
      const workspacePath = path.join(runRoot, randomUUID());
      await mkdir(workspacePath, { recursive: true });
      res.writeHead(200, {
        'Content-Type': 'text/event-stream; charset=utf-8',
        'Cache-Control': 'no-cache',
        'Access-Control-Allow-Origin': 'null',
      });
      try {
        const result = await generateRun({ config, workspacePath }, (event) => {
          res.write(`event: ${event.type}\ndata: ${JSON.stringify(event)}\n\n`);
        }, generationDependencies);
        res.write(`event: result\ndata: ${JSON.stringify(result)}\n\n`);
      } catch (error) {
        res.write(`event: result\ndata: ${JSON.stringify({ ok: false, code: 'PIPELINE_FAILED', message: String(error), stage: 'generation' })}\n\n`);
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
