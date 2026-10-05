// World-randomizer data ("Fog data root") that DS1TR prepares itself. Nothing here is chosen by the user.
//
// FogHost reads exactly 59 files from a data root (tools/fog-host/src/DataRoot.cs is the authority and verifies every
// one of them on every run). Where they come from, established by comparing them with a clean Dark Souls Remastered
// installation (docs/FOG_DATA_PROVENANCE.md):
//   - 'game': 18 map files (.msb) and GameParam are byte-identical to the clean installation. They are copied from the
//     verified pristine snapshot, never downloaded, and a different game build is reported as unsupported.
//   - 'upstream': fog.txt, 4 layouts, 18 event files and 17 talk files exist only in FogMod's repository at the pinned
//     commit (the event and talk files are FromSoftware data edited by the FogMod author and differ from the clean
//     installation). DS1TR does not bundle or host them: the app fetches them once, over HTTPS, from the upstream
//     repository at the pinned commit, verifies each SHA-256 and keeps them in its own cache.
// Only static data is ever fetched; nothing downloaded is executed.

import { createHash, randomBytes } from 'node:crypto';
import { copyFile, mkdir, readFile, readdir, rename, rm, stat, writeFile } from 'node:fs/promises';
import path from 'node:path';

export const worldDataSourceCommit = '6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f';
export const worldDataBaseUrl = `https://raw.githubusercontent.com/thefifthmatt/FogMod/${worldDataSourceCommit}/`;

export type WorldDataKind = 'fog-annotations' | 'layout' | 'event' | 'talk' | 'map' | 'param';

export interface WorldDataEntry {
  /** Path inside the data root, as FogHost expects it. */
  path: string;
  /** SHA-256 of the content; text files are hashed after CRLF -> LF normalization (as FogHost does). */
  sha256: string;
  text: boolean;
  /** Size of the file as the upstream repository serves it (used as a download bound). */
  size: number;
  kind: WorldDataKind;
  origin: 'game' | 'upstream';
  /** For origin 'game': the path inside the clean installation. */
  gamePath?: string;
}

