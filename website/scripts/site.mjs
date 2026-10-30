// Shared by synchronization, configuration and production validation.
export const site = 'https://kbourzayq.github.io';
export const base = '/arch-guard/';
export const github = 'https://github.com/kbourzayq/arch-guard';
export const pagePath = (slug) => `${base}reference/${slug}/`;

export const groups = [
  { label: 'Getting Started', items: [
    ['docs/README.md', 'documentation', 'Start here · choose your path'],
    ['docs/install.md', 'installation', 'Installation'],
    ['docs/getting-started.md', 'getting-started', 'Quick start & first policy'],
    ['docs/use-cases.md', 'examples', 'Examples & use cases'],
  ] },
  { label: 'Core Concepts', items: [
    ['docs/concepts/architecture-as-code.md', 'architecture', 'Architecture as code'],
    ['docs/concepts/modules-and-layers.md', 'repository-model', 'Modules, layers & contracts'],
    ['docs/yaml-guide.md', 'yaml-guide', 'Write a YAML policy'],
    ['docs/concepts/dependency-analysis.md', 'dependency-graph', 'Dependency analysis'],
    ['docs/concepts/diagnostics.md', 'shared-contracts', 'Diagnostics & outcomes'],
  ] },
  { label: 'Integrations', items: [
    ['docs/cli.md', 'cli', 'CLI commands & exit codes'],
    ['docs/compiler-analyzer.md', 'compiler-analyzer', 'Roslyn analyzers'],
    ['docs/mcp.md', 'mcp', 'MCP server'],
    ['docs/agent-workflow.md', 'agent-workflow', 'AI agent workflow'],
    ['docs/ci.md', 'ci', 'CI/CD quality gates'],
  ] },
  { label: 'Advanced', items: [
    ['docs/baselines.md', 'baselines', 'Baselines'],
    ['docs/changes.md', 'changes', 'Git change analysis'],
    ['docs/benchmarks.md', 'benchmarks', 'Benchmarks'],
    ['benchmarks/reports/analysis-macos-arm64.md', 'benchmark-results', 'Benchmark results', {
      title: 'BenchmarkDotNet results',
      description: 'Published latency, allocation and GC results, with hardware/runtime context and confidence intervals.',
    }],
    ['docs/performance.md', 'performance', 'Performance & telemetry'],
    ['docs/workspace-loading.md', 'workspace-loading', 'Workspace loading'],
    ['docs/troubleshooting.md', 'troubleshooting', 'Troubleshooting'],
  ] },
  { label: 'Reference', items: [
    ['docs/policy.md', 'policy', 'YAML policies & public contracts'],
    ['docs/rule-catalog.md', 'rule-catalog', 'Rules AG001–AG006'],
    ['docs/reports.md', 'reports', 'JSON & SARIF reports'],
    ['docs/semantic-accuracy.md', 'semantic-accuracy', 'Engine coverage & limitations'],
  ] },
  { label: 'Project', items: [
    ['docs/releases.md', 'releases', 'Releases & compatibility'],
    ['CHANGELOG.md', 'changelog', 'Release notes'],
  ] },
];
export const pages = groups.flatMap((group) => group.items);
