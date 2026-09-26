import http from 'node:http';
import { URL } from 'node:url';
import type { RandomizerConfig } from '@ds1tr/contracts';
import { validateInstallation } from './core/installation.js';
import { createPipelinePlan } from './core/pipeline.js';

const HOST = process.env.DS1TR_HOST ?? '127.0.0.1';
const PORT = Number(process.env.DS1TR_PORT ?? 43177);

function send(res: http.ServerResponse, status: number, body: unknown): void {
  res.writeHead(status, {
    'Content-Type': 'application/json; charset=utf-8',
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Headers': 'Content-Type',
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
  if (req.method === 'OPTIONS') return send(res, 204, null);

  try {
    const url = new URL(req.url ?? '/', `http://${HOST}:${PORT}`);

    if (req.method === 'GET' && url.pathname === '/api/health') {
      return send(res, 200, { ok: true, service: 'ds1-triple-randomizer-backend', version: '0.1.0' });
    }

    if (req.method === 'POST' && url.pathname === '/api/installation/validate') {
      const body = await readJson<{ gamePath?: string }>(req);
      return send(res, 200, await validateInstallation(body.gamePath ?? ''));
    }

    if (req.method === 'POST' && url.pathname === '/api/pipeline/plan') {
      const config = await readJson<RandomizerConfig>(req);
      return send(res, 200, await createPipelinePlan(config));
    }

    if (req.method === 'POST' && url.pathname === '/api/randomize') {
      return send(res, 501, {
        ok: false,
        code: 'ENGINES_NOT_WIRED',
        message: 'Pipeline shell is ready. Upstream randomizer adapters are the next implementation step.',
      });
    }

    return send(res, 404, { error: 'Not found' });
  } catch (error) {
    return send(res, 500, { error: error instanceof Error ? error.message : String(error) });
  }
});

server.listen(PORT, HOST, () => {
  console.log(`[backend] http://${HOST}:${PORT}`);
});
