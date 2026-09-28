using System;
using System.IO;

namespace FogHost
{
    /// <summary>A typed failure reported in the response file (exit code 0).</summary>
    public sealed class FogHostException : Exception
    {
        public FogHostException(string code, string message, bool retryable = false, string retryScope = null)
            : base(message)
        {
            Code = code;
            Retryable = retryable;
            RetryScope = retryScope;
        }

        public string Code { get; }
        public bool Retryable { get; }
        public string RetryScope { get; }
        public string Stage { get; set; }
    }

    public static class HostErrors
    {
        public const string RequestInvalid = "FOG_REQUEST_INVALID";
        public const string DataMissing = "FOG_DATA_MISSING";
        public const string StagingUnsafe = "FOG_STAGING_UNSAFE";
        public const string UpstreamIncompatible = "FOG_UPSTREAM_INCOMPATIBLE";
        public const string SeedUnsolvable = "FOG_SEED_UNSOLVABLE";
        public const string MergeConflict = "FOG_MERGE_CONFLICT";
        public const string CompatibilityViolated = "FOG_COMPATIBILITY_VIOLATED";
        public const string IoFailed = "FOG_IO_FAILED";
        public const string EngineFailed = "FOG_ENGINE_FAILED";

        /// <summary>
        /// Classifies an exception raised by the FogMod engine (commit 6e6dc78) by its known
        /// throw sites. Anything unrecognized is non-retryable.
        /// </summary>
        public static FogHostException Classify(Exception error, string stage)
        {
            if (error is FogHostException typed)
            {
                typed.Stage ??= stage;
                return typed;
            }
            string message = error.Message ?? string.Empty;
            FogHostException result;
            if ((message.StartsWith("Couldn't find ", StringComparison.Ordinal) && message.EndsWith(" in item lots", StringComparison.Ordinal))
                || (message.StartsWith("Item ", StringComparison.Ordinal) && message.Contains(" found in both ", StringComparison.Ordinal)))
            {
                // ItemReader.cs: a key item is outside FogMod's annotated item lots.
                result = new FogHostException(UpstreamIncompatible, message, true, "combined");
            }
            else if (message.StartsWith("Couldn't solve seed", StringComparison.Ordinal)
                || message.StartsWith("Ran out of eligible edges", StringComparison.Ordinal)
                || message.StartsWith("Could not find edge into unreachable areas", StringComparison.Ordinal)
                || message.StartsWith("Bad seed:", StringComparison.Ordinal))
            {
                // GraphConnector.cs: this Fog seed cannot be solved; another Fog seed may be.
                result = new FogHostException(SeedUnsolvable, message, true, "fog");
            }
            else if (error is IOException || error is UnauthorizedAccessException
                || message.StartsWith("Failed to load ", StringComparison.Ordinal))
            {
                result = new FogHostException(IoFailed, message);
            }
            else
            {
                result = new FogHostException(EngineFailed, $"{error.GetType().Name}: {message}");
            }
            result.Stage = stage;
            return result;
        }
    }
}
