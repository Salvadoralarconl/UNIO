using Clary.Api;
using Clary.Api.LocalIdentity;
using Xunit;

public class HostedOriginTests
{
    [Fact]
    public async Task PublicHttpsBootstrapAndRecoveryKeepExplicitOriginAndPrivateFile() => await IdentityFixture.Run(async f =>
    {
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Brie", "hosting-test-"+Guid.NewGuid().ToString("N"));
        var setup = Path.Combine(directory,"setup.txt"); var recovery = Path.Combine(directory,"recovery.txt");
        try
        {
            var bootstrap = new LocalOperatorBootstrap(f.Database,"https://unio.getunio.dev");
            var owner = await bootstrap.InitializeAsync(setup);
            Assert.StartsWith("https://unio.getunio.dev/setup-account#token=", (await File.ReadAllTextAsync(setup)).Trim());
            Assert.Equal(owner,await bootstrap.RecoverAsync(recovery));
            Assert.StartsWith("https://unio.getunio.dev/setup-account#token=", (await File.ReadAllTextAsync(recovery)).Trim());
            await Assert.ThrowsAsync<InvalidOperationException>(()=>bootstrap.InitializeAsync(Path.Combine(directory,"second.txt")));
        }
        finally { if(Directory.Exists(directory))Directory.Delete(directory,true); }
    });

    [Theory]
    [InlineData("http://unio.getunio.dev")]
    [InlineData("https://user:password@unio.getunio.dev")]
    [InlineData("https://unio.getunio.dev/path")]
    [InlineData("https://unio.getunio.dev?return=other")]
    [InlineData("https://unio.getunio.dev#fragment")]
    public async Task BootstrapRejectsUnsafeOrigins(string origin) => await IdentityFixture.Run(async f =>
    {
        var file=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Brie","hosting-test-"+Guid.NewGuid()+".txt");
        await Assert.ThrowsAsync<ArgumentException>(()=>new LocalOperatorBootstrap(f.Database,origin).InitializeAsync(file));
        Assert.False(File.Exists(file));
        Assert.Equal(0L,await f.Scalar("SELECT count(*) FROM operator_owner"));
    });
}
