using System.Diagnostics;

namespace SemaRepair.IntegrationTests;

// Shared configuration + helpers for the integration harness. These tests hit
// the REAL running Docker stack over HTTP (through nginx on :80, exactly as the
// manual verification did) against the real seeded Postgres and real Gemini -
// the whole point is catching regressions in real behaviour, not in mocks.
//
// Precondition: the stack must be up (`docker compose up -d`) and reachable at
// BaseUrl. Overridable via env for a differently-hosted stack.
public static class TestEnv
{
    public static string BaseUrl =>
        Environment.GetEnvironmentVariable("SEMAREPAIR_BASE_URL") ?? "http://localhost";

    // Container whose logs carry the search-service diagnostics (the boundary-tie
    // re-query line). Overridable if the compose project name differs.
    public static string SearchContainer =>
        Environment.GetEnvironmentVariable("SEMAREPAIR_SEARCH_CONTAINER") ?? "semarepair_v2-search-service-1";

    // One shared client - Gemini-backed chat turns take a few seconds, so the
    // timeout is generous.
    public static readonly HttpClient Http = new() { BaseAddress = new Uri(BaseUrl), Timeout = TimeSpan.FromSeconds(90) };

    // Returns the search-service container log text emitted since the given Unix
    // timestamp (seconds). Used to observe the boundary-tie re-query line, which
    // is emitted ONLY when the epsilon re-query fires - a faithful signal of the
    // §5 fix, since the no-car symptom endpoint exposes only car identities
    // (Rule 1) and can't surface the tied document ids directly.
    public static string SearchLogsSince(long unixSeconds)
    {
        var psi = new ProcessStartInfo("docker", $"logs --since {unixSeconds} {SearchContainer}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        // docker logs writes app logs to stderr for these services; capture both.
        var stdout = p.StandardOutput.ReadToEnd();
        var stderr = p.StandardError.ReadToEnd();
        p.WaitForExit(15000);
        return stdout + stderr;
    }

    public static long NowUnix() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public static string PostgresContainer =>
        Environment.GetEnvironmentVariable("SEMAREPAIR_PG_CONTAINER") ?? "semarepair_v2-our-postgres-1";

    // Runs a scalar query against the seeded database, through docker exec -
    // same shape as SearchLogsSince above, and for the same reason: the
    // harness already assumes a running compose stack, so shelling into it is
    // cheaper and less brittle than adding a database driver and a second
    // source of connection configuration.
    //
    // Exists so a test can state the dataset it needs instead of failing when
    // that dataset is swapped. The documents behind BoundaryTieTests came from
    // a multi-vehicle sample; the FI0396 delivery does not contain them.
    public static string Scalar(string sql)
    {
        var psi = new ProcessStartInfo("docker",
            $"exec {PostgresContainer} sh -c \"psql -U \\$POSTGRES_USER -d \\$POSTGRES_DB -t -A -c '{sql}'\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit(15000);
        return stdout.Trim();
    }

    public static bool DocumentsExist(params string[] ids)
    {
        // Doubled single quotes: the whole psql -c argument is already inside
        // single quotes in the docker exec line above. These ids are test
        // constants, never user input.
        var literals = string.Join(",", ids.Select(i => $"''{i}''"));
        var sql = $"SELECT count(DISTINCT id_documento) FROM documents WHERE id_documento IN ({literals})";
        return int.TryParse(Scalar(sql), out var n) && n == ids.Length;
    }
}
