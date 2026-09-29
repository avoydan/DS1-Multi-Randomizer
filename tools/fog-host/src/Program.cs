using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace FogHost
{
    /// <summary>
    /// <c>FogHost.exe generate --request &lt;json&gt; --response &lt;json&gt;</c>.
    /// Exit code 0 means the response file is authoritative (success or typed failure).
    /// Any other exit code means the host crashed or was invoked incorrectly.
    /// </summary>
    public static class Program
    {
        public const string HostVersion = "0.3.0";

        public static int Main(string[] args)
        {
            if (args.Length != 5 || args[0] != "generate" || args[1] != "--request" || args[3] != "--response")
            {
                Console.Error.WriteLine("Usage: FogHost generate --request <request.json> --response <response.json>");
                return 2;
            }
            string requestPath = args[2];
            string responsePath = args[4];

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            TextWriter stdout = Console.Out;
            TextWriter stderr = Console.Error;
            void Progress(string stage, string message)
            {
                stdout.WriteLine(JsonSerializer.Serialize(new { type = "progress", stage, message }));
                stdout.Flush();
            }

            var runner = new HostRunner(Progress);
            HostRequest request = null;
            byte[] response;
            string originalDirectory = Environment.CurrentDirectory;
            StreamWriter log = null;
            try
            {
                string json;
                try
                {
                    json = File.ReadAllText(requestPath);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    throw new FogHostException(HostErrors.RequestInvalid, $"Cannot read request file: {e.Message}") { Stage = "validate-request" };
                }
                request = HostRequest.Parse(json);
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(request.LogPath));
                    log = new StreamWriter(new FileStream(request.LogPath, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)) { AutoFlush = true };
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                {
                    throw new FogHostException(HostErrors.IoFailed, $"Cannot open logPath: {e.Message}") { Stage = "validate-request" };
                }
                // FogMod output is diagnostics only and is never parsed as protocol.
                Console.SetOut(log);
                Console.SetError(log);
                runner.ValidateLayout(request);
                Environment.CurrentDirectory = request.FogDataRoot;
                HostResult result = runner.Run(request);
                response = Responses.Success(request, result);
            }
            catch (Exception e)
            {
                FogHostException error = HostErrors.Classify(e, runner.Stage);
                try
                {
                    Console.WriteLine($"FogHost failure {error.Code} at {error.Stage}: {e}");
                }
                catch (Exception)
                {
                    // Logging must never mask the typed failure.
                }
                response = Responses.Failure(error);
            }
            finally
            {
                Environment.CurrentDirectory = originalDirectory;
                Console.SetOut(stdout);
                Console.SetError(stderr);
                log?.Dispose();
            }

            try
            {
                string temporary = responsePath + ".tmp";
                File.WriteAllBytes(temporary, response);
                File.Move(temporary, responsePath, true);
            }
            catch (Exception e)
            {
                stderr.WriteLine($"FogHost could not write the response file: {e.Message}");
                return 3;
            }
            return 0;
        }
    }
}
