using IgezziGuard;

internal static class UsabilityChecks
{
    internal static void Run()
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); }
        var history = new NavigationHistory();
        history.Visit("Diagnose  /  Guided checks");
        history.Visit("Connect  /  Basic checks");
        history.Visit("Connect  /  Basic checks");
        Check(history.Back() == "Diagnose  /  Guided checks", "Back must return to the exact originating tool, without duplicate visits.");
        Check(history.Back() == "Home" && history.Back() is null, "Back must stop at the start without creating a loop.");
        history.Visit("History"); history.Visit("Recovery"); history.Back(); history.Visit("Help");
        Check(history.Back() == "History" && history.Back() == "Home", "Opening a new route after Back must preserve the traversed path.");

        var now = DateTimeOffset.UtcNow;
        DiagnosticResult Result(CollectionOutcome outcome, FindingSeverity severity) => new("storage", "capacity", DiagnosticCategory.Storage,
            outcome, severity, "Storage", "Example", now, now);
        var healthy = Result(CollectionOutcome.Completed, FindingSeverity.Healthy);
        var unavailable = Result(CollectionOutcome.Unavailable, FindingSeverity.Unknown);
        var warning = Result(CollectionOutcome.Partial, FindingSeverity.Warning);
        Check(!ScanPresentation.NeedsAttention(unavailable) && ScanPresentation.HasGap(unavailable), "Missing evidence is not a diagnosed fault.");
        Check(ScanPresentation.NeedsAttention(warning) && ScanPresentation.HasGap(warning), "Partial findings must retain both warning and coverage gap.");
        Check(!ScanPresentation.HasGap(healthy) && !ScanPresentation.NeedsAttention(healthy), "Healthy checks belong in the collapsed other checks.");
        var scan = new DiagnosticScan(Guid.NewGuid(), now, now, 2, 2, false, [healthy, unavailable]);
        Check(ScanPresentation.Headline(scan).Contains("incomplete"), "A completed collection with unavailable checks must not claim everything passed.");
        Check(ScanPresentation.Headline(scan with { Cancelled = true, Results = [] }).Contains("incomplete"), "Cancelled empty scans must not claim a clean bill of health.");
        Check(ScanPresentation.Headline(scan with { Results = [healthy, warning] }).Contains("1"), "Only actionable findings count toward attention.");

        var connected = new ConnectionFacts(1, "Wi-Fi", true, true, false, true, 20, true, 30, true, 40);
        Check(InternetJourney.Evaluate(connected).Status == CardStatus.Info, "Passing probes must still ask the user to verify their symptom.");
        Check(InternetJourney.Evaluate(connected with { ActiveAdapters = 0 }).Headline.Contains("No network"), "Disconnected adapters get a connection step.");
        Check(InternetJourney.Evaluate(connected with { DnsWorks = false, NameReachable = null }).FollowUpRoute.Contains("DNS repair"), "DNS failure with a responding IP offers DNS investigation.");
        Check(InternetJourney.Evaluate(connected with { DnsWorks = null, IpReachable = null, NameReachable = null }).Status == CardStatus.Unknown, "Missing tests must remain unknown.");
        var blocked = InternetJourney.Evaluate(connected with { IpReachable = false, NameReachable = false });
        Check(blocked.Headline.Contains("did not answer") && !blocked.Instruction.Contains("PC reaches the router"), "A configured gateway is not evidence the router responded.");
        Check(InternetJourney.Evaluate(connected with { HasIPv4 = false, HasGateway = false }).Headline.Contains("passed"), "Working IPv6 must not be diagnosed as disconnected.");
        Check(InternetJourney.Evaluate(connected with { HasGateway = false, VpnActive = true, IpReachable = false, NameReachable = false }).Headline.Contains("did not answer"), "VPN routes must not be mistaken for a missing physical gateway.");
    }
}
