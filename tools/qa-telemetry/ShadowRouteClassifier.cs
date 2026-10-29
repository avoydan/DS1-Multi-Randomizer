namespace QaTelemetry;

public static class ShadowRouteClassifier
{
    public static TraceImportResult Classify(CaseDescriptor descriptor, IReadOnlyList<TraceRecord> records, string traceSha256)
    {
        TraceRecord[] observations = records.Skip(1).ToArray();
        var confirmed = observations.Where(record => record.Checkpoint is not null && record.OwnerConfirmedCheckpoint == true)
            .GroupBy(record => record.Checkpoint!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderBy(record => record.Sequence).ToArray(), StringComparer.Ordinal);
        var edges = new List<ShadowEdgeResult>();
        bool FlagTrueAtOrAfter(string checkpoint) =>
            confirmed.TryGetValue(checkpoint, out TraceRecord[]? marks) &&
            marks.Any(mark => mark.OwnerAnnotationTimestampUtc is not null && observations.Any(record =>
                record.SampleTimestampUtc is not null &&
                DateTimeOffset.TryParse(record.SampleTimestampUtc, out DateTimeOffset sampledAt) &&
                DateTimeOffset.TryParse(mark.OwnerAnnotationTimestampUtc, out DateTimeOffset markedAt) &&
                sampledAt >= markedAt && record.EventFlag11410410 == true &&
                record.EventFlagTrust is "MARKER_VERIFIED" or "SESSION_VERIFIED"));

        bool defeated = FlagTrueAtOrAfter("CEASELESS_DEFEATED");
        edges.Add(new("boss-completion:11410410", defeated ? "GAMEPLAY_CONFIRMED" : "UNKNOWN"));
        bool approach = defeated && confirmed.ContainsKey("EXIT_APPROACH");
        edges.Add(new("post-boss-approach:o4961_0000", approach ? "GAMEPLAY_CONFIRMED" : "UNKNOWN"));

        bool prompt = confirmed.TryGetValue("EXIT_PROMPT_PRESENT", out TraceRecord[]? promptRecords) && promptRecords.Any(x => x.ExitPromptPresent == true && x.ExitActionable == true);
        bool crossed = confirmed.TryGetValue("EXIT_CROSSED", out TraceRecord[]? crossingRecords) && crossingRecords.Any(x =>
            x.TransitionSucceeded == true && x.TransitionType == "OWNER_OBSERVED_MAP_CHANGE" &&
            x.TransitionTrust is "MARKER_VERIFIED" or "SESSION_VERIFIED" &&
            x.TransitionFromAreaId.HasValue && x.TransitionToAreaId.HasValue && x.TransitionFromAreaId != x.TransitionToAreaId);
        bool landed = confirmed.TryGetValue("DESTINATION_LANDED", out TraceRecord[]? landingRecords) && landingRecords.Any(x =>
            x.ValidLanding == true && x.LandingIsolated == false &&
            x.PositionTrust is "MARKER_VERIFIED" or "SESSION_VERIFIED" &&
            x.PlayerX.HasValue && x.PlayerY.HasValue && x.PlayerZ.HasValue &&
            x.MapTrust is "MARKER_VERIFIED" or "SESSION_VERIFIED");
        bool crossingSafe = defeated && prompt && crossed && landed && !HasContinuityBreak(observations);
        string crossingCertainty = crossingSafe ? "GAMEPLAY_CONFIRMED_ACTIONABLE" : promptRecords?.Any(x => x.ExitActionable == false) == true ? "GAMEPLAY_CONFIRMED_NON_ACTIONABLE" : "UNKNOWN";
        edges.Add(new("post-boss-crossing:o4961_0000", crossingCertainty));

        bool arrived = crossingSafe && landingRecords!.Any(x => string.Equals(x.ObservedMapId, "Demon Ruins", StringComparison.Ordinal) &&
            x.MapTrust is "MARKER_VERIFIED" or "SESSION_VERIFIED" && x.PositionTrust is "MARKER_VERIFIED" or "SESSION_VERIFIED");
        edges.Add(new("arrival:main-demon-ruins", arrived ? "GAMEPLAY_CONFIRMED_ACTIONABLE" : "UNKNOWN"));
        bool route = arrived && HasOrderedRoute(confirmed) &&
            confirmed["MAIN_DEMON_RUINS_REACHED"].Any(x => x.RouteMilestoneReached == true) &&
            confirmed["LOWER_LAVA_REACHED"].Any(x => x.RouteMilestoneReached == true) &&
            confirmed["POST_RELOAD_EXIT_VERIFIED"].Any(x => x.RouteMilestoneReached == true) && !HasContinuityBreak(observations);
        edges.Add(new("route:main-to-lower-lava", route ? "GAMEPLAY_CONFIRMED_ACTIONABLE" : "UNKNOWN"));

        string status = route ? "PHYSICALLY_SOLVABLE_GAMEPLAY_CONFIRMED" : "GAMEPLAY_TRACE_INCOMPLETE";
        string explanation = route
            ? "The complete ordered route was owner-confirmed in this exact package trace, with verified map/position/flag samples and no recorded unclassified position discontinuity. This does not modify or certify the production validator."
            : "Only exact-case observations with a verified marker profile may be promoted. Missing owner checkpoints, false/unknown outcomes, map ambiguity, or a continuity break leave the corresponding edge UNKNOWN.";
        return new TraceImportResult(status, route, explanation, edges.ToArray(), observations.Length, traceSha256);
    }

    private static bool HasContinuityBreak(IEnumerable<TraceRecord> observations) => observations.Any(record =>
        record.TransitionType is "UNCLASSIFIED_POSITION_JUMP" or "PROCESS_RESTART" or "SAMPLING_GAP" or
            "OWNER_REPORTED_DEATH_OR_WARP" or "OWNER_REPORTED_LOADING");

    private static bool HasOrderedRoute(Dictionary<string, TraceRecord[]> confirmed)
    {
        string[] required =
        [
            "CEASELESS_PRE_FIGHT", "CEASELESS_BOSS_ACTIVE", "CEASELESS_DEFEATED", "EXIT_APPROACH",
            "EXIT_PROMPT_PRESENT", "EXIT_CROSSED", "DESTINATION_LANDED", "MAIN_DEMON_RUINS_REACHED",
            "LOWER_LAVA_REACHED", "POST_RELOAD_EXIT_VERIFIED"
        ];
        long previous = 0;
        foreach (string checkpoint in required)
        {
            if (!confirmed.TryGetValue(checkpoint, out TraceRecord[]? marks) || !marks.Any(mark => mark.Sequence > previous)) return false;
            previous = marks.Where(mark => mark.Sequence > previous).Min(mark => mark.Sequence);
        }
        return true;
    }
}
