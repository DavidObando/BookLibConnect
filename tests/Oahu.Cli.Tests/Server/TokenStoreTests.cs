using System.IO;
using System.Runtime.InteropServices;
using Oahu.Cli.Server.Auth;
using Xunit;

namespace Oahu.Cli.Tests.Server;

public sealed class TokenStoreTests
{
    [Fact]
    public void ReadOrCreate_Generates_Then_Reuses_Token()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oahu-token-{System.Guid.NewGuid():n}");
        try
        {
            var store = new TokenStore(path);
            var first = store.ReadOrCreate();
            Assert.False(string.IsNullOrWhiteSpace(first));
            Assert.True(File.Exists(path));
            var second = store.ReadOrCreate();
            Assert.Equal(first, second);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Rotate_Replaces_Token()
    {
        var path = Path.Combine(Path.GetTempPath(), $"oahu-token-{System.Guid.NewGuid():n}");
        try
        {
            var store = new TokenStore(path);
            var first = store.ReadOrCreate();
            var second = store.Rotate();
            Assert.NotEqual(first, second);
            Assert.Equal(second, store.ReadOrCreate());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Token_File_Has_Restrictive_Mode_On_Unix()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }
        var path = Path.Combine(Path.GetTempPath(), $"oahu-token-{System.Guid.NewGuid():n}");
        try
        {
            new TokenStore(path).ReadOrCreate();
            var mode = File.GetUnixFileMode(path);
            const UnixFileMode forbidden = UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                                           UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            Assert.Equal((UnixFileMode)0, mode & forbidden);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode & (UnixFileMode.UserRead | UnixFileMode.UserWrite));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void ReadOrCreate_Refuses_Loose_Mode()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }
        var path = Path.Combine(Path.GetTempPath(), $"oahu-token-{System.Guid.NewGuid():n}");
        try
        {
            File.WriteAllText(path, "abcdef");
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
            Assert.Throws<System.InvalidOperationException>(() => new TokenStore(path).ReadOrCreate());
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData("abc", "abc", true)]
    [InlineData("abc", "abd", false)]
    [InlineData("a", "ab", false)]
    [InlineData(null, "abc", false)]
    public void Equal_Constant_Time_Compare(string? a, string? b, bool expected)
    {
        Assert.Equal(expected, TokenStore.Equal(a, b));
    }
}
