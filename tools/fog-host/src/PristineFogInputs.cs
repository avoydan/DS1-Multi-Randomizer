using System;
using System.IO;
using System.Linq;

namespace FogHost
{
    /// <summary>Checks the local DSR files from which the Compact runtime reconstructs FogMod's data inputs.</summary>
    public static class PristineFogInputs
    {
        private const string ParamDefPath = "paramdef/paramdef.paramdefbnd.dcx";
        private const string ParamDefSha256 = "647bd58afc8f4f30c6d007626bf50fc0e66d9a93f6bf56666e29c0d767e0f6e6";

        public static void Verify(string pristineRoot)
        {
            string root = Path.GetFullPath(pristineRoot ?? throw new ArgumentNullException(nameof(pristineRoot)));
            int verified = 0;
            foreach (DataRoot.Entry entry in DataRoot.Manifest.Where(e =>
                         e.Path.StartsWith("dist/DS1R/map/MapStudio/", StringComparison.Ordinal)
                         || e.Path.StartsWith("dist/DS1R/param/GameParam/", StringComparison.Ordinal)))
            {
                string relative = entry.Path.Substring("dist/DS1R/".Length).Replace('/', Path.DirectorySeparatorChar);
                string path = Path.Combine(root, relative);
                if (!File.Exists(path)) throw new FileNotFoundException("The verified pristine DSR snapshot is missing a Fog input.", path);
                string actual = DataRoot.Hash(path, entry.Text);
                if (!string.Equals(actual, entry.Sha256, StringComparison.Ordinal))
                    throw new InvalidDataException($"Pristine DSR input does not match the pinned build: {relative}.");
                verified++;
            }
            if (verified != 19) throw new InvalidDataException($"Expected 19 pristine Fog base files, verified {verified}.");

            string paramDefPath = Path.Combine(root, ParamDefPath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(paramDefPath)) throw new FileNotFoundException("The pristine DSR snapshot is missing ParamDef.", paramDefPath);
            if (!string.Equals(DataRoot.Hash(paramDefPath, text: false), ParamDefSha256, StringComparison.Ordinal))
                throw new InvalidDataException("Pristine DSR ParamDef does not match the pinned DSR build.");
        }
    }
}