export const worldDataManifest: readonly WorldDataEntry[] = [
  { path: 'dist/DS1R/Layouts/GAME_AREA_PARAM_ST.xml', sha256: 'c05a0beb92ee9b49ba3bd80270681014d9ad2e0b055dddff9aff94f89e78f75a', text: true, size: 1471, kind: 'layout', origin: 'upstream' },
  { path: 'dist/DS1R/Layouts/ITEMLOT_PARAM_ST.xml', sha256: 'e3f0f03d8ac3cf9fd3d4ce2a49d71332aaec6e2ac8f792dd485f5a5bf097fd66', text: true, size: 7270, kind: 'layout', origin: 'upstream' },
  { path: 'dist/DS1R/Layouts/NPC_PARAM_ST.xml', sha256: '69e7cc19e829f7132c5b91c9600a1a4ef4800071032286345673b496120c26a1', text: true, size: 16449, kind: 'layout', origin: 'upstream' },
  { path: 'dist/DS1R/Layouts/SP_EFFECT_PARAM_ST.xml', sha256: 'ad96136a44f1135b469ac48d30469efcd9ad1ea3e0daf1eda32212980e2b10df', text: true, size: 18587, kind: 'layout', origin: 'upstream' },
  { path: 'dist/DS1R/event/common.emevd.dcx', sha256: '2018562b4cc5c4ebb1de818ea7da736093e66c88848acd983ae6306b79bf71c0', text: false, size: 8594, kind: 'event', origin: 'upstream' },
  { path: 'dist/DS1R/event/m10_00_00_00.emevd.dcx', sha256: '6494c24b1149c6423fa085dc2b3a5cb08da4903ed40fbc4b4fa7aa4d8b91274b', text: false, size: 4949, kind: 'event', origin: 'upstream' },
  { path: 'dist/DS1R/event/m10_01_00_00.emevd.dcx', sha256: '96c3ddf96e35cad5c77d5d035de9e04929351fc87515357d0dc63578bae235fb', text: false, size: 13489, kind: 'event', origin: 'upstream' },
  { path: 'dist/DS1R/event/m10_02_00_00.emevd.dcx', sha256: '0be8539a72302a35af0649350978918f1d332fc9b568b48868f206863b59428d', text: false, size: 8752, kind: 'event', origin: 'upstream' },
  { path: 'dist/DS1R/event/m11_00_00_00.emevd.dcx', sha256: '60caf4122637037a7b3e2d7cdc1bf1792a80efd61f19356a002ff27276fcc7d2', text: false, size: 8087, kind: 'event', origin: 'upstream' },
  { path: 'dist/DS1R/event/m12_00_00_00.emevd.dcx', sha256: '8822ee9b1f7015d4bf21754cea9b0f46198ffcb1e7b812cfec9c7126c73a9ee4', text: false, size: 8597, kind: 'event', origin: 'upstream' },
  { path: 'dist/DS1R/event/m12_01_00_00.emevd.dcx', sha256: 'c17551fdf12c028b4d4d4f9220aca98df428431ba30842260f25df6542eb04ea', text: false, size: 24838, kind: 'event', origin: 'upstream' },
  { path: 'dist/DS1R/event/m13_00_00_00.emevd.dcx', sha256: '9e65ecd1824111a97f341eccbd47045b8c0937ac7a187c57567a77d493316d80', text: false, size: 8251, kind: 'event', origin: 'upstream' },
  { path: 'dist/DS1R/event/m13_01_00_00.emevd.dcx', sha256: '10043a7b990e97443d744c7753514cf516aba0b7e92af4551e5e328660203cd4', text: false, size: 6668, kind: 'event', origin: 'upstream' },
  { path: 'dist/DS1R/event/m13_02_00_00.emevd.dcx', sha256: '706dc53f705acc92db2986d3a2a96f2138f66b816b60b96f91960f110f7b42fc', text: false, size: 2914, kind: 'event', origin: 'upstream' },
  { path: 'dist/DS1R/event/m14_00_00_00.emevd.dcx', sha256: 'bd9fb9592d3787d2cb58d7336f0a9d37202131d47b8c8cd86a542a4c807619ea', text: false, size: 7763, kind: 'event', origin: 'upstream' },
  { path: 'dist/DS1R/event/m14_01_00_00.emevd.dcx', sha256: '26fa087386ae61522cd5262bcd5e44a2cbc4c914e7ac5b915d84b70b1f7848f7', text: false, size: 12515, kind: 'event', origin: 'upstream' },
  { path: 'dist/DS1R/event/m15_00_00_00.emevd.dcx', sha256: 'c1b8caf046c0cdb35cdaf053bf0f6534f84cfce6f20ad88f4008407b7c35cca9', text: false, size: 10104, kind: 'event', origin: 'upstream' },
  { path: 'dist/DS1R/event/m15_01_00_00.emevd.dcx', sha256: '30690a5adf81838707dcf92e016848548fa42c9745c0d0e275c3371919ef30b2', text: false, size: 12879, kind: 'event', origin: 'upstream' },
  { path: 'dist/DS1R/event/m16_00_00_00.emevd.dcx', sha256: 'e609feedae9a4edc9d9751f27fe1f35c240911c0aecb5e25a0a49ead7274ea3f', text: false, size: 8986, kind: 'event', origin: 'upstream' },
  { path: 'dist/DS1R/event/m17_00_00_00.emevd.dcx', sha256: '8e1d3ae997471dbace1a361bf6fdba0226d9dc17b1f0a5c2f59119c4ca14cbc4', text: false, size: 9390, kind: 'event', origin: 'upstream' },
  { path: 'dist/DS1R/event/m18_00_00_00.emevd.dcx', sha256: 'f210406c69fff8633f188707558f555386561968f41c23c8c1b083d147e4c63c', text: false, size: 4289, kind: 'event', origin: 'upstream' },
  { path: 'dist/DS1R/event/m18_01_00_00.emevd.dcx', sha256: '8382905c412f9f366470fe91cc6dad528a512703ddb37f435bbe89adedc41229', text: false, size: 5644, kind: 'event', origin: 'upstream' },
  { path: 'dist/DS1R/map/MapStudio/m10_00_00_00.msb', sha256: 'bda3f68445f13730b5bcd950c5413d747a9cf8dbb999d2c11c72f5ba5b8a8ac0', text: false, size: 177300, kind: 'map', origin: 'game', gamePath: 'map/MapStudio/m10_00_00_00.msb' },
  { path: 'dist/DS1R/map/MapStudio/m10_01_00_00.msb', sha256: 'd33134ff30f3811d9052d1270731eb843d7d2f333f7d968c895cc1dffed4f1d5', text: false, size: 361908, kind: 'map', origin: 'game', gamePath: 'map/MapStudio/m10_01_00_00.msb' },
  { path: 'dist/DS1R/map/MapStudio/m10_02_00_00.msb', sha256: '0d1656fe2c8a4bdf37232565abbc936b86b3cbabdb898de299a4576587bac086', text: false, size: 89416, kind: 'map', origin: 'game', gamePath: 'map/MapStudio/m10_02_00_00.msb' },
  { path: 'dist/DS1R/map/MapStudio/m11_00_00_00.msb', sha256: 'd0fd37f91d82f26413d7dec29cc2e66638c52e0502c1373581bc5d13db0b01f0', text: false, size: 179744, kind: 'map', origin: 'game', gamePath: 'map/MapStudio/m11_00_00_00.msb' },
  { path: 'dist/DS1R/map/MapStudio/m12_00_00_00.msb', sha256: 'eeb088800c225df4a6a313002d85bb41b626e29a9dc42257027ab3373c751e00', text: false, size: 207936, kind: 'map', origin: 'game', gamePath: 'map/MapStudio/m12_00_00_00.msb' },
  { path: 'dist/DS1R/map/MapStudio/m12_00_00_01.msb', sha256: '5680674234f89c7899f4ec16df8acd24fca51d20ddf98fd7a25bc721b67a3fc9', text: false, size: 210180, kind: 'map', origin: 'game', gamePath: 'map/MapStudio/m12_00_00_01.msb' },
  { path: 'dist/DS1R/map/MapStudio/m12_01_00_00.msb', sha256: 'e68be5d13ee27ecf42e30f50b649a98a4bc4fcba75c0207cb34d37c774a605e1', text: false, size: 405920, kind: 'map', origin: 'game', gamePath: 'map/MapStudio/m12_01_00_00.msb' },
  { path: 'dist/DS1R/map/MapStudio/m13_00_00_00.msb', sha256: '9bff6063d0e7ce80f8a316d49dc737a7d1d27328ced29c105d7cf32d9b737585', text: false, size: 230556, kind: 'map', origin: 'game', gamePath: 'map/MapStudio/m13_00_00_00.msb' },
  { path: 'dist/DS1R/map/MapStudio/m13_01_00_00.msb', sha256: '5d5a2956d467a98f10b296a0b04c8d30bbfd3e3794a9a106c25381d82cd19394', text: false, size: 157608, kind: 'map', origin: 'game', gamePath: 'map/MapStudio/m13_01_00_00.msb' },
  { path: 'dist/DS1R/map/MapStudio/m13_02_00_00.msb', sha256: 'de8f234dd5af3e022e8508c772011fd44e8d36e0d1d0dad042adf8bab09dc779', text: false, size: 169104, kind: 'map', origin: 'game', gamePath: 'map/MapStudio/m13_02_00_00.msb' },
  { path: 'dist/DS1R/map/MapStudio/m14_00_00_00.msb', sha256: '4ecb1a8329d66d21f726c12617f63f0a3b2a62279da251b200eaca664a16e4e9', text: false, size: 323292, kind: 'map', origin: 'game', gamePath: 'map/MapStudio/m14_00_00_00.msb' },
  { path: 'dist/DS1R/map/MapStudio/m14_01_00_00.msb', sha256: 'eb3262dce99a28667c487dea46b3bb0e97fa884cf7544901ab1984bc9cddd86f', text: false, size: 288032, kind: 'map', origin: 'game', gamePath: 'map/MapStudio/m14_01_00_00.msb' },
  { path: 'dist/DS1R/map/MapStudio/m15_00_00_00.msb', sha256: '1828f5540dbd3cb91e0a2b1143e2a7b83ed7764554f6e4cf70e2028ad5a45419', text: false, size: 136240, kind: 'map', origin: 'game', gamePath: 'map/MapStudio/m15_00_00_00.msb' },
  { path: 'dist/DS1R/map/MapStudio/m15_01_00_00.msb', sha256: 'a077593fda6c8d73b7220366f23e23716560f1d1fbd03b09bdc97be11d271367', text: false, size: 180740, kind: 'map', origin: 'game', gamePath: 'map/MapStudio/m15_01_00_00.msb' },
  { path: 'dist/DS1R/map/MapStudio/m16_00_00_00.msb', sha256: 'b6c61a5e90882d450d8dea85f49c90f926b2b893df05c993c25c1908e457e01a', text: false, size: 138492, kind: 'map', origin: 'game', gamePath: 'map/MapStudio/m16_00_00_00.msb' },
  { path: 'dist/DS1R/map/MapStudio/m17_00_00_00.msb', sha256: 'b8eed7579c30abcaf731c4c470a79f69c8449ac266dd5c3875d178168fa22fc0', text: false, size: 228768, kind: 'map', origin: 'game', gamePath: 'map/MapStudio/m17_00_00_00.msb' },
  { path: 'dist/DS1R/map/MapStudio/m18_00_00_00.msb', sha256: 'ddb52ac2638e47ef963917080844405adcbe93986becc7cee19fa2086150c6af', text: false, size: 54296, kind: 'map', origin: 'game', gamePath: 'map/MapStudio/m18_00_00_00.msb' },
  { path: 'dist/DS1R/map/MapStudio/m18_01_00_00.msb', sha256: 'd9051999ea31aaffe77accad352985cc7629b791c115d28266ea20c8b36aa56e', text: false, size: 109036, kind: 'map', origin: 'game', gamePath: 'map/MapStudio/m18_01_00_00.msb' },
  { path: 'dist/DS1R/param/GameParam/GameParam.parambnd.dcx', sha256: 'cc4a81cc87f028534d965c908a8aa7dba2f9e235ac30f5e72ddf6bb45dc7b5ab', text: false, size: 498505, kind: 'param', origin: 'game', gamePath: 'param/GameParam/GameParam.parambnd.dcx' },
  { path: 'dist/DS1R/script/talk/m10_00_00_00.talkesdbnd.dcx', sha256: '439b9c8179908581b45495a43bd1b57c5319b848cdb699c29fa27fae815c0bcc', text: false, size: 12075, kind: 'talk', origin: 'upstream' },
  { path: 'dist/DS1R/script/talk/m10_01_00_00.talkesdbnd.dcx', sha256: '3bf132fd86644f6457e24a10bd641db790938dfed47952c8228c37ac5f4c270f', text: false, size: 33859, kind: 'talk', origin: 'upstream' },
  { path: 'dist/DS1R/script/talk/m10_02_00_00.talkesdbnd.dcx', sha256: '067c6dd0f337f140012ee466a830373d7eaff24cbd40c774e82554cf43f8d968', text: false, size: 64520, kind: 'talk', origin: 'upstream' },
  { path: 'dist/DS1R/script/talk/m11_00_00_00.talkesdbnd.dcx', sha256: '023c10d0ce3ec6919ae65c6a0db7a21bebc479835003032adf8bb12aac329a83', text: false, size: 7609, kind: 'talk', origin: 'upstream' },
  { path: 'dist/DS1R/script/talk/m12_00_00_00.talkesdbnd.dcx', sha256: 'e70e729d2056311f75053b87a28ffa872e856a063bde34cd0ab650b31c82a701', text: false, size: 16403, kind: 'talk', origin: 'upstream' },
  { path: 'dist/DS1R/script/talk/m12_01_00_00.talkesdbnd.dcx', sha256: 'd15a2d56b1cd183da60fb83bed31faf58ecd0dd567eed68099b971f280733ab5', text: false, size: 23712, kind: 'talk', origin: 'upstream' },
  { path: 'dist/DS1R/script/talk/m13_00_00_00.talkesdbnd.dcx', sha256: 'da700a1dd6ce95c12504eed906b47d673456b2d21ea212528f77124790d308e4', text: false, size: 14927, kind: 'talk', origin: 'upstream' },
  { path: 'dist/DS1R/script/talk/m13_01_00_00.talkesdbnd.dcx', sha256: 'a6ea226c09ee303d1f5f481fffe2e943e2b499f6881de10472ece41fa48fbe3c', text: false, size: 13753, kind: 'talk', origin: 'upstream' },
  { path: 'dist/DS1R/script/talk/m13_02_00_00.talkesdbnd.dcx', sha256: 'd2c65c4cbe6d1fc95d419a6c79d6f50c1ff62daa92c33889817f2399502755a9', text: false, size: 10018, kind: 'talk', origin: 'upstream' },
  { path: 'dist/DS1R/script/talk/m14_00_00_00.talkesdbnd.dcx', sha256: '4e0dab28b36c67c0611b89dc674aeacbb8a6580271d77c4f47ae807cbaede84a', text: false, size: 28956, kind: 'talk', origin: 'upstream' },
  { path: 'dist/DS1R/script/talk/m14_01_00_00.talkesdbnd.dcx', sha256: '8951b09c33cdfe2effb5feb8f7019922b4146dcb3634c760a083b587a1614047', text: false, size: 10883, kind: 'talk', origin: 'upstream' },
  { path: 'dist/DS1R/script/talk/m15_00_00_00.talkesdbnd.dcx', sha256: 'cef2a2539da8f4468e6dc96ada98435ad5255bdcd83dd4265a29aebac30c75de', text: false, size: 11711, kind: 'talk', origin: 'upstream' },
  { path: 'dist/DS1R/script/talk/m15_01_00_00.talkesdbnd.dcx', sha256: '1335471155cbffa29ae868e158c6df8ac31473f297337bb48f21bcc3f0d6e805', text: false, size: 26142, kind: 'talk', origin: 'upstream' },
  { path: 'dist/DS1R/script/talk/m16_00_00_00.talkesdbnd.dcx', sha256: '20411cff65bb1d00f55369769f8cf06b0bdeabf762820b4b1677763e79b05b51', text: false, size: 21485, kind: 'talk', origin: 'upstream' },
  { path: 'dist/DS1R/script/talk/m17_00_00_00.talkesdbnd.dcx', sha256: '8104041480975defcfe2af1883272ff97697a13decda4b4e5b9cfddda6ce922b', text: false, size: 12685, kind: 'talk', origin: 'upstream' },
  { path: 'dist/DS1R/script/talk/m18_00_00_00.talkesdbnd.dcx', sha256: '9c48aec325b97b3ff07f52d8a1d5ebe50816f58c0778278d30d86b8f6cf2b0eb', text: false, size: 22955, kind: 'talk', origin: 'upstream' },
  { path: 'dist/DS1R/script/talk/m18_01_00_00.talkesdbnd.dcx', sha256: '1ea5f79f174bfd183a04a36a11d60120f8e8e5084b8fef276bef3ec2a01d62f5', text: false, size: 7974, kind: 'talk', origin: 'upstream' },
  { path: 'dist/fog.txt', sha256: '77a10b4015c51bdfbe1499f38344c85540a63bfa1ef6370ad9c1ebdc9a2d2af6', text: true, size: 147516, kind: 'fog-annotations', origin: 'upstream' },
];

