// electron-builder never copies a `node_modules` directory through extraResources. The packaged backend imports
// @ds1tr/contracts at run time, so the staged copy is placed into the unpacked runtime after packing, before the
// portable target is built.
const fs = require('node:fs');
const path = require('node:path');

exports.default = async function stageRuntimeModules(context) {
  const source = path.join(__dirname, '..', 'release', 'runtime', 'node_modules');
  if (!fs.existsSync(source)) throw new Error(`Staged runtime modules are missing: ${source}`);
  fs.cpSync(source, path.join(context.appOutDir, 'resources', 'runtime', 'node_modules'), { recursive: true });
};
