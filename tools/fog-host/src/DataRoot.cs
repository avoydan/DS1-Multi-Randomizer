using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace FogHost
{
    /// <summary>
    /// Verifies an externally supplied FogMod data root. The data (dist/DS1R) is FromSoftware-derived
    /// and is never bundled; the user supplies the `dist` folder of FogMod commit 6e6dc78.
    /// </summary>
    public static class DataRoot
    {
        public const string FogModCommit = "6e6dc7886d0f8bcc53c1800d95c9e859d1f2f87f";

        public sealed record Entry(string Path, string Sha256, bool Text);

        /// <summary>
        /// Exactly the files the patched DS1R engine reads from the data root. Text files (.txt/.xml)
        /// are hashed after CRLF -> LF normalization, so a checkout with core.autocrlf still verifies.
        /// </summary>
        public static readonly IReadOnlyList<Entry> Manifest = new Entry[]
        {
            new("dist/DS1R/Layouts/GAME_AREA_PARAM_ST.xml", "c05a0beb92ee9b49ba3bd80270681014d9ad2e0b055dddff9aff94f89e78f75a", true),
            new("dist/DS1R/Layouts/ITEMLOT_PARAM_ST.xml", "e3f0f03d8ac3cf9fd3d4ce2a49d71332aaec6e2ac8f792dd485f5a5bf097fd66", true),
            new("dist/DS1R/Layouts/NPC_PARAM_ST.xml", "69e7cc19e829f7132c5b91c9600a1a4ef4800071032286345673b496120c26a1", true),
            new("dist/DS1R/Layouts/SP_EFFECT_PARAM_ST.xml", "ad96136a44f1135b469ac48d30469efcd9ad1ea3e0daf1eda32212980e2b10df", true),
            new("dist/DS1R/event/common.emevd.dcx", "2018562b4cc5c4ebb1de818ea7da736093e66c88848acd983ae6306b79bf71c0", false),
            new("dist/DS1R/event/m10_00_00_00.emevd.dcx", "6494c24b1149c6423fa085dc2b3a5cb08da4903ed40fbc4b4fa7aa4d8b91274b", false),
            new("dist/DS1R/event/m10_01_00_00.emevd.dcx", "96c3ddf96e35cad5c77d5d035de9e04929351fc87515357d0dc63578bae235fb", false),
            new("dist/DS1R/event/m10_02_00_00.emevd.dcx", "0be8539a72302a35af0649350978918f1d332fc9b568b48868f206863b59428d", false),
            new("dist/DS1R/event/m11_00_00_00.emevd.dcx", "60caf4122637037a7b3e2d7cdc1bf1792a80efd61f19356a002ff27276fcc7d2", false),
            new("dist/DS1R/event/m12_00_00_00.emevd.dcx", "8822ee9b1f7015d4bf21754cea9b0f46198ffcb1e7b812cfec9c7126c73a9ee4", false),
            new("dist/DS1R/event/m12_01_00_00.emevd.dcx", "c17551fdf12c028b4d4d4f9220aca98df428431ba30842260f25df6542eb04ea", false),
            new("dist/DS1R/event/m13_00_00_00.emevd.dcx", "9e65ecd1824111a97f341eccbd47045b8c0937ac7a187c57567a77d493316d80", false),
            new("dist/DS1R/event/m13_01_00_00.emevd.dcx", "10043a7b990e97443d744c7753514cf516aba0b7e92af4551e5e328660203cd4", false),
            new("dist/DS1R/event/m13_02_00_00.emevd.dcx", "706dc53f705acc92db2986d3a2a96f2138f66b816b60b96f91960f110f7b42fc", false),
            new("dist/DS1R/event/m14_00_00_00.emevd.dcx", "bd9fb9592d3787d2cb58d7336f0a9d37202131d47b8c8cd86a542a4c807619ea", false),
            new("dist/DS1R/event/m14_01_00_00.emevd.dcx", "26fa087386ae61522cd5262bcd5e44a2cbc4c914e7ac5b915d84b70b1f7848f7", false),
            new("dist/DS1R/event/m15_00_00_00.emevd.dcx", "c1b8caf046c0cdb35cdaf053bf0f6534f84cfce6f20ad88f4008407b7c35cca9", false),
            new("dist/DS1R/event/m15_01_00_00.emevd.dcx", "30690a5adf81838707dcf92e016848548fa42c9745c0d0e275c3371919ef30b2", false),
            new("dist/DS1R/event/m16_00_00_00.emevd.dcx", "e609feedae9a4edc9d9751f27fe1f35c240911c0aecb5e25a0a49ead7274ea3f", false),
            new("dist/DS1R/event/m17_00_00_00.emevd.dcx", "8e1d3ae997471dbace1a361bf6fdba0226d9dc17b1f0a5c2f59119c4ca14cbc4", false),
            new("dist/DS1R/event/m18_00_00_00.emevd.dcx", "f210406c69fff8633f188707558f555386561968f41c23c8c1b083d147e4c63c", false),
            new("dist/DS1R/event/m18_01_00_00.emevd.dcx", "8382905c412f9f366470fe91cc6dad528a512703ddb37f435bbe89adedc41229", false),
            new("dist/DS1R/map/MapStudio/m10_00_00_00.msb", "bda3f68445f13730b5bcd950c5413d747a9cf8dbb999d2c11c72f5ba5b8a8ac0", false),
            new("dist/DS1R/map/MapStudio/m10_01_00_00.msb", "d33134ff30f3811d9052d1270731eb843d7d2f333f7d968c895cc1dffed4f1d5", false),
            new("dist/DS1R/map/MapStudio/m10_02_00_00.msb", "0d1656fe2c8a4bdf37232565abbc936b86b3cbabdb898de299a4576587bac086", false),
            new("dist/DS1R/map/MapStudio/m11_00_00_00.msb", "d0fd37f91d82f26413d7dec29cc2e66638c52e0502c1373581bc5d13db0b01f0", false),
            new("dist/DS1R/map/MapStudio/m12_00_00_00.msb", "eeb088800c225df4a6a313002d85bb41b626e29a9dc42257027ab3373c751e00", false),
            new("dist/DS1R/map/MapStudio/m12_00_00_01.msb", "5680674234f89c7899f4ec16df8acd24fca51d20ddf98fd7a25bc721b67a3fc9", false),
            new("dist/DS1R/map/MapStudio/m12_01_00_00.msb", "e68be5d13ee27ecf42e30f50b649a98a4bc4fcba75c0207cb34d37c774a605e1", false),
            new("dist/DS1R/map/MapStudio/m13_00_00_00.msb", "9bff6063d0e7ce80f8a316d49dc737a7d1d27328ced29c105d7cf32d9b737585", false),
            new("dist/DS1R/map/MapStudio/m13_01_00_00.msb", "5d5a2956d467a98f10b296a0b04c8d30bbfd3e3794a9a106c25381d82cd19394", false),
            new("dist/DS1R/map/MapStudio/m13_02_00_00.msb", "de8f234dd5af3e022e8508c772011fd44e8d36e0d1d0dad042adf8bab09dc779", false),
            new("dist/DS1R/map/MapStudio/m14_00_00_00.msb", "4ecb1a8329d66d21f726c12617f63f0a3b2a62279da251b200eaca664a16e4e9", false),
            new("dist/DS1R/map/MapStudio/m14_01_00_00.msb", "eb3262dce99a28667c487dea46b3bb0e97fa884cf7544901ab1984bc9cddd86f", false),
            new("dist/DS1R/map/MapStudio/m15_00_00_00.msb", "1828f5540dbd3cb91e0a2b1143e2a7b83ed7764554f6e4cf70e2028ad5a45419", false),
            new("dist/DS1R/map/MapStudio/m15_01_00_00.msb", "a077593fda6c8d73b7220366f23e23716560f1d1fbd03b09bdc97be11d271367", false),
            new("dist/DS1R/map/MapStudio/m16_00_00_00.msb", "b6c61a5e90882d450d8dea85f49c90f926b2b893df05c993c25c1908e457e01a", false),
            new("dist/DS1R/map/MapStudio/m17_00_00_00.msb", "b8eed7579c30abcaf731c4c470a79f69c8449ac266dd5c3875d178168fa22fc0", false),
            new("dist/DS1R/map/MapStudio/m18_00_00_00.msb", "ddb52ac2638e47ef963917080844405adcbe93986becc7cee19fa2086150c6af", false),
            new("dist/DS1R/map/MapStudio/m18_01_00_00.msb", "d9051999ea31aaffe77accad352985cc7629b791c115d28266ea20c8b36aa56e", false),
            new("dist/DS1R/param/GameParam/GameParam.parambnd.dcx", "cc4a81cc87f028534d965c908a8aa7dba2f9e235ac30f5e72ddf6bb45dc7b5ab", false),
            new("dist/DS1R/script/talk/m10_00_00_00.talkesdbnd.dcx", "439b9c8179908581b45495a43bd1b57c5319b848cdb699c29fa27fae815c0bcc", false),
            new("dist/DS1R/script/talk/m10_01_00_00.talkesdbnd.dcx", "3bf132fd86644f6457e24a10bd641db790938dfed47952c8228c37ac5f4c270f", false),
            new("dist/DS1R/script/talk/m10_02_00_00.talkesdbnd.dcx", "067c6dd0f337f140012ee466a830373d7eaff24cbd40c774e82554cf43f8d968", false),
            new("dist/DS1R/script/talk/m11_00_00_00.talkesdbnd.dcx", "023c10d0ce3ec6919ae65c6a0db7a21bebc479835003032adf8bb12aac329a83", false),
            new("dist/DS1R/script/talk/m12_00_00_00.talkesdbnd.dcx", "e70e729d2056311f75053b87a28ffa872e856a063bde34cd0ab650b31c82a701", false),
            new("dist/DS1R/script/talk/m12_01_00_00.talkesdbnd.dcx", "d15a2d56b1cd183da60fb83bed31faf58ecd0dd567eed68099b971f280733ab5", false),
            new("dist/DS1R/script/talk/m13_00_00_00.talkesdbnd.dcx", "da700a1dd6ce95c12504eed906b47d673456b2d21ea212528f77124790d308e4", false),
            new("dist/DS1R/script/talk/m13_01_00_00.talkesdbnd.dcx", "a6ea226c09ee303d1f5f481fffe2e943e2b499f6881de10472ece41fa48fbe3c", false),
            new("dist/DS1R/script/talk/m13_02_00_00.talkesdbnd.dcx", "d2c65c4cbe6d1fc95d419a6c79d6f50c1ff62daa92c33889817f2399502755a9", false),
            new("dist/DS1R/script/talk/m14_00_00_00.talkesdbnd.dcx", "4e0dab28b36c67c0611b89dc674aeacbb8a6580271d77c4f47ae807cbaede84a", false),
            new("dist/DS1R/script/talk/m14_01_00_00.talkesdbnd.dcx", "8951b09c33cdfe2effb5feb8f7019922b4146dcb3634c760a083b587a1614047", false),
            new("dist/DS1R/script/talk/m15_00_00_00.talkesdbnd.dcx", "cef2a2539da8f4468e6dc96ada98435ad5255bdcd83dd4265a29aebac30c75de", false),
            new("dist/DS1R/script/talk/m15_01_00_00.talkesdbnd.dcx", "1335471155cbffa29ae868e158c6df8ac31473f297337bb48f21bcc3f0d6e805", false),
            new("dist/DS1R/script/talk/m16_00_00_00.talkesdbnd.dcx", "20411cff65bb1d00f55369769f8cf06b0bdeabf762820b4b1677763e79b05b51", false),
            new("dist/DS1R/script/talk/m17_00_00_00.talkesdbnd.dcx", "8104041480975defcfe2af1883272ff97697a13decda4b4e5b9cfddda6ce922b", false),
            new("dist/DS1R/script/talk/m18_00_00_00.talkesdbnd.dcx", "9c48aec325b97b3ff07f52d8a1d5ebe50816f58c0778278d30d86b8f6cf2b0eb", false),
            new("dist/DS1R/script/talk/m18_01_00_00.talkesdbnd.dcx", "1ea5f79f174bfd183a04a36a11d60120f8e8e5084b8fef276bef3ec2a01d62f5", false),
            new("dist/fog.txt", "77a10b4015c51bdfbe1499f38344c85540a63bfa1ef6370ad9c1ebdc9a2d2af6", true),
        };

        /// <summary>Directories FogMod enumerates; any extra file there would change its behavior.</summary>
        private static readonly (string Directory, string Pattern)[] Enumerated =
        {
            ("dist/DS1R/Layouts", "*"),
            ("dist/DS1R/event", "*"),
            ("dist/DS1R/map/MapStudio", "*"),
            ("dist/DS1R/script/talk", "*"),
        };

        public static IEnumerable<string> EventFiles => Names("dist/DS1R/event/");
        public static IEnumerable<string> MapFiles => Names("dist/DS1R/map/MapStudio/");

        private static IEnumerable<string> Names(string prefix) =>
            Manifest.Where(e => e.Path.StartsWith(prefix, StringComparison.Ordinal)).Select(e => e.Path.Substring(prefix.Length));

        /// <summary>Throws FOG_DATA_MISSING listing every missing, altered, or unexpected file.</summary>
        public static void Verify(string root)
        {
            var problems = new List<string>();
            if (!Directory.Exists(root))
            {
                throw Missing(root, new[] { $"directory does not exist: {root}" });
            }
            foreach (Entry entry in Manifest)
            {
                string file = Resolve(root, entry.Path);
                if (!File.Exists(file))
                {
                    problems.Add($"missing {entry.Path}");
                    continue;
                }
                if (!string.Equals(Hash(file, entry.Text), entry.Sha256, StringComparison.Ordinal))
                {
                    problems.Add($"altered {entry.Path}");
                }
            }
            var expected = new HashSet<string>(Manifest.Select(e => e.Path), StringComparer.OrdinalIgnoreCase);
            foreach ((string directory, string pattern) in Enumerated)
            {
                string full = Resolve(root, directory);
                if (!Directory.Exists(full)) continue;
                foreach (string file in Directory.GetFiles(full, pattern).OrderBy(f => f, StringComparer.Ordinal))
                {
                    string relative = directory + "/" + System.IO.Path.GetFileName(file);
                    if (!expected.Contains(relative)) problems.Add($"unexpected {relative}");
                }
            }
            if (problems.Count > 0) throw Missing(root, problems);
        }

        /// <summary>SHA-256 of fog.txt as it is pinned (LF-normalized).</summary>
        public static string AnnotationSha256(string root) => Hash(Resolve(root, "dist/fog.txt"), true);

        public static string Hash(string file, bool text)
        {
            byte[] data = File.ReadAllBytes(file);
            if (text) data = NormalizeLineEndings(data);
            return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        }

        private static byte[] NormalizeLineEndings(byte[] data)
        {
            var output = new List<byte>(data.Length);
            for (int i = 0; i < data.Length; i++)
            {
                if (data[i] == (byte)'\r' && i + 1 < data.Length && data[i + 1] == (byte)'\n') continue;
                output.Add(data[i]);
            }
            return output.ToArray();
        }

        private static string Resolve(string root, string relative) =>
            System.IO.Path.Combine(root, relative.Replace('/', System.IO.Path.DirectorySeparatorChar));

        private static FogHostException Missing(string root, IReadOnlyCollection<string> problems)
        {
            string shown = string.Join("; ", problems.Take(12)) + (problems.Count > 12 ? $"; and {problems.Count - 12} more" : "");
            return new FogHostException(HostErrors.DataMissing,
                $"FogMod data root is missing, incomplete, or not the pinned version ({shown}). "
                + $"Supply the folder that contains `dist` from https://github.com/thefifthmatt/FogMod at commit {FogModCommit} "
                + "(dist/fog.txt and dist/DS1R/{Layouts,event,map/MapStudio,param/GameParam,script/talk}). "
                + "FogMod releases or other commits are not accepted.")
            { Stage = "validate-request" };
        }
    }
}