/** Identifies the prepared set; changes when the pinned source or any expected hash changes. */
export const worldDataId = createHash('sha256')
  .update(`${worldDataSourceCommit}\n${worldDataManifest.map((entry) => `${entry.path}:${entry.sha256}`).join('\n')}`)
  .digest('hex').slice(0, 16);

export function hashWorldDataContent(bytes: Uint8Array, text: boolean): string {
  const hash = createHash('sha256');
  if (!text) return hash.update(bytes).digest('hex');
  return hash.update(Buffer.from(Buffer.from(bytes).toString('utf8').replace(/\r\n/gu, '\n'), 'utf8')).digest('hex');
}

export type WorldDataFailureCode =
  | 'WORLD_DATA_GAME_VERSION'
  | 'WORLD_DATA_GAME_FILES'
  | 'WORLD_DATA_OFFLINE'
  | 'WORLD_DATA_DOWNLOAD_CORRUPT'
  | 'WORLD_DATA_WRITE_FAILED';

export interface WorldDataFailure {
  ok: false;
  code: WorldDataFailureCode;
  /** Short plain-language text for the person using the app. */
  userMessage: string;
  /** Exact reason for logs and diagnostics. */
  technical: string;
}

export type WorldDataResult =
  | { ok: true; root: string; reused: boolean; downloaded: number; derived: number }
  | WorldDataFailure;

