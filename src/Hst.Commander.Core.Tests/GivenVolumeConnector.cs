using Hst.Commander.Abstractions;

namespace Hst.Commander.Core.Tests;

public class GivenVolumeConnector
{
    /// <summary>Location volume provider accepting only "user"/"secret", recording the credentials it's given.</summary>
    private sealed class TestLocationVolumeProvider : ILocationVolumeProvider
    {
        public string Name => "TestLocation";

        public string Description => "Test locations";

        public string LocationLabel => "Test path";

        public string LocationExample => "server";

        public List<VolumeCredentials?> Attempts { get; } = [];

        public Task<IVolume> CreateVolume(CancellationToken cancellationToken = default) =>
            throw new IOException("Enter a location");

        public Task<VolumeConnection> Connect(string location, VolumeCredentials? credentials,
            CancellationToken cancellationToken = default)
        {
            Attempts.Add(credentials);
            return credentials == new VolumeCredentials("user", "secret")
                ? Task.FromResult(new VolumeConnection(new TestVolumeProvider(_ => { }), ["dir"]))
                : throw new CredentialsRequiredException(credentials == null ? "Needs credentials" : "Wrong password");
        }
    }

    private static void Ignore(string message)
    {
    }

    private static void Ignore()
    {
    }

    [Fact]
    public async Task When_CredentialsAreRejected_Then_TheyAreAskedForAgain()
    {
        // arrange
        var volumeProvider = new TestLocationVolumeProvider();
        var requests = new List<CredentialsRequest>();
        var answers = new Queue<VolumeCredentials>([new("user", "wrong"), new("user", "secret")]);

        // act
        var connection = await VolumeConnector.ConnectAsync(volumeProvider, "  server  ", request =>
        {
            requests.Add(request);
            return Task.FromResult<VolumeCredentials?>(answers.Dequeue());
        }, Ignore, Ignore);

        // assert
        Assert.NotNull(connection);
        Assert.Equal(["dir"], connection.PathComponents);
        Assert.Equal(3, volumeProvider.Attempts.Count);
        Assert.Null(volumeProvider.Attempts[0]);
        Assert.Equal(
        [
            new CredentialsRequest("TestLocation", "server", "Needs credentials", string.Empty),
            new CredentialsRequest("TestLocation", "server", "Wrong password", "user")
        ], requests);
        Assert.Equal("server", VolumeConnector.GetLastLocation(volumeProvider));
    }

    [Fact]
    public async Task When_CredentialsDialogIsCancelled_Then_NullIsReturned()
    {
        // arrange
        var volumeProvider = new TestLocationVolumeProvider();

        // act
        var connection = await VolumeConnector.ConnectAsync(volumeProvider, "server",
            _ => Task.FromResult<VolumeCredentials?>(null), Ignore, Ignore);

        // assert
        Assert.Null(connection);
        Assert.Single(volumeProvider.Attempts);
    }

    [Fact]
    public void When_FormattingCredentials_Then_PasswordIsHidden() =>
        Assert.DoesNotContain("secret", new VolumeCredentials("user", "secret").ToString());
}
