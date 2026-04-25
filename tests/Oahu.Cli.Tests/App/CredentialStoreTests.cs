using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Oahu.Cli.App.Credentials;
using Xunit;

namespace Oahu.Cli.Tests.App;

public class CredentialStoreTests
{
    [Fact]
    public void Factory_Returns_Platform_Appropriate_Store()
    {
        using var tempDir = new TempDir();
        var store = CredentialStoreFactory.Create(tempDir.Path);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Assert.Equal("dpapi", store.Provider);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Assert.Equal("keychain", store.Provider);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            Assert.Equal("secret-tool", store.Provider);
        }
    }

    [Fact]
    public async Task Unsupported_Store_Always_Throws_With_Reason()
    {
        var store = new UnsupportedCredentialStore("test reason");
        Assert.Equal("unsupported", store.Provider);
        var ex = await Assert.ThrowsAsync<CredentialStoreUnavailableException>(() => store.GetAsync("acct"));
        Assert.Contains("test reason", ex.Message);
        await Assert.ThrowsAsync<CredentialStoreUnavailableException>(() => store.SetAsync("acct", "x"));
        await Assert.ThrowsAsync<CredentialStoreUnavailableException>(() => store.DeleteAsync("acct"));
        await Assert.ThrowsAsync<CredentialStoreUnavailableException>(() => store.ListAccountsAsync());
    }

#pragma warning disable CA1416
    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public async Task WindowsDpapi_Round_Trip()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }
        using var tempDir = new TempDir();
        var path = Path.Combine(tempDir.Path, "creds.dpapi");
        var store = new WindowsDpapiCredentialStore(path);
        await store.SetAsync("alice", "s3cret");
        Assert.Equal("s3cret", await store.GetAsync("alice"));
        Assert.True(await store.DeleteAsync("alice"));
        Assert.Null(await store.GetAsync("alice"));
        Assert.False(File.Exists(path + ".tmp"));
    }
#pragma warning restore CA1416

    private sealed class TempDir : IDisposable
    {
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"oahu-cli-creds-{Guid.NewGuid():n}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }
}