export interface WorldDataProgress { fraction: number; message: string }

export interface WorldDataOptions {
  /** Application-managed cache folder; the prepared set lives in `<cacheRoot>/<worldDataId>`. */
  cacheRoot: string;
  /** Verified clean snapshot of the installation, the source of every 'game' file. */
  pristineDirectory: string;
  manifest?: readonly WorldDataEntry[];
  baseUrl?: string;
  id?: string;
  /** Replaceable for tests; the default uses HTTPS `fetch` with a timeout. */
  fetchBytes?: (url: string, maxBytes: number) => Promise<Uint8Array>;
  onProgress?: (progress: WorldDataProgress) => void;
  attempts?: number;
}

export interface WorldDataVerification { ok: boolean; missing: string[]; altered: string[]; unexpected: string[] }

/** Same rules as FogHost's DataRoot.Verify: every manifest file present with the right hash, nothing extra in the enumerated folders. */
export async function verifyWorldDataRoot(root: string, manifest: readonly WorldDataEntry[] = worldDataManifest): Promise<WorldDataVerification> {
  const missing: string[] = [];
  const altered: string[] = [];
  for (const entry of manifest) {
    let content: Buffer;
    try { content = await readFile(path.join(root, ...entry.path.split('/'))); } catch { missing.push(entry.path); continue; }
    if (hashWorldDataContent(content, entry.text) !== entry.sha256) altered.push(entry.path);
  }
  const expected = new Set(manifest.map((entry) => entry.path.toLowerCase()));
  const unexpected: string[] = [];
  const folders = new Set(manifest.filter((entry) => entry.path.includes('/DS1R/')).map((entry) => entry.path.slice(0, entry.path.lastIndexOf('/'))));
  for (const folder of folders) {
    let names: string[];
    try { names = await readdir(path.join(root, ...folder.split('/'))); } catch { continue; }
    for (const name of names) {
      const relative = `${folder}/${name}`;
      if (!expected.has(relative.toLowerCase())) unexpected.push(relative);
    }
  }
  return { ok: missing.length === 0 && altered.length === 0 && unexpected.length === 0, missing, altered, unexpected };
}

