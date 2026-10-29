// Shared by synchronization, configuration and production validation.
export const site = 'https://kbourzayq.github.io';
export const base = '/arch-guard/';
export const github = 'https://github.com/kbourzayq/arch-guard';
export const pagePath = (slug) => `${base}reference/${slug}/`;

export const groups = [
  { label: 'Getting Started', items: [
    ['docs/README.md', 'documentation', 'Start here · choose your path'],
    ['README.md', 'installation', 'Installation & introduction'],
    ['docs/getting-started.md', 'getting-started', 'Quick start & first policy'],
    ['docs/m1-demo.md', 'examples', 'First violations & examples'],
  ] },
  { label: 'Core Concepts', items: [
    ['docs/architecture/overview.md', 'architecture', 'Architecture as code'],
    ['docs/repository-model.md', 'repository-model', 'Modules, layers & ownership'],
    ['docs/dependency-graph.md', 'dependency-graph', 'Dependency graph'],
    ['docs/shared-contracts.md', 'shared-contracts', 'Analysis contracts & diagnostics'],
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
    ['docs/performance.md', 'performance', 'Performance'],
    ['docs/workspace-loading.md', 'workspace-loading', 'Workspace loading'],
    ['docs/troubleshooting.md', 'troubleshooting', 'Troubleshooting'],
    ['docs/installation.md', 'local-development', 'Local .NET development'],
    ['docs/manual-validation.md', 'manual-validation', 'Repository validation'],
  ] },
  { label: 'Reference', items: [
    ['docs/policy.md', 'policy', 'YAML policies & public contracts'],
    ['docs/rule-catalog.md', 'rule-catalog', 'Rules AG001–AG006'],
    ['docs/reports.md', 'reports', 'JSON & SARIF reports'],
    ['docs/semantic-accuracy.md', 'semantic-accuracy', 'Engine coverage & limitations'],
    ['docs/architecture/decisions/0001-component-boundaries-and-analysis-engines.md', 'architecture-decision', 'Architecture decision'],
  ] },
  { label: 'Project', items: [
    ['docs/releases.md', 'releases', 'Releases & compatibility'],
    ['CHANGELOG.md', 'changelog', 'Release notes'],
    ['CONTRIBUTING.md', 'contributing', 'Contributing'],
    ['AGENT.md', 'coding-conventions', 'Coding conventions'],
    ['benchmarks/README.md', 'benchmarks', 'Benchmark development'],
    ['website/README.md', 'website-development', 'Website development'],
  ] },
];
export const pages = groups.flatMap((group) => group.items);
