using TechHub.Infrastructure.Updates;

namespace TechHub.Tests;

public sealed class MachineUpdateMarkerTests
{
    [Fact]
    public void SharedRunningMarkerSurvivesUntilAllHandlesClose()
    {
        // Test only when no real machine instance has registered its marker.
        Assert.False(MachineUpdateMarker.Exists(MachineUpdateMarker.Running));
        var first = MachineUpdateMarker.Create(MachineUpdateMarker.Running, out var created);
        Assert.True(created);
        try
        {
            using var second = MachineUpdateMarker.Create(MachineUpdateMarker.Running, out var createdAgain);
            Assert.False(createdAgain);
            Assert.True(MachineUpdateMarker.Exists(MachineUpdateMarker.Running));
            first.Dispose();
            Assert.True(MachineUpdateMarker.Exists(MachineUpdateMarker.Running));
        }
        finally { first.Dispose(); }
        Assert.False(MachineUpdateMarker.Exists(MachineUpdateMarker.Running));
    }

    [Fact]
    public void OnlyFixedGlobalNamesAreAccepted()
    {
        Assert.StartsWith("Global\\", MachineUpdateMarker.Updating);
        Assert.StartsWith("Global\\", MachineUpdateMarker.Running);
        Assert.Throws<ArgumentException>(() => MachineUpdateMarker.Create("Local\\Unrelated", out _));
    }
}
