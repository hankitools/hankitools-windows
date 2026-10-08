namespace IgezziGuard;

public static class PagefileAdvisor
{
    public static string Explain(ulong used, ulong limit, ulong peak, ulong available, ulong ram)
    {
        if (limit == 0 || ram == 0) return "PAGEFILE ADVISOR: counters unavailable; no recommendation can be derived.";
        var headroom = limit > used ? limit - used : 0;
        var pressure = (double)used / limit;
        return "PAGEFILE ADVISOR\r\n" +
            $"Current commit headroom: {headroom / (1024d * 1024 * 1024):0.00} GiB. Available physical RAM: {available / (1024d * 1024 * 1024):0.00} GiB.\r\n" +
            (pressure >= .9 ? "Review now: current commit is at least 90% of its limit. Inspect high-memory apps and free space on the pagefile drive; check whether Windows can grow the pagefile.\r\n" :
                "This snapshot does not justify increasing the pagefile by itself. Keep measuring during your heaviest normal workload.\r\n") +
            (peak > ram ? "Peak commit since boot exceeded usable physical RAM. Do not disable the pagefile on the assumption that RAM alone covers that demand.\r\n" :
                "Peak commit since boot has not exceeded usable physical RAM; this is NOT evidence that disabling the pagefile is safe.\r\n") +
            "Recommendation: system-managed sizing is the starting point unless your administrator or workload requires a custom setup. Review the configured entries and drive free space below.\r\n" +
            "No exact custom size is recommended: representative peak demand, growth margin and crash-dump requirements have not all been established. Peak since boot may predate a change to the current commit limit. Commit is not pagefile usage, and increasing a pagefile does not itself increase FPS.\r\n";
    }
}
