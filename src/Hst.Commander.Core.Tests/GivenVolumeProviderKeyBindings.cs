namespace Hst.Commander.Core.Tests;

public class GivenVolumeProviderKeyBindings
{
    [Fact]
    public void When_GetDefault_Then_FunctionKeysAreReturned() =>
        Assert.Equal(VolumeProviderKeys.FunctionKeys, VolumeProviderKeyBindings.Default);

    [Theory]
    [InlineData(VolumeProviderKeys.FunctionKeys, "F1", "F2", "F1/F2")]
    [InlineData(VolumeProviderKeys.AltFunctionKeys, "Alt+F1", "Alt+F2", "Alt+F1/Alt+F2")]
    [InlineData(VolumeProviderKeys.ShiftFunctionKeys, "Shift+F1", "Shift+F2", "Shift+F1/Shift+F2")]
    public void When_GetKeyTexts_Then_TextsAreReturned(VolumeProviderKeys keys, string left, string right,
        string name)
    {
        Assert.Equal(left, VolumeProviderKeyBindings.GetLeftKeyText(keys));
        Assert.Equal(right, VolumeProviderKeyBindings.GetRightKeyText(keys));
        Assert.Equal(name, VolumeProviderKeyBindings.GetName(keys));
    }
}
