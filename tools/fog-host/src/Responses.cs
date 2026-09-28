using System.IO;
using System.Text.Json;

namespace FogHost
{
    /// <summary>
    /// Schema-v2 response writer. Key order is fixed and no absolute paths or timestamps are
    /// emitted, so two runs with the same inputs produce byte-identical responses.
    /// </summary>
    public static class Responses
    {
        public static byte[] Success(HostRequest request, HostResult result) => Write(json =>
        {
            json.WriteNumber("schemaVersion", HostRequest.SchemaVersion);
            json.WriteBoolean("ok", true);
            json.WriteString("hostVersion", Program.HostVersion);
            json.WriteString("fogModCommit", DataRoot.FogModCommit);
            json.WriteString("annotationSha256", result.AnnotationSha256);
            json.WriteString("mode", request.Mode);
            json.WriteNumber("seed", request.Seed);
            json.WriteString("fogOptions", result.FogOptions);
            json.WriteStartObject("keyItems");
            json.WriteBoolean("randomized", result.KeyItemsRandomized);
            json.WriteString("hash", result.KeyItemsHash);
            json.WriteEndObject();
            json.WriteStartArray("files");
            foreach (OutputFile file in result.Files)
            {
                json.WriteStartObject();
                json.WriteString("path", file.Path);
                json.WriteNumber("size", file.Size);
                json.WriteString("sha256", file.Sha256);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteNull("error");
        });

        public static byte[] Failure(FogHostException error) => Write(json =>
        {
            json.WriteNumber("schemaVersion", HostRequest.SchemaVersion);
            json.WriteBoolean("ok", false);
            json.WriteStartObject("error");
            json.WriteString("code", error.Code);
            json.WriteString("stage", error.Stage ?? "validate-request");
            json.WriteBoolean("retryable", error.Retryable);
            if (error.RetryScope == null) json.WriteNull("retryScope");
            else json.WriteString("retryScope", error.RetryScope);
            json.WriteString("message", error.Message);
            json.WriteEndObject();
        });

        private static byte[] Write(System.Action<Utf8JsonWriter> body)
        {
            using var stream = new MemoryStream();
            using (var json = new Utf8JsonWriter(stream))
            {
                json.WriteStartObject();
                body(json);
                json.WriteEndObject();
            }
            stream.WriteByte((byte)'\n');
            return stream.ToArray();
        }
    }
}
