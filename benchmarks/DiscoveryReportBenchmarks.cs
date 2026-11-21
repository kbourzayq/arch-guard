using System.Security.Cryptography;
using System.Text;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Diagnostics;
using ArchitectureGuardian.Core.Discovery;
using ArchitectureGuardian.Core.Repositories;
using ArchitectureGuardian.Reporting;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;

namespace ArchitectureGuardian.Benchmarks;

/// <summary>Compares buffer and stream costs for writing the same discovery evidence to disk.</summary>
[MemoryDiagnoser]
[MinColumn, MaxColumn, MedianColumn]
[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 3, iterationCount: 10, invocationCount: 1)]
public class DiscoveryReportBenchmarks
{
    private DiscoveryResult _result = null!;
    private DirectoryInfo? _directory;
    private string _reportPath = null!;
    private long _expectedSize;
    private byte[] _expectedHash = null!;

    /// <summary>Gets or sets the number of distinct source locations in the evidence graph.</summary>
    [Params(5_000, 200_000)]
    public int EvidenceLocations { get; set; }

    /// <summary>Prepares the immutable input, isolated output directory and reference file size and hash.</summary>
    [GlobalSetup]
    public async Task SetupAsync()
    {
        _result = CreateResult(EvidenceLocations);
        _directory = Directory.CreateTempSubdirectory("archguard-discovery-benchmark-");
        _reportPath = Path.Combine(_directory.FullName, "architecture.guardian.discovery.json");
        try
        {
            await StreamedFile();
            await using (var file = File.OpenRead(_reportPath))
            {
                _expectedSize = file.Length;
                _expectedHash = await SHA256.HashDataAsync(file);
            }

            File.Delete(_reportPath);
            Console.WriteLine(FormattableString.Invariant($"Discovery report size ({EvidenceLocations} evidence locations): {_expectedSize} bytes."));
        }
        catch
        {
            Cleanup();
            throw;
        }
    }

    /// <summary>Serializes the full report to a string and writes it as UTF-8, as the original CLI did.</summary>
    [Benchmark(Baseline = true)]
    public Task BufferedFile() => File.WriteAllTextAsync(_reportPath, DiscoveryReportSerializer.Serialize(_result), new UTF8Encoding(false));

    /// <summary>Serializes the same report incrementally to an asynchronous file stream.</summary>
    [Benchmark]
    public async Task StreamedFile()
    {
        await using var file = new FileStream(_reportPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous);
        await DiscoveryReportSerializer.SerializeAsync(file, _result);
    }

    /// <summary>Verifies that every invocation produced the complete reference bytes and removes the output.</summary>
    [IterationCleanup]
    public void VerifyIteration()
    {
        using (var file = File.OpenRead(_reportPath))
        {
            if (file.Length != _expectedSize || !SHA256.HashData(file).AsSpan().SequenceEqual(_expectedHash))
            {
                throw new InvalidOperationException("The measured discovery report differs from the prepared report.");
            }
        }

        File.Delete(_reportPath);
    }

    /// <summary>Removes the isolated output directory after measurement.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        _directory?.Delete(recursive: true);
        _directory = null;
    }

    private static DiscoveryResult CreateResult(int locationCount)
    {
        const int edgeCount = 100;
        const string application = "Example.Application";
        const string domain = "Example.Domain";
        const string applicationPath = "src/Example.Application/Example.Application.csproj";
        const string domainPath = "src/Example.Domain/Example.Domain.csproj";
        var inventory = new RepositoryInventory("Example.slnx",
        [
            new(applicationPath, application, new(applicationPath, application, [])),
            new(domainPath, domain, new(domainPath, domain, [])),
        ]);
        var source = new DependencyNode(DependencyKind.Type, "Example.Application.OrderService", "OrderService", applicationPath);
        var edges = Enumerable.Range(1, edgeCount).Select(number =>
        {
            var name = FormattableString.Invariant($"OrderType{number:D3}");
            var target = new DependencyNode(DependencyKind.Type, $"{domain}.{name}", name, domainPath);
            var path = $"src/Example.Application/Services/OrderService.{name}.cs";
            var evidence = Enumerable.Range(1, locationCount / edgeCount).Select(line => new DependencyEvidence(applicationPath, domainPath,
                new DiagnosticEvidence("Uses a domain type when processing the order request.",
                    new SourceLocation(path, line, 5, line, 10)),
                source.Identity, target.Identity, targetAssembly: domain, targetNamespace: domain));
            return new DependencyEdge(source, target, evidence);
        });
        var graph = new DependencyGraph([], edges);
        var observations = new DependencyInventoryResult(inventory, graph, AnalysisStatus.Completed);
        var proposal = new PolicyProposalBuilder().Build(observations);
        if (proposal.Policy is null) { throw new InvalidOperationException("The discovery benchmark fixture needs a reviewable proposal."); }

        return new("Example.slnx", "Release", null, [applicationPath, domainPath], [], observations, proposal, new(AnalysisStatus.Completed));
    }
}
