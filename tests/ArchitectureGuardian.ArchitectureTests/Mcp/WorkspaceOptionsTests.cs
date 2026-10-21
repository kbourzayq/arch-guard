using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using ArchitectureGuardian.Mcp.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Mcp;

/// <summary>Verifies workspace configuration binding and startup validation through the standard options service.</summary>
public sealed class WorkspaceOptionsTests
{
    /// <summary>Requires an existing absolute directory before host startup.</summary>
    [Fact]
    public void Invalid_workspace_configuration_fails_startup_validation()
    {
        foreach (var root in new[] { "", "relative", Path.Combine(Repository.Root, "README.md"), Path.Combine(Repository.Root, Guid.NewGuid().ToString()) })
        {
            using var services = Services(root);
            Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IStartupValidator>().Validate());
        }
    }

    /// <summary>Binds normal host settings and defaults build trust to false.</summary>
    [Fact]
    public void Workspace_settings_bind_through_the_standard_options_service()
    {
        using var defaults = Services(Repository.Root);
        defaults.GetRequiredService<IStartupValidator>().Validate();
        var options = defaults.GetRequiredService<IOptions<WorkspaceOptions>>().Value;
        Assert.Equal(Repository.Root, options.Root);
        Assert.False(options.Trusted);
        using var trusted = Services(Repository.Root, "true");
        Assert.True(trusted.GetRequiredService<IOptions<WorkspaceOptions>>().Value.Trusted);
    }

    private static ServiceProvider Services(string root, string? trusted = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Workspace:Root"] = root,
            ["Workspace:Trusted"] = trusted,
        }).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddArchitectureGuardianMcp();
        return services.BuildServiceProvider();
    }
}
