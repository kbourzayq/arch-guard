using System.Diagnostics;
using System.Text;

namespace ArchitectureGuardian.Benchmarks;

/// <summary>Owns package-free benchmark repositories and their preparation outside measured operations.</summary>
internal sealed class BenchmarkInputs : IDisposable
{
    internal const string EnvironmentVariable = "ARCHGUARD_BENCHMARK_INPUTS";
    private static readonly string[] Cases = ["CleanArchitecture", "ModularMonolith", "Generated24x40"];
    private const string Rules = "rules:\n  AG001: error\n  AG002: error\n  AG003: error\n  AG004: error\n  AG005: error\n  AG006: error\n";

    private BenchmarkInputs(string directory) => DirectoryPath = directory;

    internal string DirectoryPath { get; }

    internal static string FindRepository()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ArchitectureGuardian.slnx"))
                && File.Exists(Path.Combine(directory.FullName, "global.json"))) { return directory.FullName; }
        }
        throw new InvalidOperationException("Run the benchmark project from an Architecture Guardian development checkout.");
    }

    internal static async Task<BenchmarkInputs> PrepareAsync(string repository, CancellationToken cancellationToken)
    {
        var inputs = new BenchmarkInputs(Path.Combine(repository, "artifacts", "performance", "inputs", Guid.NewGuid().ToString("N")));
        try
        {
            Directory.CreateDirectory(inputs.DirectoryPath);
            var references = Path.Combine(repository, "tests", "Fixtures", "ReferenceSolutions");
            foreach (var name in new[] { "Directory.Build.props", "Directory.Packages.props", "NuGet.Config" })
            {
                File.Copy(Path.Combine(references, name), Path.Combine(inputs.DirectoryPath, name));
            }
            File.Copy(Path.Combine(repository, "global.json"), Path.Combine(inputs.DirectoryPath, "global.json"));
            // Stop future checkout targets from changing these deliberately package-free inputs.
            File.WriteAllText(Path.Combine(inputs.DirectoryPath, "Directory.Build.targets"), "<Project />\n");
            foreach (var name in Cases.Take(2))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = Path.Combine(inputs.DirectoryPath, name);
                CopyDirectory(Path.Combine(references, name), destination);
                var policy = Path.Combine(destination, "architecture.guardian.yaml");
                var source = await File.ReadAllTextAsync(policy, cancellationToken);
                var index = source.IndexOf("rules:", StringComparison.Ordinal);
                if (index < 0) { throw new InvalidOperationException($"Reference policy {name} has no rule section."); }
                await File.WriteAllTextAsync(policy, source[..index] + Rules, cancellationToken);
            }
            Generate(Path.Combine(inputs.DirectoryPath, "Generated24x40"), cancellationToken);
            foreach (var name in Cases)
            {
                Console.WriteLine($"Preparing {name} (outside measurement)");
                await RestoreAsync(inputs.DirectoryPath, Path.Combine(inputs.DirectoryPath, name, name + ".slnx"), cancellationToken);
            }
            return inputs;
        }
        catch
        {
            inputs.Dispose();
            throw;
        }
    }

    /// <summary>Deletes only the isolated input directory; generated BenchmarkDotNet reports remain available.</summary>
    public void Dispose()
    {
        if (Directory.Exists(DirectoryPath)) { Directory.Delete(DirectoryPath, recursive: true); }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source).Order(StringComparer.Ordinal))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (var directory in Directory.EnumerateDirectories(source).Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(directory);
            if (name is not ("bin" or "obj")) { CopyDirectory(directory, Path.Combine(destination, name)); }
        }
    }

    private static void Generate(string directory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var projects = new List<string>();
        var modules = new StringBuilder("version: 1\nmodules:\n");
        for (var number = 0; number < 24; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = $"Module{number:000}";
            var previous = $"Module{number - 1:000}";
            var folder = Directory.CreateDirectory(Path.Combine(directory, name)).FullName;
            var reference = number == 0 ? "" : $"<ItemGroup><ProjectReference Include=\"../{previous}/{previous}.csproj\" /></ItemGroup>";
            File.WriteAllText(Path.Combine(folder, name + ".csproj"), $"<Project Sdk=\"Microsoft.NET.Sdk\">{reference}</Project>\n");
            projects.Add($"<Project Path=\"{name}/{name}.csproj\" />");
            modules.Append($"  - id: {name}\n    projects: [\"{name}/*.csproj\"]\n");
            if (number != 0) { modules.Append($"    allowedDependencies: [\"module:{previous}\"]\n"); }
            for (var type = 0; type < 40; type++)
            {
                var methods = Enumerable.Range(0, 8).Select(method => number == 0
                    ? $"    public int Call{method}() => {type + method};"
                    : $"    public {previous}.Type000 Call{method}() => new();");
                File.WriteAllText(Path.Combine(folder, $"Type{type:000}.cs"),
                    $"namespace {name};\npublic class Type{type:000}\n{{\n" + string.Join('\n', methods) + "\n}\n");
            }
        }
        File.WriteAllText(Path.Combine(directory, "Generated24x40.slnx"), "<Solution>\n" + string.Join('\n', projects) + "\n</Solution>\n");
        File.WriteAllText(Path.Combine(directory, "architecture.guardian.yaml"), modules + """
            layers:
              - id: All
                projects: ["**/*.csproj"]
                forbiddenNamespaces: [Example.Forbidden]
                domainInfrastructure:
                  forbiddenNamespaces: [Example.Infrastructure]
            """ + "\n" + Rules);
    }

    private static async Task RestoreAsync(string root, string input, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in new[] { "restore", input, "--configfile", Path.Combine(root, "NuGet.Config"),
            "-p:Configuration=Release", "--disable-parallel", "--disable-build-servers" }) { start.ArgumentList.Add(argument); }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        using var process = Process.Start(start) ?? throw new IOException("Cannot start dotnet restore for benchmark inputs.");
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var standardOutput = await output;
            var standardError = await error;
            if (process.ExitCode != 0) { throw new IOException($"Benchmark restore failed: {standardOutput}\n{standardError}"); }
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); }
            await process.WaitForExitAsync(CancellationToken.None);
            // Observe both drains even when cancellation interrupted their reads.
            try { await Task.WhenAll(output, error); }
            catch (OperationCanceledException) { }
            throw;
        }
    }
}
