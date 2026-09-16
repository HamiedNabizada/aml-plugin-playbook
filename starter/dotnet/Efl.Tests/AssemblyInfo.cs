using Xunit;

// Aml.Engine keeps process-wide caches (class path resolution, the loaded
// library cache). Tests that build documents in parallel then see each other's
// libraries now and then, and fail in a way that does not reproduce on a rerun.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
