import assert from 'node:assert/strict';
import { test } from 'node:test';
import { execFileSync } from 'node:child_process';
import { mkdtemp, mkdir, writeFile, readFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { materializeRelease, selectPublishedRelease, releaseVersion } from './release-source.mjs';
import { contentGroups } from './site.mjs';
import { transformDocument } from './sync-docs.mjs';

test('public documentation requires a stable published release and every package on NuGet', async () => {
  const calls = [];
  const request = async (url) => {
    calls.push(url);
    return { ok: true, json: async () => url.includes('api.github.com')
      ? { tag_name: 'v0.3.0', published_at: '2026-10-29T21:45:52Z', prerelease: false, draft: false }
      : { versions: ['0.2.0', '0.3.0'] } };
  };
  assert.deepEqual(await selectPublishedRelease('owner/repo', request), { tag: 'v0.3.0', version: '0.3.0' });
  assert.equal(calls.length, 4);
  assert.deepEqual(calls.slice(1).map((url) => url.split('/')[4]), ['architectureguardian.cli', 'architectureguardian.mcp', 'architectureguardian.analyzers']);
  for (const release of [
    { tag_name: 'v0.4.0-preview.1', published_at: 'now', prerelease: true },
    { tag_name: 'v0.4.0', published_at: 'now', draft: true },
    { tag_name: 'v0.4.0', published_at: null },
  ]) {
    await assert.rejects(selectPublishedRelease('owner/repo', async () => ({ ok: true, json: async () => release })), /published stable/);
  }
  await assert.rejects(selectPublishedRelease('owner/repo', async () => ({ ok: false, status: 404 })), /lookup failed/);
  for (const name of ['architectureguardian.cli', 'architectureguardian.mcp', 'architectureguardian.analyzers']) {
    const unavailable = async (url) => url.includes(`/${name}/`)
      ? { ok: true, json: async () => ({ versions: ['0.2.0'] }) } : request(url);
    await assert.rejects(selectPublishedRelease('owner/repo', unavailable), /not available on NuGet/);
  }
  for (const tag of ['main', 'v0.3.0\ntag=main', 'v0.3.0\n', 'v0.4.0-preview.1', '../v0.3.0']) assert.throws(() => releaseVersion(tag), /stable/);
});

test('published content and page inventory come from the tag, never modified or new main files', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'guardian-release-source-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const git = (...args) => execFileSync('git', args, { cwd: root, stdio: 'pipe' });
  git('init');
  await mkdir(join(root, 'docs'));
  await writeFile(join(root, 'Directory.Build.props'), '<Project><PropertyGroup><Version>0.3.0</Version></PropertyGroup></Project>');
  await writeFile(join(root, 'README.md'), '# Install the released CLI\n');
  await writeFile(join(root, 'docs/cli.md'), '# Released CLI\n\nscan only\n');
  git('add', '.');
  git('-c', 'user.name=Fixture', '-c', 'user.email=fixture@example.invalid', 'commit', '-m', 'test: release fixture');
  git('tag', 'v0.3.0');
  await writeFile(join(root, 'docs/cli.md'), '# Development CLI\n\ninit --discover\n');
  await writeFile(join(root, 'docs/discovery.md'), '# Unreleased discovery\n');
  const released = materializeRelease('v0.3.0', root);
  assert.equal(released.version, '0.3.0');
  assert.equal(await readFile(join(released.root, 'docs/cli.md'), 'utf8'), '# Released CLI\n\nscan only\n');
  const pages = contentGroups(released.root, true).flatMap((group) => group.items);
  assert.ok(pages.some(([file, slug]) => file === 'README.md' && slug === 'installation'));
  assert.ok(pages.some(([file, slug]) => file === 'docs/cli.md' && slug === 'cli'));
  assert.ok(!pages.some(([, slug]) => slug === 'discovery'));
  assert.equal(materializeRelease('v0.3.0', root).commit, released.commit);
  git('tag', 'v0.4.0');
  assert.throws(() => materializeRelease('v0.4.0', root), /does not match/);
  assert.throws(() => materializeRelease('v9.0.0', root));
});

test('released source links and source navigation stay pinned to the release tag', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'guardian-release-links-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  await mkdir(join(root, 'docs'));
  await writeFile(join(root, 'config.yaml'), 'version: 1');
  const result = await transformDocument('# Released guide\n\n[Config](../config.yaml)', 'docs/guide.md', root, new Map(), {}, 'v0.3.0');
  assert.match(result.content, /blob\/v0\.3\.0\/config\.yaml/);
  assert.match(result.content, /editUrl: .*blob\/v0\.3\.0\/docs\/guide\.md/);
  assert.doesNotMatch(result.content, /\/main\//);
});
