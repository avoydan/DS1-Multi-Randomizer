// Optional local release-health check: ask the installed Microsoft Defender to scan release artifacts, and report what it says.
//
//   node tools/release/defender-scan.mjs <file-or-folder> [...]
//
// It only calls the supported on-demand scanner (MpCmdRun.exe -Scan -ScanType 3 -File <path> -DisableRemediation).
// It never changes a Defender setting, never adds an exclusion, never disables protection, never deletes or quarantines a file
// (-DisableRemediation), and never uploads anything: the scan is local. If Defender is not the active antivirus of this machine or its
// service is off, it says so instead of inventing a result.

import { execFile } from 'node:child_process';
import { readdir, stat } from 'node:fs/promises';
import path from 'node:path';
import { exists, listFiles } from './release-lib.mjs';

async function findScanner() {
  const platformRoot = path.join(process.env.ProgramData ?? 'C:\\ProgramData', 'Microsoft', 'Windows Defender', 'Platform');
  const candidates = [];
  if (await exists(platformRoot)) for (const name of (await readdir(platformRoot)).sort().reverse()) candidates.push(path.join(platformRoot, name, 'MpCmdRun.exe'));
  candidates.push(path.join(process.env.ProgramFiles ?? 'C:\\Program Files', 'Windows Defender', 'MpCmdRun.exe'));
  for (const candidate of candidates) if (await exists(candidate)) return candidate;
  return null;
}

const run = (file, args, timeout) => new Promise((resolve) => {
  execFile(file, args, { windowsHide: true, timeout, maxBuffer: 8 * 1024 * 1024 }, (error, stdout, stderr) => resolve({ code: error ? (typeof error.code === 'number' ? error.code : -1) : 0, stdout: String(stdout), stderr: String(stderr), timedOut: Boolean(error?.killed) }));
});

async function defenderState() {
  const script = '$s = Get-MpComputerStatus; "$($s.AMServiceEnabled)|$($s.AntivirusEnabled)|$($s.RealTimeProtectionEnabled)|$($s.AntivirusSignatureVersion)|$($s.AMProductVersion)"';
  const { code, stdout } = await run('powershell.exe', ['-NoProfile', '-NonInteractive', '-Command', script], 20000);
  if (code !== 0) return null;
  const [service, antivirus, realtime, signatures, product] = stdout.trim().split('|');
  return { service: service === 'True', antivirus: antivirus === 'True', realtime: realtime === 'True', signatures, product };
}

const targets = process.argv.slice(2);
if (targets.length === 0) { console.error('Usage: node tools/release/defender-scan.mjs <file-or-folder> [...]'); process.exit(2); }
const scanner = await findScanner();
if (!scanner) { console.log('RESULT: NOT AVAILABLE - MpCmdRun.exe was not found on this machine; no local scan result.'); process.exit(0); }
const state = await defenderState();
console.log(`Scanner: ${scanner}`);
console.log(state ? `Defender: service ${state.service ? 'on' : 'OFF'}, antivirus ${state.antivirus ? 'active' : 'NOT the active antivirus'}, real-time ${state.realtime ? 'on' : 'off'}, signatures ${state.signatures || 'unknown'}, product ${state.product}` : 'Defender: its state could not be read');

const files = [];
for (const target of targets) {
  const full = path.resolve(target);
  if ((await stat(full)).isDirectory()) for (const relative of await listFiles(full)) { if (/\.(exe|dll|node|msi)$/iu.test(relative)) files.push(path.join(full, relative)); }
  else files.push(full);
}
let detections = 0;
let unscanned = 0;
for (const file of files) {
  const result = await run(scanner, ['-Scan', '-ScanType', '3', '-File', file, '-DisableRemediation'], 300000);
  const text = `${result.stdout}\n${result.stderr}`;
  const found = /found (\d+) threats?/iu.exec(text);
  const threatNames = [...text.matchAll(/Threat\s*:\s*(.+)/giu)].map((match) => match[1].trim());
  let verdict;
  // Exit 0: scanned, nothing found. A failure to scan ("[Failed]", "Failed with hr") also exits non-zero: that is NOT a detection.
  if (result.timedOut) { verdict = 'TIMED OUT'; unscanned += 1; }
  else if (result.code === 0) verdict = 'clean';
  else if (/\[Failed\]|Failed with hr|0x8[0-9a-f]{7}/iu.test(text)) { verdict = `NOT SCANNED (exit ${result.code}: ${text.replace(/\s+/gu, ' ').trim().slice(0, 140)})`; unscanned += 1; }
  else { verdict = `DETECTED ${threatNames.join(', ') || '(name not reported)'}`; detections += 1; }
  console.log(`${verdict.padEnd(14)} ${file}`);
}
if (detections > 0) console.log(`RESULT: ${detections} file(s) detected by the local scanner. Do not publish: preserve the artifact and its hash, check the signature and follow docs/RELEASE_TRUST.md ("If a scanner flags a release"). Nothing was removed.`);
else if (unscanned === files.length && files.length > 0) console.log('RESULT: NO RESULT - Defender did not scan any file (it is not active here or refused the request). This is not a clean bill of health.');
else console.log(`RESULT: ${files.length - unscanned} file(s) scanned, no detection${unscanned > 0 ? `, ${unscanned} not scanned` : ''}.`);
process.exitCode = detections > 0 ? 1 : 0;