async function defaultFetchBytes(url: string, maxBytes: number): Promise<Uint8Array> {
  if (!url.startsWith('https://')) throw new Error('Only HTTPS sources are allowed.');
  const response = await fetch(url, { signal: AbortSignal.timeout(30000), redirect: 'follow' });
  if (!response.ok) throw new Error(`HTTP ${response.status}`);
  const declared = Number(response.headers.get('content-length') ?? '0');
  if (declared > maxBytes) throw new Error(`Response of ${declared} bytes exceeds the ${maxBytes} byte bound.`);
  const bytes = new Uint8Array(await response.arrayBuffer());
  if (bytes.length > maxBytes) throw new Error(`Response of ${bytes.length} bytes exceeds the ${maxBytes} byte bound.`);
  return bytes;
}

const pending = new Map<string, Promise<WorldDataResult>>();

/**
 * Makes sure a verified data root exists in the application cache and returns it. A valid cache is reused without any
 * network access. Otherwise the set is assembled in a temporary folder next to it (game files copied from the pristine
 * snapshot, the rest downloaded and hash-checked), verified as a whole, and moved into place atomically. The live
 * installation is never touched. Concurrent calls for the same cache share one run.
 */
export function prepareWorldData(options: WorldDataOptions): Promise<WorldDataResult> {
  const key = path.resolve(options.cacheRoot, options.id ?? worldDataId);
  const running = pending.get(key);
  if (running) return running;
  const promise = assemble(options).finally(() => pending.delete(key));
  pending.set(key, promise);
  return promise;
}

