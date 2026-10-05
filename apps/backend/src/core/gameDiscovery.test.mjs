import assert from 'node:assert/strict';
import test from 'node:test';
import { discoverGameInstallations, parseInstallDir, parseLibraryFolders } from '../../dist/core/gameDiscovery.js';

const LIBRARY_FILE = `"libraryfolders"
{
	"0"
	{
		"path"		"C:\\\\Program Files (x86)\\\\Steam"
		"apps"
		{
			"228980"		"123"
		}
	}
	"1"
	{
		"path"		"D:\\\\SteamLibrary"
		"apps"
		{
			"570940"		"456"
		}
	}
}`;

function environment({ registry = {}, files = {}, installed = [], valid = true, platform = 'win32', env = {} } = {}) {
  return {
    platform,
    env,
    readRegistry: async (key, value) => registry[`${key}|${value}`] ?? null,
    readText: async (file) => files[file.replaceAll('/', '\\')] ?? null,
    exists: async (target) => installed.includes(target.replaceAll('/', '\\')),
    validate: async (gamePath) => ({ valid, gamePath, executableFound: valid, gameParamFound: valid, mapStudioFound: valid, scriptFound: valid, eventFound: valid, problems: valid ? [] : ['DarkSoulsRemastered.exe not found.'] }),
  };
}

test('library folders and the install folder are read from Steam files', () => {
  assert.deepEqual(parseLibraryFolders(LIBRARY_FILE), ['C:\\Program Files (x86)\\Steam', 'D:\\SteamLibrary']);
  assert.equal(parseInstallDir('"AppState"\n{\n\t"installdir"\t\t"DARK SOULS REMASTERED"\n}'), 'DARK SOULS REMASTERED');
  assert.equal(parseInstallDir('nothing'), null);
});

test('the game is found through the registry and the extra library, without asking anything', async () => {
  const found = await discoverGameInstallations(environment({
    registry: { 'HKCU\\Software\\Valve\\Steam|SteamPath': 'c:\\program files (x86)\\steam' },
    files: { 'c:\\program files (x86)\\steam\\steamapps\\libraryfolders.vdf': LIBRARY_FILE },
    installed: ['D:\\SteamLibrary\\steamapps\\common\\DARK SOULS REMASTERED\\DarkSoulsRemastered.exe'],
  }));
  assert.equal(found.length, 1);
  assert.equal(found[0].gamePath, 'D:\\SteamLibrary\\steamapps\\common\\DARK SOULS REMASTERED');
  assert.equal(found[0].valid, true);
  assert.deepEqual(found[0].problems, []);
});

test('the app manifest decides the folder name and the usual Steam folder is a fallback when the registry is silent', async () => {
  const found = await discoverGameInstallations(environment({
    env: { 'ProgramFiles(x86)': 'C:\\Program Files (x86)' },
    files: { 'C:\\Program Files (x86)\\Steam\\steamapps\\appmanifest_570940.acf': '"installdir"\t\t"DS Remastered Custom"' },
    installed: ['C:\\Program Files (x86)\\Steam\\steamapps\\common\\DS Remastered Custom\\DarkSoulsRemastered.exe'],
  }));
  assert.deepEqual(found.map((item) => item.gamePath), ['C:\\Program Files (x86)\\Steam\\steamapps\\common\\DS Remastered Custom']);
});

test('several installations are all returned, the same folder reached twice only once', async () => {
  const found = await discoverGameInstallations(environment({
    registry: {
      'HKCU\\Software\\Valve\\Steam|SteamPath': 'C:\\Steam',
      'HKLM\\SOFTWARE\\WOW6432Node\\Valve\\Steam|InstallPath': 'c:\\steam',
    },
    files: { 'C:\\Steam\\steamapps\\libraryfolders.vdf': LIBRARY_FILE.replace('C:\\\\Program Files (x86)\\\\Steam', 'C:\\\\Steam') },
    installed: [
      'C:\\Steam\\steamapps\\common\\DARK SOULS REMASTERED\\DarkSoulsRemastered.exe',
      'D:\\SteamLibrary\\steamapps\\common\\DARK SOULS REMASTERED\\DarkSoulsRemastered.exe',
    ],
  }));
  assert.deepEqual(found.map((item) => item.gamePath).sort(), ['C:\\Steam\\steamapps\\common\\DARK SOULS REMASTERED', 'D:\\SteamLibrary\\steamapps\\common\\DARK SOULS REMASTERED']);
});

test('nothing found is an empty list; an incomplete folder is reported in plain language', async () => {
  assert.deepEqual(await discoverGameInstallations(environment({ registry: { 'HKCU\\Software\\Valve\\Steam|SteamPath': 'C:\\Steam' } })), []);
  assert.deepEqual(await discoverGameInstallations(environment({ platform: 'linux' })), []);
  const broken = await discoverGameInstallations(environment({
    registry: { 'HKCU\\Software\\Valve\\Steam|SteamPath': 'C:\\Steam' },
    installed: ['C:\\Steam\\steamapps\\common\\DARK SOULS REMASTERED\\DarkSoulsRemastered.exe'],
    valid: false,
  }));
  assert.equal(broken.length, 1);
  assert.equal(broken[0].valid, false);
  assert.match(broken[0].problems[0], /Verify the game files in Steam/u);
  assert.doesNotMatch(broken[0].problems.join(' '), /GameParam|MapStudio|\.exe/u);
});
