using System;
using Oahu.Cli.Server.Capabilities;
using Xunit;

namespace Oahu.Cli.Tests.Server;

public sealed class CapabilityPolicyTests
{
    [Fact]
    public void Safe_Always_Allowed()
    {
        new CapabilityPolicy(ServerTransport.Stdio, unattended: false).Require("library_list", CapabilityClass.Safe);
        new CapabilityPolicy(ServerTransport.Http, unattended: false).Require("library_list", CapabilityClass.Safe);
    }

    [Fact]
    public void Mutating_Denied_Under_Stdio_Without_Unattended()
    {
        var p = new CapabilityPolicy(ServerTransport.Stdio, unattended: false);
        Assert.Throws<UnauthorizedAccessException>(() => p.Require("queue_add", CapabilityClass.Mutating));
    }

    [Fact]
    public void Mutating_Allowed_Under_Stdio_With_Unattended()
    {
        new CapabilityPolicy(ServerTransport.Stdio, unattended: true).Require("queue_add", CapabilityClass.Mutating);
    }

    [Fact]
    public void Mutating_Allowed_Under_Http_Always()
    {
        new CapabilityPolicy(ServerTransport.Http, unattended: false).Require("queue_add", CapabilityClass.Mutating);
    }

    [Fact]
    public void Destructive_Requires_Confirm_Even_In_Unattended()
    {
        var p = new CapabilityPolicy(ServerTransport.Http, unattended: true);
        Assert.Throws<UnauthorizedAccessException>(() => p.Require("queue_clear", CapabilityClass.Destructive, confirmed: false));
        p.Require("queue_clear", CapabilityClass.Destructive, confirmed: true); // ok
    }

    [Fact]
    public void Expensive_Treated_Like_Mutating()
    {
        Assert.Throws<UnauthorizedAccessException>(() =>
            new CapabilityPolicy(ServerTransport.Stdio, unattended: false)
                .Require("library_sync", CapabilityClass.Expensive));
        new CapabilityPolicy(ServerTransport.Http, unattended: false)
            .Require("library_sync", CapabilityClass.Expensive);
    }
}
