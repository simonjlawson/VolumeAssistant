using VolumeAssistant.Service.Matter.Clusters;

namespace VolumeAssistant.Tests;

/// <summary>
/// Tests for Matter cluster implementations.
/// </summary>
public class ClusterTests
{
    // ── Level Control Cluster ──────────────────────────────────────────────────

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(100f, 254)]
    [InlineData(50f, 127)]
    [InlineData(25f, 64)]
    public void LevelControl_VolumePercentToMatterLevel_ConvertsCorrectly(
        float volumePercent, byte expectedLevel)
    {
        byte result = LevelControlCluster.VolumePercentToMatterLevel(volumePercent);
        Assert.Equal(expectedLevel, result);
    }

    [Theory]
    [InlineData(0, 0f)]
    [InlineData(254, 100f)]
    [InlineData(127, 50f)]
    public void LevelControl_MatterLevelToVolumePercent_ConvertsCorrectly(
        byte matterLevel, float expectedPercent)
    {
        float result = LevelControlCluster.MatterLevelToVolumePercent(matterLevel);
        Assert.Equal(expectedPercent, result, precision: 1);
    }

    [Fact]
    public void LevelControl_VolumePercentToMatterLevel_ClampsAbove100()
    {
        byte result = LevelControlCluster.VolumePercentToMatterLevel(150f);
        Assert.Equal(254, result);
    }

    [Fact]
    public void LevelControl_VolumePercentToMatterLevel_ClampsBelow0()
    {
        byte result = LevelControlCluster.VolumePercentToMatterLevel(-10f);
        Assert.Equal(0, result);
    }

    [Fact]
    public void LevelControl_ReadAttribute_CurrentLevel_ReturnsCurrentLevel()
    {
        var cluster = new LevelControlCluster { CurrentLevel = 128 };
        object? value = cluster.ReadAttribute(0x0000); // CurrentLevel attribute
        Assert.Equal((byte?)128, value);
    }

    [Fact]
    public void LevelControl_ReadAttribute_MinLevel_Returns0()
    {
        var cluster = new LevelControlCluster();
        object? value = cluster.ReadAttribute(0x0002); // MinLevel attribute
        Assert.Equal((byte)0, value);
    }

    [Fact]
    public void LevelControl_ReadAttribute_MaxLevel_Returns254()
    {
        var cluster = new LevelControlCluster();
        object? value = cluster.ReadAttribute(0x0003); // MaxLevel attribute
        Assert.Equal((byte)254, value);
    }

    [Fact]
    public void LevelControl_InvokeMoveToLevel_UpdatesCurrentLevel()
    {
        var cluster = new LevelControlCluster();
        byte? result = cluster.InvokeMoveToLevel(200);
        Assert.Equal((byte)200, result);
        Assert.Equal((byte)200, cluster.CurrentLevel);
    }

    [Fact]
    public void LevelControl_CurrentLevelChanged_EventFired()
    {
        var cluster = new LevelControlCluster();
        byte? changedLevel = null;
        cluster.CurrentLevelChanged += (_, level) => changedLevel = level;

        cluster.CurrentLevel = 100;

        Assert.Equal((byte)100, changedLevel);
    }

    [Fact]
    public void LevelControl_CurrentLevelChanged_NotFiredWhenSameValue()
    {
        var cluster = new LevelControlCluster { CurrentLevel = 50 };
        int eventCount = 0;
        cluster.CurrentLevelChanged += (_, _) => eventCount++;

        cluster.CurrentLevel = 50; // Same value

        Assert.Equal(0, eventCount);
    }

    // ── On/Off Cluster ────────────────────────────────────────────────────────

    [Fact]
    public void OnOff_DefaultOnOff_IsTrue()
    {
        var cluster = new OnOffCluster();
        Assert.True(cluster.OnOff);
    }

    [Fact]
    public void OnOff_InvokeOff_SetsOnOffFalse()
    {
        var cluster = new OnOffCluster();
        bool result = cluster.InvokeCommand(OnOffCluster.CmdOff);
        Assert.True(result);
        Assert.False(cluster.OnOff);
    }

    [Fact]
    public void OnOff_InvokeOn_SetsOnOffTrue()
    {
        var cluster = new OnOffCluster();
        cluster.OnOff = false;
        bool result = cluster.InvokeCommand(OnOffCluster.CmdOn);
        Assert.True(result);
        Assert.True(cluster.OnOff);
    }

    [Fact]
    public void OnOff_InvokeToggle_TogglesState()
    {
        var cluster = new OnOffCluster();
        cluster.InvokeCommand(OnOffCluster.CmdToggle);
        Assert.False(cluster.OnOff);
        cluster.InvokeCommand(OnOffCluster.CmdToggle);
        Assert.True(cluster.OnOff);
    }

    [Fact]
    public void OnOff_OnOffChanged_EventFired()
    {
        var cluster = new OnOffCluster();
        bool? changedValue = null;
        cluster.OnOffChanged += (_, value) => changedValue = value;

        cluster.OnOff = false;

        Assert.False(changedValue);
    }

    [Fact]
    public void OnOff_ReadAttribute_OnOff_ReturnsCurrentState()
    {
        var cluster = new OnOffCluster();
        cluster.OnOff = false;
        object? value = cluster.ReadAttribute(0x0000);
        Assert.Equal(false, value);
    }

    [Fact]
    public void OnOff_WriteAttribute_OnOff_UpdatesState()
    {
        var cluster = new OnOffCluster();
        bool result = cluster.WriteAttribute(0x0000, false);
        Assert.True(result);
        Assert.False(cluster.OnOff);
    }

    // ── Basic Information Cluster ────────────────────────────────────────────

    [Fact]
    public void BasicInfo_ReadAttribute_VendorName_ReturnsVendorName()
    {
        var cluster = new BasicInformationCluster();
        object? value = cluster.ReadAttribute(0x0001);
        Assert.Equal("VolumeAssistant", value);
    }

    [Fact]
    public void BasicInfo_ReadAttribute_ProductName_ReturnsProductName()
    {
        var cluster = new BasicInformationCluster();
        object? value = cluster.ReadAttribute(0x0003);
        Assert.IsType<string>(value);
        Assert.NotEmpty((string)value!);
    }

    [Fact]
    public void BasicInfo_WriteAttribute_NodeLabel_UpdatesLabel()
    {
        var cluster = new BasicInformationCluster();
        bool result = cluster.WriteAttribute(0x0005, "My Device");
        Assert.True(result);
        Assert.Equal("My Device", cluster.NodeLabel);
    }

    [Fact]
    public void BasicInfo_WriteAttribute_ReadOnly_ReturnsFalse()
    {
        var cluster = new BasicInformationCluster();
        bool result = cluster.WriteAttribute(0x0001, "NewVendor"); // VendorName is read-only
        Assert.False(result);
    }
}
