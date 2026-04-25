using System;
using System.Threading.Tasks;
using Oahu.Cli.App.Auth;
using Oahu.Cli.App.Models;
using Oahu.CommonTypes;
using Xunit;

namespace Oahu.Cli.Tests.App;

public class CoreAuthRegionMappingTests
{
    [Theory]
    [InlineData(CliRegion.Us, ERegion.Us)]
    [InlineData(CliRegion.Uk, ERegion.Uk)]
    [InlineData(CliRegion.De, ERegion.De)]
    [InlineData(CliRegion.Fr, ERegion.Fr)]
    [InlineData(CliRegion.It, ERegion.It)]
    [InlineData(CliRegion.Es, ERegion.Es)]
    [InlineData(CliRegion.Jp, ERegion.Jp)]
    [InlineData(CliRegion.Au, ERegion.Au)]
    [InlineData(CliRegion.Ca, ERegion.Ca)]
    [InlineData(CliRegion.In, ERegion.In)]
    [InlineData(CliRegion.Br, ERegion.Br)]
    public void Region_Maps_Both_Directions(CliRegion cli, ERegion core)
    {
        Assert.Equal(core, CoreAuthService.ToCoreRegion(cli));
        Assert.Equal(cli, CoreAuthService.ToCliRegion(core));
    }

    [Fact]
    public void All_Cli_Regions_Round_Trip()
    {
        foreach (CliRegion r in Enum.GetValues<CliRegion>())
        {
            var roundTripped = CoreAuthService.ToCliRegion(CoreAuthService.ToCoreRegion(r));
            Assert.Equal(r, roundTripped);
        }
    }
}

public class CallbackBridgeTests
{
    [Fact]
    public async Task Bridge_Forwards_Mfa_Through_Broker()
    {
        var broker = new RecordingBroker { MfaAnswer = "987654" };
        var callbacks = CallbackBridge.ToCoreCallbacks(broker, default);

        // Run the synchronous callback off the test thread to mirror how Core
        // invokes it (typically inside Task.Run).
        var code = await Task.Run(() => callbacks.MfaCallback());
        Assert.Equal("987654", code);
        Assert.Equal(1, broker.MfaCalls);
    }

    [Fact]
    public async Task Bridge_Forwards_External_Login_Uri()
    {
        var loginUri = new Uri("https://amazon.example/login");
        var redirect = new Uri("https://audible.example/maplanding?code=xyz");
        var broker = new RecordingBroker { ExternalLoginAnswer = redirect };
        var callbacks = CallbackBridge.ToCoreCallbacks(broker, default);

        var result = await Task.Run(() => callbacks.ExternalLoginCallback(loginUri));
        Assert.Equal(redirect, result);
        Assert.Equal(loginUri, broker.LastExternalLoginUri);
    }

    [Fact]
    public void Bridge_Always_Confirms_Deregister_Of_Previous_Device()
    {
        var broker = new RecordingBroker();
        var callbacks = CallbackBridge.ToCoreCallbacks(broker, default);

        // Cli has no UI for "confirm de-register"; bridge default = true.
        var confirmed = callbacks.DeregisterDeviceConfirmCallback(
            new Oahu.Core.ProfileKeyEx(0u, ERegion.Us, "name", "acct", "device"));
        Assert.True(confirmed);
    }

    [Fact]
    public async Task Bridge_Propagates_NonInteractive_Exception()
    {
        var broker = new RecordingBroker { ThrowNonInteractive = true };
        var callbacks = CallbackBridge.ToCoreCallbacks(broker, default);

        var ex = await Assert.ThrowsAsync<NonInteractiveCallbackException>(
            () => Task.Run(() => callbacks.MfaCallback()));
        Assert.Equal("mfa", ex.Kind);
    }

    private sealed class RecordingBroker : IAuthCallbackBroker
    {
        public string? CaptchaAnswer { get; init; }
        public string MfaAnswer { get; init; } = "000000";
        public string CvfAnswer { get; init; } = "0000";
        public Uri ExternalLoginAnswer { get; init; } = new Uri("https://example.org/");
        public bool ThrowNonInteractive { get; init; }

        public int MfaCalls { get; private set; }
        public Uri? LastExternalLoginUri { get; private set; }

        public Task<string> SolveCaptchaAsync(CaptchaChallenge challenge, System.Threading.CancellationToken cancellationToken)
            => ThrowNonInteractive
                ? Task.FromException<string>(new NonInteractiveCallbackException("captcha"))
                : Task.FromResult(CaptchaAnswer ?? string.Empty);

        public Task<string> SolveMfaAsync(MfaChallenge challenge, System.Threading.CancellationToken cancellationToken)
        {
            MfaCalls++;
            return ThrowNonInteractive
                ? Task.FromException<string>(new NonInteractiveCallbackException("mfa"))
                : Task.FromResult(MfaAnswer);
        }

        public Task<string> SolveCvfAsync(CvfChallenge challenge, System.Threading.CancellationToken cancellationToken)
            => Task.FromResult(CvfAnswer);

        public Task ConfirmApprovalAsync(ApprovalChallenge challenge, System.Threading.CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<Uri> CompleteExternalLoginAsync(ExternalLoginChallenge challenge, System.Threading.CancellationToken cancellationToken)
        {
            LastExternalLoginUri = challenge.LoginUri;
            return Task.FromResult(ExternalLoginAnswer);
        }
    }
}
