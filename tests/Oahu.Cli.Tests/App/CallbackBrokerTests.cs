using System;
using System.IO;
using System.Threading.Tasks;
using Oahu.Cli.App.Auth;
using Xunit;

namespace Oahu.Cli.Tests.App;

public class CallbackBrokerTests
{
    [Fact]
    public async Task Stdin_Broker_Pipes_Mfa_Code_Trimmed()
    {
        using var input = new StringReader("  123456  \n");
        var output = new StringWriter();
        var broker = new StdinCallbackBroker(input, output, interactive: true);

        var code = await broker.SolveMfaAsync(new MfaChallenge(), default);
        Assert.Equal("123456", code);
        Assert.Contains("Enter MFA code", output.ToString());
    }

    [Fact]
    public async Task Stdin_Broker_External_Login_Returns_Pasted_Uri()
    {
        var login = new ExternalLoginChallenge(new Uri("https://amazon.example/login"));
        using var input = new StringReader("https://audible.example/maplanding?code=abc\n");
        var broker = new StdinCallbackBroker(input, new StringWriter(), interactive: true);

        var uri = await broker.CompleteExternalLoginAsync(login, default);
        Assert.Equal("audible.example", uri.Host);
    }

    [Fact]
    public async Task Stdin_Broker_External_Login_Throws_On_Invalid_Url()
    {
        var login = new ExternalLoginChallenge(new Uri("https://amazon.example/login"));
        using var input = new StringReader("not a url\n");
        var broker = new StdinCallbackBroker(input, new StringWriter(), interactive: true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => broker.CompleteExternalLoginAsync(login, default));
    }

    [Fact]
    public async Task Non_Interactive_Stdin_Throws_NonInteractiveCallbackException()
    {
        using var input = new StringReader(string.Empty);
        var broker = new StdinCallbackBroker(input, new StringWriter(), interactive: false);
        var ex = await Assert.ThrowsAsync<NonInteractiveCallbackException>(
            () => broker.SolveCaptchaAsync(new CaptchaChallenge(new byte[] { 1 }), default));
        Assert.Equal("captcha", ex.Kind);
    }

    [Fact]
    public async Task NonInteractive_Broker_Always_Throws()
    {
        var broker = new NonInteractiveCallbackBroker();
        await Assert.ThrowsAsync<NonInteractiveCallbackException>(() => broker.SolveMfaAsync(new MfaChallenge(), default));
        await Assert.ThrowsAsync<NonInteractiveCallbackException>(() => broker.SolveCvfAsync(new CvfChallenge(), default));
        await Assert.ThrowsAsync<NonInteractiveCallbackException>(() => broker.ConfirmApprovalAsync(new ApprovalChallenge(), default));
    }
}
