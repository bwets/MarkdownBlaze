// Some tests change the process's current directory or write under a test-only registry key; the
// whole suite is quick, so nothing runs in parallel and none of them can disturb another.

[assembly: CollectionBehavior(DisableTestParallelization = true)]
