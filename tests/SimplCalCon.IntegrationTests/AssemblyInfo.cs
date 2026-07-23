using Xunit;

// Each test class spins up a full WebApplicationFactory host (EF migration + seeding).
// Running them concurrently starves cold-start migrations under CPU contention and
// makes host startup flaky, so serialize the assembly's tests.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
