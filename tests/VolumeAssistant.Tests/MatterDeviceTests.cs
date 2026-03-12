using VolumeAssistant.Service.Audio;
using VolumeAssistant.Service.Matter;

namespace VolumeAssistant.Tests;

/// <summary>
/// Tests for the MatterDevice model.
/// </summary>
public class MatterDeviceTests
{
    [Fact]
    public void MatterDevice_HasRootEndpoint0_And_DeviceEndpoint1()
    {
        var device = new MatterDevice();

        Assert.NotNull(device.GetEndpoint(0));
        Assert.NotNull(device.GetEndpoint(1));
    }

    [Fact]
    public void MatterDevice_Endpoint1_HasLevelControlAndOnOffClusters()
    {
        var device = new MatterDevice();
        var endpoint = device.GetEndpoint(1)!;

        Assert.NotNull(endpoint.GetCluster(0x0008)); // LevelControl
        Assert.NotNull(endpoint.GetCluster(0x0006)); // OnOff
    }

    [Fact]
    public void MatterDevice_Endpoint0_HasBasicInformationCluster()
    {
        var device = new MatterDevice();
        var endpoint = device.GetEndpoint(0)!;

        Assert.NotNull(endpoint.GetCluster(0x0028)); // BasicInformation
    }

    [Fact]
    public void MatterDevice_UpdateFromVolume_SetsLevelAndOnOff()
    {
        var device = new MatterDevice();
        device.UpdateFromVolume(50f, isMuted: false);

        byte expectedLevel = (byte)Math.Round(50f / 100f * 254f);
        Assert.Equal(expectedLevel, device.LevelControlCluster.CurrentLevel);
        Assert.True(device.OnOffCluster.OnOff);
    }

    [Fact]
    public void MatterDevice_UpdateFromVolume_MutedSetsOnOffFalse()
    {
        var device = new MatterDevice();
        device.UpdateFromVolume(75f, isMuted: true);

        Assert.False(device.OnOffCluster.OnOff);
    }

    [Fact]
    public void MatterDevice_InstanceName_Is16HexChars()
    {
        var device = new MatterDevice();
        Assert.Equal(16, device.InstanceName.Length);
        Assert.Matches("^[0-9A-F]{16}$", device.InstanceName);
    }

    [Fact]
    public void MatterDevice_Discriminator_IsWithin12BitRange()
    {
        var device = new MatterDevice(discriminator: 3840);
        Assert.InRange(device.Discriminator, 0, 4095);
    }

    [Fact]
    public void MatterDevice_LevelChange_RaisesDeviceStateChanged()
    {
        var device = new MatterDevice();
        (byte Level, bool IsOn)? captured = null;
        device.DeviceStateChanged += (_, state) => captured = state;

        device.LevelControlCluster.CurrentLevel = 200;

        Assert.NotNull(captured);
        Assert.Equal(200, captured!.Value.Level);
    }

    [Fact]
    public void MatterDevice_OnOffChange_RaisesDeviceStateChanged()
    {
        var device = new MatterDevice();
        (byte Level, bool IsOn)? captured = null;
        device.DeviceStateChanged += (_, state) => captured = state;

        device.OnOffCluster.OnOff = false;

        Assert.NotNull(captured);
        Assert.False(captured!.Value.IsOn);
    }
}

/// <summary>
/// Tests for VolumeChangedEventArgs.
/// </summary>
public class VolumeChangedEventArgsTests
{
    [Fact]
    public void VolumeChangedEventArgs_StoresVolumeAndMutedState()
    {
        var args = new VolumeChangedEventArgs(65.5f, isMuted: true);

        Assert.Equal(65.5f, args.VolumePercent);
        Assert.True(args.IsMuted);
    }

    [Fact]
    public void VolumeChangedEventArgs_UnmutedState()
    {
        var args = new VolumeChangedEventArgs(30f, isMuted: false);

        Assert.Equal(30f, args.VolumePercent);
        Assert.False(args.IsMuted);
    }
}
