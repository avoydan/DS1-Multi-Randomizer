using System;
using System.IO;
using SoulsFormats;

namespace FogReconstruction
{
    internal static class ParamDefInspector
    {
        public static void Inspect(string pristineRoot)
        {
            string path = Path.Combine(pristineRoot, "paramdef", "paramdef.paramdefbnd.dcx");
            if (!File.Exists(path)) throw new FileNotFoundException("The pristine snapshot does not contain the DSR ParamDef binder.", path);
            byte[] bytes = File.ReadAllBytes(path);
            if (DCX.Is(bytes)) bytes = DCX.Decompress(bytes);
            IBinder binder = BND4.Is(bytes) ? (IBinder)BND4.Read(bytes)
                : BND3.Is(bytes) ? (IBinder)BND3.Read(bytes)
                : throw new InvalidDataException("ParamDef binder is not BND3/BND4 after decompression.");

            foreach (BinderFile entry in binder.Files)
            {
                PARAMDEF definition = PARAMDEF.Read(entry.Bytes);
                Console.WriteLine($"{Path.GetFileNameWithoutExtension(entry.Name)}\t{definition.ParamType}\trowSize={definition.GetRowSize()}\tfields={definition.Fields.Count}");
            }
        }
    }
}
