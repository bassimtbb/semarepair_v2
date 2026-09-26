// These tests drive ONE running Docker stack over HTTP, so they are not
// independent of each other and must not run concurrently.
//
// The failure that forced this was a real one and only appeared in a full
// run: RoutingDeterminismTests proves a fault query did NOT reach
// /api/search/technical by scraping search-service logs over a time window.
// With TechnicalSearchTests running in parallel, its own perfectly legitimate
// technical requests landed inside that window, and the trap tests failed
// while passing in isolation.
//
// Scoping the log read per session would be the finer fix, but search-service
// request logs carry no session id, and inventing one to make a test pass
// would be tail-wagging. Parallelism buys little here anyway: the wall time
// is dominated by live Gemini calls, not by CPU.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
