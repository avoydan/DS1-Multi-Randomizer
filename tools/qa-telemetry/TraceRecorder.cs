using System.Collections.Concurrent;
using System.Text.Json;

namespace QaTelemetry;

public static class TraceRecorder
{
    private const int MaximumRecords = 9990;
    private const int MaximumQueuedSamples = 500;
    private static readonly string[] Checkpoints =
    [
        "CEASELESS_PRE_FIGHT", "CEASELESS_BOSS_ACTIVE", "CEASELESS_DEFEATED", "EXIT_APPROACH",
        "EXIT_PROMPT_PRESENT", "EXIT_CROSSED", "DESTINATION_LANDED", "MAIN_DEMON_RUINS_REACHED",
        "LOWER_LAVA_REACHED", "POST_RELOAD_EXIT_VERIFIED"
    ];

    public static int Run(CaseDescriptor descriptor, string profilePath, string? requestedTracePath)
    {
        PackageVerification package = PackageVerifier.Verify(descriptor);
        if (!package.Ok) throw new QaTelemetryException(package.Code, package.Message);
        RuntimePreflightResult runtime = RuntimePreflight.Run(descriptor);
        if (!runtime.Ok || !string.Equals(runtime.ModifiedManifestSha256, descriptor.Runtime!.ModifiedManifestSha256, StringComparison.OrdinalIgnoreCase))
            throw new QaTelemetryException("QA_RUNTIME_PREFLIGHT_BLOCKED", string.Join("; ", runtime.Blockers));
        MarkerProfileCheck profileCheck = MarkerProfileStore.LoadVerified(profilePath, descriptor.Runtime.ExecutableSha256!,
            ExactBuildProfile.ExpectedRuntimeDigest, ExactBuildProfile.ToolVersion, ExactBuildProfile.DefinitionSha256);
        if (!profileCheck.Ok || profileCheck.Profile is null || profileCheck.ProfileSha256 is null)
            throw new QaTelemetryException(profileCheck.Code, profileCheck.Message);
        ExactBuildAuditResult audit = ExactBuildProfile.Audit(ExactBuildProfile.ExpectedExecutablePath);
        if (audit.Status != "EXACT_BUILD_SIGNATURES_UNIQUE_CANDIDATE")
            throw new QaTelemetryException("QA_SIGNATURE_UNVERIFIED", "Exact-build signatures changed or became ambiguous; trace mode is disabled.");

        using ReadOnlyGameProcess process = ReadOnlyGameProcess.Attach(descriptor);
        string sessionId = "trace-" + Guid.NewGuid().ToString("N");
        string tracePath = requestedTracePath ?? Path.Combine(Path.GetTempPath(), "ds1tr-qa-telemetry", "traces", sessionId + ".jsonl");
        using var writer = TraceSessionWriter.CreateLive(descriptor, tracePath, sessionId, profilePath, process.Identity, ExactBuildProfile.ExpectedRuntimeDigest);
        using var cancellation = new CancellationTokenSource();
        var samples = new ConcurrentQueue<CandidateTelemetrySample>();
        var samplingErrors = new ConcurrentQueue<Exception>();
        int queuedSamples = 0;
        Task sampler = Task.Run(() => SampleInBackground(process, audit, samples, samplingErrors, cancellation.Token,
            () => Interlocked.Increment(ref queuedSamples), () => Interlocked.Decrement(ref queuedSamples)));

        Console.WriteLine("Verified read-only trace mode. No game input is sent. The game must remain on this exact QA runtime and package.");
        Console.WriteLine("Press 1-0 to annotate the ordered checkpoints, W to report a death/warp/loading/gap, or Q to seal the trace.");
        Console.WriteLine("Checkpoint keys record only that you reached the named checkpoint. Every outcome requires an explicit y/n answer.");
        for (int i = 0; i < Checkpoints.Length; i++) Console.WriteLine($"{i + 1}: {Checkpoints[i]}");

        CandidateTelemetrySample? previous = null;
        CandidateTelemetrySample? latest = null;
        TraceRecord? latestTransitionObservation = null;
        int recordCount = 1;
        bool complete = false;
        try
        {
            while (!complete && recordCount < MaximumRecords)
            {
                DrainSamples();
                ThrowSamplingErrorIfAny();
                if (Console.KeyAvailable)
                {
                    ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                    if (key.Key == ConsoleKey.Q) { complete = true; continue; }
                    if (key.Key == ConsoleKey.W)
                    {
                        string interruption = ReadInterruptionKind();
                        DrainSamples();
                        writer.Append(new TraceRecord
                        {
                            Type = "observation",
                            TransitionType = interruption,
                            OwnerAnnotationTimestampUtc = DateTimeOffset.UtcNow.ToString("O"),
                            Confidence = "OWNER_REPORTED_INTERRUPTION"
                        });
                        recordCount++;
                        continue;
                    }

                    int checkpointIndex = CheckpointIndex(key.Key);
                    if (checkpointIndex >= 0)
                    {
                        DateTimeOffset ownerTime = DateTimeOffset.UtcNow;
                        TraceRecord annotation = BuildAnnotation(Checkpoints[checkpointIndex], ownerTime);
                        DrainSamples();
                        CopyLatestObservedState(annotation, latest, latestTransitionObservation);
                        writer.Append(annotation);
                        recordCount++;
                    }
                }
                Thread.Sleep(50);
            }
            if (!complete) Console.WriteLine("Record limit reached; sealing the trace.");
        }
        finally
        {
            cancellation.Cancel();
            if (!sampler.Wait(TimeSpan.FromSeconds(5)))
                throw new QaTelemetryException("QA_SAMPLER_SHUTDOWN_TIMEOUT", "Background read-only sampler did not stop promptly.");
        }

        DrainSamples();
        ThrowSamplingErrorIfAny();
        var completed = writer.Complete();
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            status = "TRACE_COMPLETE",
            sessionId,
            tracePath,
            traceSha256 = completed.Summary.TraceSha256,
            summaryPath = completed.SummaryPath,
            summarySha256 = SafePaths.HashFile(completed.SummaryPath),
            summarySha256Path = completed.SummarySha256Path,
            records = completed.Summary.RecordCount,
            runtimeDigest = ExactBuildProfile.ExpectedRuntimeDigest,
            executableSha256 = descriptor.Runtime!.ExecutableSha256,
            packageTreeSha256 = descriptor.Package!.TreeSha256,
            runManifestSha256 = descriptor.Package.RunManifestSha256,
            profileSha256 = profileCheck.ProfileSha256,
            profileStatus = "VERIFIED",
            writeCapableProcessRights = false
        }, CaseDescriptor.JsonOptions));
        return 0;

        void DrainSamples()
        {
            while (samples.TryDequeue(out CandidateTelemetrySample? sample))
            {
                Interlocked.Decrement(ref queuedSamples);
                if (sample is null) continue;
                if (recordCount >= MaximumRecords) throw new QaTelemetryException("QA_TRACE_RECORD_LIMIT", "Trace reached its record limit before all queued samples could be written.");
                string? readFailure = sample.MapReadError ?? sample.PositionReadError ?? sample.FlagReadError;
                if (readFailure is not null)
                {
                    MarkerProfileStore.InvalidateAfterReadFailure(profilePath, readFailure);
                    throw new QaTelemetryException("QA_MARKER_PROFILE_INVALIDATED", "An exact-build pointer or field read failed. The profile has been invalidated and the trace will not be sealed.");
                }
                TraceRecord record = ToRecord(sample, profileCheck.Profile!, previous);
                if (previous is not null && sample.TimestampUtc - previous.TimestampUtc > TimeSpan.FromSeconds(3))
                    record.TransitionType = "SAMPLING_GAP";
                if (record.TransitionType == "OWNER_OBSERVED_MAP_CHANGE") latestTransitionObservation = record;
                writer.Append(record);
                recordCount++;
                previous = latest = sample;
            }
        }

        void ThrowSamplingErrorIfAny()
        {
            if (samplingErrors.TryDequeue(out Exception? error))
            {
                if (error is QaTelemetryException { Code: "QA_MEMORY_READ_FAILED" } && process.IsRunning is true)
                {
                    MarkerProfileStore.InvalidateAfterReadFailure(profilePath, error.Message);
                    throw new QaTelemetryException("QA_MARKER_PROFILE_INVALIDATED", "A memory read failed while the exact QA process was still running. The marker profile has been invalidated and the trace will not be sealed.", error);
                }
                throw new QaTelemetryException("QA_TELEMETRY_SAMPLING_FAILED", "Read-only sampling stopped; the trace was not sealed.", error);
            }
        }
    }

    private static void SampleInBackground(ReadOnlyGameProcess process, ExactBuildAuditResult audit,
        ConcurrentQueue<CandidateTelemetrySample> samples, ConcurrentQueue<Exception> errors,
        CancellationToken cancellationToken, Func<int> incrementQueue, Action decrementQueue)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                CandidateTelemetrySample sample = CandidateTelemetryReader.Sample(process, process.ModuleBase, audit);
                if (incrementQueue() > MaximumQueuedSamples)
                {
                    decrementQueue();
                    errors.Enqueue(new InvalidDataException("Unwritten sample queue exceeded its safety limit."));
                    return;
                }
                samples.Enqueue(sample);
                if (cancellationToken.WaitHandle.WaitOne(TimeSpan.FromSeconds(1))) return;
            }
        }
        catch (Exception ex)
        {
            errors.Enqueue(ex);
        }
    }

    private static int CheckpointIndex(ConsoleKey key) => key switch
    {
        ConsoleKey.D1 or ConsoleKey.NumPad1 => 0,
        ConsoleKey.D2 or ConsoleKey.NumPad2 => 1,
        ConsoleKey.D3 or ConsoleKey.NumPad3 => 2,
        ConsoleKey.D4 or ConsoleKey.NumPad4 => 3,
        ConsoleKey.D5 or ConsoleKey.NumPad5 => 4,
        ConsoleKey.D6 or ConsoleKey.NumPad6 => 5,
        ConsoleKey.D7 or ConsoleKey.NumPad7 => 6,
        ConsoleKey.D8 or ConsoleKey.NumPad8 => 7,
        ConsoleKey.D9 or ConsoleKey.NumPad9 => 8,
        ConsoleKey.D0 or ConsoleKey.NumPad0 => 9,
        _ => -1
    };

    private static string ReadInterruptionKind()
    {
        Console.WriteLine("Select the interruption explicitly: D=death, W=warp/teleport, L=loading interruption, G=observed sampling gap, Esc=cancel.");
        while (true)
        {
            ConsoleKey key = Console.ReadKey(intercept: true).Key;
            switch (key)
            {
                case ConsoleKey.D: return "OWNER_REPORTED_DEATH_OR_WARP";
                case ConsoleKey.W: return "OWNER_REPORTED_DEATH_OR_WARP";
                case ConsoleKey.L: return "OWNER_REPORTED_LOADING";
                case ConsoleKey.G: return "SAMPLING_GAP";
                case ConsoleKey.Escape: return "OWNER_REPORTED_INTERRUPTION_CANCELLED";
            }
        }
    }

    private static TraceRecord ToRecord(CandidateTelemetrySample sample, MarkerProfileDocument profile, CandidateTelemetrySample? previous)
    {
        int? from = null, to = null;
        string? transitionType = null;
        if (previous?.CandidateAreaId is int oldArea && sample.CandidateAreaId is int newArea && oldArea != newArea)
        {
            from = oldArea;
            to = newArea;
            transitionType = "OWNER_OBSERVED_MAP_CHANGE";
        }
        if (previous is not null && previous.CandidateAreaId == sample.CandidateAreaId && previous.X.HasValue && sample.X.HasValue)
        {
            double dx = sample.X.Value - previous.X.Value;
            double dy = sample.Y!.Value - previous.Y!.Value;
            double dz = sample.Z!.Value - previous.Z!.Value;
            if (Math.Sqrt(dx * dx + dy * dy + dz * dz) > 100)
                transitionType = "UNCLASSIFIED_POSITION_JUMP";
        }
        string? mapAlias = sample.CandidateAreaId is int areaId
            ? profile.OwnerConfirmedMapAreaIds.FirstOrDefault(pair => pair.Value == areaId).Key
            : null;
        return new TraceRecord
        {
            Type = "observation",
            Checkpoint = null,
            OwnerConfirmedCheckpoint = false,
            CandidateMapAreaId = sample.CandidateAreaId,
            ObservedMapId = string.IsNullOrEmpty(mapAlias) ? null : mapAlias,
            PlayerX = sample.X,
            PlayerY = sample.Y,
            PlayerZ = sample.Z,
            EventFlag11410410 = sample.CeaselessFlag11410410,
            TransitionFromAreaId = from,
            TransitionToAreaId = to,
            TransitionType = transitionType,
            SampleTimestampUtc = sample.TimestampUtc.ToString("O"),
            Confidence = "READ_ONLY_SAMPLE"
        };
    }

    private static TraceRecord BuildAnnotation(string checkpoint, DateTimeOffset ownerTime)
    {
        var record = new TraceRecord
        {
            Type = "observation",
            Checkpoint = checkpoint,
            OwnerConfirmedCheckpoint = true,
            OwnerAnnotationTimestampUtc = ownerTime.ToString("O"),
            Confidence = "OWNER_CONSOLE_ANNOTATION"
        };
        if (checkpoint == "EXIT_PROMPT_PRESENT")
        {
            record.ExitPromptPresent = AskYesNo("Was the exit interaction prompt visible? (y/n)");
            record.ExitActionable = AskYesNo("Did the normal in-game interact action successfully pass the exit? (y/n)");
        }
        else if (checkpoint == "EXIT_CROSSED")
        {
            record.TransitionSucceeded = AskYesNo("Did the normal crossing finish without death, warp, or forced teleport? (y/n)");
        }
        else if (checkpoint == "DESTINATION_LANDED")
        {
            record.ValidLanding = AskYesNo("Did the character land alive on visible walkable ground? (y/n)");
            record.LandingIsolated = AskYesNo("Was the landing visibly isolated from the expected route? (y/n)");
        }
        else if (checkpoint is "MAIN_DEMON_RUINS_REACHED" or "LOWER_LAVA_REACHED" or "POST_RELOAD_EXIT_VERIFIED")
        {
            record.RouteMilestoneReached = AskYesNo("Did you physically reach and confirm this checkpoint in game? (y/n)");
        }
        return record;
    }

    private static void CopyLatestObservedState(TraceRecord annotation, CandidateTelemetrySample? latest, TraceRecord? latestTransition)
    {
        CandidateTelemetrySample? sample = latest;
        if (sample is null) return;
        annotation.CandidateMapAreaId = sample.CandidateAreaId;
        annotation.PlayerX = sample.X;
        annotation.PlayerY = sample.Y;
        annotation.PlayerZ = sample.Z;
        annotation.EventFlag11410410 = sample.CeaselessFlag11410410;
        annotation.SampleTimestampUtc = sample.TimestampUtc.ToString("O");
        bool recentTransition = latestTransition?.SampleTimestampUtc is string transitionTime &&
            DateTimeOffset.TryParse(transitionTime, out DateTimeOffset transitionAt) &&
            DateTimeOffset.TryParse(annotation.OwnerAnnotationTimestampUtc, out DateTimeOffset markedAt) &&
            (markedAt - transitionAt).Duration() <= TimeSpan.FromSeconds(60);
        if (annotation.Checkpoint == "EXIT_CROSSED" && recentTransition && latestTransition!.TransitionFromAreaId is int source && latestTransition.TransitionToAreaId is int destination)
        {
            annotation.TransitionType = "OWNER_OBSERVED_MAP_CHANGE";
            annotation.TransitionFromAreaId = source;
            annotation.TransitionToAreaId = destination;
        }
    }

    private static bool AskYesNo(string prompt)
    {
        Console.WriteLine(prompt);
        string value = Console.ReadLine()?.Trim().ToLowerInvariant() ?? "";
        return value switch
        {
            "y" or "yes" => true,
            "n" or "no" => false,
            _ => throw new InvalidDataException("Outcome must be explicitly answered y or n.")
        };
    }
}