function failure(code: WorldDataFailureCode, userMessage: string, technical: string): WorldDataFailure {
  return { ok: false, code, userMessage, technical };
}

async function assemble(options: WorldDataOptions): Promise<WorldDataResult> {
  const manifest = options.manifest ?? worldDataManifest;
  const id = options.id ?? worldDataId;
  const baseUrl = options.baseUrl ?? worldDataBaseUrl;
  const fetchBytes = options.fetchBytes ?? defaultFetchBytes;
  const attempts = options.attempts ?? 3;
  const report = (fraction: number, message: string): void => options.onProgress?.({ fraction, message });
  const root = path.join(options.cacheRoot, id);

  report(0, 'Checking world randomizer data...');
  const current = await verifyWorldDataRoot(root, manifest);
  if (current.ok) {
    report(1, 'World randomizer data is ready.');
    return { ok: true, root, reused: true, downloaded: 0, derived: 0 };
  }

  const staging = path.join(options.cacheRoot, `.preparing-${randomBytes(6).toString('hex')}`);
  try {
    await mkdir(staging, { recursive: true });
    let downloaded = 0;
    let derived = 0;
    const needed = manifest.length;
    let done = 0;
    const place = async (entry: WorldDataEntry, bytes: Uint8Array): Promise<void> => {
      const target = path.join(staging, ...entry.path.split('/'));
      await mkdir(path.dirname(target), { recursive: true });
      await writeFile(target, bytes);
    };

    // Files that are still good in an old cache are kept; only the rest is rebuilt.
    const bad = new Set([...current.missing, ...current.altered]);
    const reusable = new Set(manifest.filter((entry) => !bad.has(entry.path)).map((entry) => entry.path));
    for (const entry of manifest) {
      if (!reusable.has(entry.path)) continue;
      const target = path.join(staging, ...entry.path.split('/'));
      await mkdir(path.dirname(target), { recursive: true });
      await copyFile(path.join(root, ...entry.path.split('/')), target);
      done += 1;
    }

    for (const entry of manifest.filter((candidate) => candidate.origin === 'game' && !reusable.has(candidate.path))) {
      report(0.05 + 0.15 * (done / needed), 'Preparing world randomizer data...');
      let content: Buffer;
      try { content = await readFile(path.join(options.pristineDirectory, ...(entry.gamePath ?? '').split('/'))); } catch (error) {
        return failure('WORLD_DATA_GAME_FILES', 'Your Dark Souls Remastered files could not be read. Verify the game files in Steam and try again.',
          `Cannot read ${entry.gamePath} from the pristine snapshot: ${error instanceof Error ? error.message : String(error)}`);
      }
      const actual = hashWorldDataContent(content, entry.text);
      if (actual !== entry.sha256) {
        return failure('WORLD_DATA_GAME_VERSION',
          'This version of Dark Souls Remastered is not supported by the world randomizer yet. Verify the game files in Steam, or turn the world randomizer off.',
          `${entry.gamePath} does not match the expected content (sha256 ${actual}, expected ${entry.sha256}).`);
      }
      await place(entry, content);
      derived += 1;
      done += 1;
    }

    const toDownload = manifest.filter((candidate) => candidate.origin === 'upstream' && !reusable.has(candidate.path));
    let next = 0;
    let firstFailure: WorldDataFailure | null = null;
    const worker = async (): Promise<void> => {
      while (firstFailure === null) {
        const entry = toDownload[next];
        next += 1;
        if (!entry) return;
        const result = await download(entry, baseUrl, fetchBytes, attempts);
        if ('failure' in result) {
          firstFailure ??= result.failure;
          return;
        }
        await place(entry, result.bytes);
        downloaded += 1;
        done += 1;
        report(0.2 + 0.7 * (done / needed), 'Downloading required world randomizer data...');
      }
    };
    await Promise.all([worker(), worker(), worker(), worker()]);
    if (firstFailure) return firstFailure;

    report(0.92, 'Verifying world randomizer data...');
    const verification = await verifyWorldDataRoot(staging, manifest);
    if (!verification.ok) {
      return failure('WORLD_DATA_DOWNLOAD_CORRUPT', 'The world randomizer data could not be verified. Click Retry; if it keeps failing, check your internet connection.',
        `Prepared set failed verification: missing ${verification.missing.join(', ') || 'none'}; altered ${verification.altered.join(', ') || 'none'}; unexpected ${verification.unexpected.join(', ') || 'none'}.`);
    }

    // Atomic install: the old folder (if any) is moved aside, the verified one takes its place.
    const aside = `${root}.old-${randomBytes(4).toString('hex')}`;
    let movedAside = false;
    try {
      try { await stat(root); await rename(root, aside); movedAside = true; } catch { /* no previous cache */ }
      await rename(staging, root);
    } catch (error) {
      if (movedAside) await rename(aside, root).catch(() => undefined);
      return failure('WORLD_DATA_WRITE_FAILED', 'The world randomizer data could not be saved. Check that the disk has free space and try again.',
        `Cannot install ${root}: ${error instanceof Error ? error.message : String(error)}`);
    }
    if (movedAside) await rm(aside, { recursive: true, force: true }).catch(() => undefined);
    report(1, 'World randomizer data is ready.');
    return { ok: true, root, reused: false, downloaded, derived };
  } catch (error) {
    return failure('WORLD_DATA_WRITE_FAILED', 'The world randomizer data could not be prepared. Check that the disk has free space and try again.',
      error instanceof Error ? error.message : String(error));
  } finally {
    await rm(staging, { recursive: true, force: true }).catch(() => undefined);
  }
}

