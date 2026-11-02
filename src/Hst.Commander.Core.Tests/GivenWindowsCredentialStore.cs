namespace Hst.Commander.Core.Tests;

public class GivenWindowsCredentialStore
{
    [Fact]
    public void When_PasswordIsSavedLoadedAndDeleted_Then_ItRoundTrips()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        // arrange
        var store = new WindowsCredentialStore();
        var id = $"test-{Guid.NewGuid():N}";
        try
        {
            // act
            store.Save(id, "pässwörd 1");
            var loaded = store.Load(id);
            store.Save(id, "changed");
            var changed = store.Load(id);
            store.Delete(id);

            // assert
            Assert.Equal("pässwörd 1", loaded);
            Assert.Equal("changed", changed);
            Assert.Null(store.Load(id));
        }
        finally
        {
            store.Delete(id);
        }
    }
}
