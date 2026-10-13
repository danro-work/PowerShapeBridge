using Xunit;

namespace PowerShapeMcp.Tests;

// Keep the subsecond admission/deadline probes away from parallel fsync/archive
// fixtures. Their original budgets and production timeout behavior stay intact.
[CollectionDefinition("Timing-sensitive IPC",DisableParallelization=true)]
public sealed class TimingSensitiveIpcCollection { }