async function download(
  entry: WorldDataEntry, baseUrl: string, fetchBytes: NonNullable<WorldDataOptions['fetchBytes']>, attempts: number,
): Promise<{ bytes: Uint8Array } | { failure: WorldDataFailure }> {
  const url = baseUrl + entry.path;
  const bound = Math.max(entry.size * 2, 65536);
  let lastError = '';
  let corrupt = false;
  for (let attempt = 1; attempt <= attempts; attempt += 1) {
    try {
      const bytes = await fetchBytes(url, bound);
      const actual = hashWorldDataContent(bytes, entry.text);
      if (actual === entry.sha256) return { bytes };
      corrupt = true;
      lastError = `content hash ${actual} does not match the expected ${entry.sha256}`;
    } catch (error) {
      corrupt = false;
      lastError = error instanceof Error ? error.message : String(error);
    }
    if (attempt < attempts) await new Promise((resolve) => setTimeout(resolve, 250 * attempt));
  }
  return corrupt
    ? { failure: failure('WORLD_DATA_DOWNLOAD_CORRUPT', 'The world randomizer data arrived damaged. Click Retry.', `${url}: ${lastError}`) }
    : { failure: failure('WORLD_DATA_OFFLINE', 'The world randomizer files could not be prepared. Check your internet connection and click Retry.', `${url}: ${lastError}`) };
}
