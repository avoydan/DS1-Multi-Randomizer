// Shared helpers of the release tooling (tools/release/*). Build-time only: nothing here ships in the application.
//
// Authenticode is read through Windows PowerShell's Get-AuthenticodeSignature (the one supported read-only API for it without extra
// tools). The script is written to a temp .ps1 and run with -File (an -EncodedCommand run returned no output when started from node), with the file list in a temp JSON file, so no path ever goes through a shell.

import { execFile } from 'node:child_process';
import { createHash } from 'node:crypto';
import { createReadStream } from 'node:fs';
import { mkdtemp, readFile, readdir, rm, stat, writeFile } from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

export const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..', '..');

export const sha256File = (file) => new Promise((resolve, reject) => {
  const hash = createHash('sha256');
  createReadStream(file).on('data', (chunk) => hash.update(chunk)).on('error', reject).on('end', () => resolve(hash.digest('hex')));
});

export const readJson = async (file) => JSON.parse((await readFile(file, 'utf8')).replace(/^﻿/u, ''));

/** Every file below a directory, as forward-slash paths relative to it. */
export async function listFiles(directory, prefix = '') {
  const result = [];
  for (const entry of await readdir(path.join(directory, prefix), { withFileTypes: true })) {
    const relative = prefix === '' ? entry.name : `${prefix}/${entry.name}`;
    if (entry.isDirectory()) result.push(...await listFiles(directory, relative));
    else result.push(relative);
  }
  return result;
}

const SCRIPT = `
$ErrorActionPreference = 'Stop'
$list = Get-Content -LiteralPath $env:DS1TR_RELEASE_LIST -Raw | ConvertFrom-Json
$out = foreach ($f in $list) {
  $s = Get-AuthenticodeSignature -LiteralPath $f
  [pscustomobject]@{
    file = $f
    status = [string]$s.Status
    statusMessage = [string]$s.StatusMessage
    signer = if ($s.SignerCertificate) { $s.SignerCertificate.Subject } else { $null }
    issuer = if ($s.SignerCertificate) { $s.SignerCertificate.Issuer } else { $null }
    thumbprint = if ($s.SignerCertificate) { $s.SignerCertificate.Thumbprint } else { $null }
    notAfter = if ($s.SignerCertificate) { $s.SignerCertificate.NotAfter.ToString('o') } else { $null }
    timestamp = if ($s.TimeStamperCertificate) { $s.TimeStamperCertificate.Subject } else { $null }
  }
}
ConvertTo-Json -InputObject @($out) -Compress -Depth 4
`;

async function runPowerShell(script, directory, list) {
  const file = path.join(directory, 'script.ps1');
  await writeFile(file, script, 'utf8');
  const { stdout } = await new Promise((resolve, reject) => {
    execFile('powershell.exe', ['-NoProfile', '-NonInteractive', '-ExecutionPolicy', 'Bypass', '-File', file],
      { windowsHide: true, maxBuffer: 16 * 1024 * 1024, env: { ...process.env, DS1TR_RELEASE_LIST: list } },
      (error, out, err) => (error ? reject(new Error(`${error.message} ${err}`)) : resolve({ stdout: out })));
  });
  return stdout;
}

/** Authenticode status of each file: { file, status, signer, issuer, thumbprint, notAfter, timestamp }. Status "NotSigned" means no signature. */
export async function readSignatures(files) {
  if (files.length === 0) return [];
  const directory = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-release-'));
  try {
    const list = path.join(directory, 'files.json');
    await writeFile(list, JSON.stringify(files));
    return JSON.parse(await runPowerShell(SCRIPT, directory, list));
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
}

/** The PE version resource of an executable (read with PowerShell's file version info): { product, description, company, copyright, fileVersion, productVersion }. */
export async function readVersionInfo(files) {
  if (files.length === 0) return [];
  const directory = await mkdtemp(path.join(os.tmpdir(), 'ds1tr-release-'));
  try {
    const list = path.join(directory, 'files.json');
    await writeFile(list, JSON.stringify(files));
    const script = `
$ErrorActionPreference = 'Stop'
$list = Get-Content -LiteralPath $env:DS1TR_RELEASE_LIST -Raw | ConvertFrom-Json
$out = foreach ($f in $list) { $v = (Get-Item -LiteralPath $f).VersionInfo
  [pscustomobject]@{ file = $f; product = $v.ProductName; description = $v.FileDescription; company = $v.CompanyName; copyright = $v.LegalCopyright; fileVersion = $v.FileVersion; productVersion = $v.ProductVersion; originalFilename = $v.OriginalFilename } }
ConvertTo-Json -InputObject @($out) -Compress -Depth 3`;
    return JSON.parse(await runPowerShell(script, directory, list));
  } finally {
    await rm(directory, { recursive: true, force: true });
  }
}

export const exists = (file) => stat(file).then(() => true, () => false);

/** Command line options: --flag, --name value, positional arguments. */
export function parseArguments(argv) {
  const flags = new Set();
  const values = new Map();
  const positional = [];
  const withValue = new Set(['mode', 'expected-publisher', 'manifest', 'out', 'unpacked', 'installer', 'portable', 'commit']);
  for (let index = 0; index < argv.length; index += 1) {
    const argument = argv[index];
    if (argument.startsWith('--')) {
      const name = argument.slice(2);
      if (withValue.has(name)) values.set(name, argv[++index]);
      else flags.add(name);
    } else positional.push(argument);
  }
  return { flags, values, positional };
}
