import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, mkdtempSync, readFileSync, writeFileSync, rmSync, renameSync } from 'node:fs';
import { dirname, resolve, relative, isAbsolute } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { unzipSync } from 'fflate';

// Astro relocates bundled modules under dist during prerendering; its config pins this source root.
export const repositoryRoot = process.env.DOCS_REPOSITORY_ROOT
  ? resolve(process.env.DOCS_REPOSITORY_ROOT) : resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const packages = ['ArchitectureGuardian.Cli', 'ArchitectureGuardian.Mcp', 'ArchitectureGuardian.Analyzers'];

export function releaseVersion(tag) {
  if (!/^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/.test(tag ?? '') || tag.trim() !== tag) {
    throw new Error('Documentation requires a stable vMAJOR.MINOR.PATCH release tag.');
  }
  return tag.slice(1);
}

// A content snapshot is a build input, never another implementation checkout or a fallback to main.
export function materializeRelease(tag, root = repositoryRoot, destination = resolve(root, 'artifacts/docs-source')) {
  const version = releaseVersion(tag);
  const git = (...args) => execFileSync('git', args, { cwd: root, maxBuffer: 32 * 1024 * 1024, stdio: ['ignore', 'pipe', 'pipe'] });
  const commit = git('rev-parse', '--verify', `refs/tags/${tag}^{commit}`).toString().trim();
  const output = resolve(destination, commit);
  const marker = resolve(output, '.docs-release.json');
  if (!existsSync(marker)) {
    const entries = unzipSync(new Uint8Array(git('archive', '--format=zip', commit)));
    // Validate the tagged version before publishing any snapshot marker.
    const props = new TextDecoder().decode(entries['Directory.Build.props'] ?? new Uint8Array());
    if (!props.includes(`<Version>${version}</Version>`)) throw new Error(`${tag}: tagged source version does not match the release.`);
    mkdirSync(destination, { recursive: true });
    const temporary = mkdtempSync(resolve(destination, '.staging-'));
    try {
      for (const [name, data] of Object.entries(entries)) {
        const path = resolve(temporary, name);
        const inside = relative(temporary, path);
        if (inside === '..' || inside.startsWith('../') || inside.startsWith('..\\') || isAbsolute(inside)) throw new Error('Release archive escapes its content root.');
        if (name.endsWith('/')) mkdirSync(path, { recursive: true });
        else { mkdirSync(dirname(path), { recursive: true }); writeFileSync(path, data); }
      }
      writeFileSync(resolve(temporary, '.docs-release.json'), JSON.stringify({ tag, version, commit }) + '\n');
      try { renameSync(temporary, output); }
      catch (error) { if (!existsSync(marker)) throw error; } // Another build can publish the identical complete snapshot first.
    } finally {
      rmSync(temporary, { recursive: true, force: true });
    }
  }
  const metadata = JSON.parse(readFileSync(marker, 'utf8'));
  if (metadata.tag !== tag || metadata.version !== version || metadata.commit !== commit) throw new Error('Release snapshot provenance does not match its tag.');
  return { root: output, tag, version, commit, kind: 'release', sourceRef: tag };
}

export async function selectPublishedRelease(repository, request = fetch, token = '') {
  if (!/^[\w.-]+\/[\w.-]+$/.test(repository ?? '') || repository.trim() !== repository) throw new Error('Choose an owner/repository for published documentation.');
  const response = await request(`https://api.github.com/repos/${repository}/releases/latest`, {
    headers: { Accept: 'application/vnd.github+json', ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    signal: AbortSignal.timeout(20000),
  });
  if (!response.ok) throw new Error(`Published GitHub release lookup failed (${response.status}); development docs cannot be deployed.`);
  const release = await response.json();
  if (release.draft !== false || release.prerelease !== false || !release.published_at) throw new Error('Only published stable releases can supply public documentation.');
  const version = releaseVersion(release.tag_name);
  for (const name of packages) {
    const index = await request(`https://api.nuget.org/v3-flatcontainer/${name.toLowerCase()}/index.json`, { signal: AbortSignal.timeout(20000) });
    const versions = index.ok ? (await index.json()).versions : null;
    if (!Array.isArray(versions) || !versions.includes(version)) {
      throw new Error(`${name} ${version} is not available on NuGet; keep the previous published documentation.`);
    }
  }
  return { tag: release.tag_name, version };
}

export const content = process.env.DOCS_RELEASE_TAG
  ? materializeRelease(process.env.DOCS_RELEASE_TAG)
  : { root: repositoryRoot, kind: 'development', sourceRef: 'main', tag: null, version: null, commit: null };

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  if (process.argv[2] !== 'select') throw new Error('Use release-source.mjs select to verify published documentation inputs.');
  const release = await selectPublishedRelease(process.env.GITHUB_REPOSITORY, fetch, process.env.GH_TOKEN);
  if (!process.env.GITHUB_OUTPUT) throw new Error('Release selection requires a GitHub Actions output file.');
  writeFileSync(process.env.GITHUB_OUTPUT, `tag=${release.tag}\nversion=${release.version}\n`, { flag: 'a' });
  console.log(`Public documentation source: ${release.tag}; all three NuGet packages verified.`);
}
