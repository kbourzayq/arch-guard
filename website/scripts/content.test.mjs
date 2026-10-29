import assert from 'node:assert/strict';
import { test } from 'node:test';
import { mkdtemp, mkdir, writeFile, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, posix, win32 } from 'node:path';
import { transformDocument } from './sync-docs.mjs';
import { checkLinks, isWithinDirectory } from './check-links.mjs';

test('output containment handles native paths on POSIX and Windows', () => {
  assert.equal(isWithinDirectory('/site/dist', '/site/dist/guide/index.html', posix), true);
  assert.equal(isWithinDirectory('/site/dist', '/site/dist-other/page.html', posix), false);
  assert.equal(isWithinDirectory('/site/dist', '/site/outside.html', posix), false);
  assert.equal(isWithinDirectory('C:\\site\\dist', 'C:\\site\\dist\\guide\\index.html', win32), true);
  assert.equal(isWithinDirectory('C:\\site\\dist', 'C:\\site\\dist', win32), true);
  assert.equal(isWithinDirectory('C:\\site\\dist', 'C:\\site\\dist-other\\page.html', win32), false);
  assert.equal(isWithinDirectory('C:\\site\\dist', 'C:\\site\\outside.html', win32), false);
  assert.equal(isWithinDirectory('C:\\site\\dist', 'D:\\site\\dist\\page.html', win32), false);
});

test('synchronization preserves GFM, code, title anchors, images and authoritative edit links', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'guardian-content-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  await mkdir(join(root, 'docs'));
  await writeFile(join(root, 'docs/guide.md'), '# Guide');
  await writeFile(join(root, 'docs/diagram.svg'), '<svg/>');
  await writeFile(join(root, 'config.yaml'), 'version: 1');
  const source = '# Source title\n\nA useful description.\n\n[Guide](guide.md#install)\n\n[Reference][guide]\n\n[guide]: guide.md#install\n\n![Diagram](diagram.svg)\n\n<img src="diagram.svg" alt="Diagram">\n\n[Config](../config.yaml)\n\n| Rule | Result |\n| --- | --- |\n| AG002 | error |\n\n```yaml\nversion: 1\n```\n';
  const mapping = new Map([['docs/guide.md', 'guide']]);
  const result = await transformDocument(source, 'docs/source.md', root, mapping);
  assert.deepEqual(result, await transformDocument(source, 'docs/source.md', root, mapping));
  assert.match(result.content, /id="source-title"/);
  assert.match(result.content, /\/arch-guard\/reference\/guide\/#install/);
  assert.match(result.content, /\/arch-guard\/source-assets\/docs\/diagram.svg/);
  assert.match(result.content, /edit\/main\/docs\/source.md/);
  assert.match(result.content, /blob\/main\/config.yaml/);
  assert.match(result.content, /\| AG002\s*\| error\s*\|/);
  assert.match(result.content, /```yaml\nversion: 1\n```/);
  assert.deepEqual([...result.assets], ['docs/diagram.svg']);
});

test('synchronization rejects missing sources, image targets and repository escapes', async () => {
  await assert.rejects(transformDocument('# Title\n\n[Broken](missing.md)', 'docs/test.md'), /missing link target/);
  await assert.rejects(transformDocument('# Title\n\n![Broken](missing.svg)', 'docs/test.md'), /missing link target/);
  await assert.rejects(transformDocument('# Title\n\n[Outside](../../outside.md)', 'test.md'), /escapes repository/);
  await assert.rejects(transformDocument('No title', 'test.md'), /leading level-one title/);
});

test('Mermaid diagrams get a validated preview, expandable view and preserved source', async () => {
  const result = await transformDocument('# Title\n\n## Execution\n\n```mermaid\nflowchart TD\nA --> B\n```', 'test.md');
  assert.match(result.content, /class="guardian-diagram"/);
  assert.match(result.content, /Execution diagram/);
  assert.match(result.content, /data-diagram-expand/);
  assert.match(result.content, /View diagram source/);
  assert.match(result.content, /```text\nflowchart TD\nA --> B\n```/);
  await assert.rejects(transformDocument('# Title\n\n```mermaid\nflowchart TD\nA --> [\n```', 'test.md'), /test.md:3: invalid Mermaid diagram/);
});

test('production validation checks base paths, anchors, encoded assets and heading structure', async (t) => {
  const root = await mkdtemp(join(tmpdir(), 'guardian-links-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  await mkdir(join(root, 'guide'));
  await writeFile(join(root, 'image name.svg'), '<svg/>');
  await writeFile(join(root, 'guide/index.html'), '<h1>Guide</h1><h2 id="install">Install</h2>');
  const good = '<h1>Home</h1><a href="/arch-guard/guide/#install">Guide</a><img src="/arch-guard/image%20name.svg" alt="Test">';
  await writeFile(join(root, 'index.html'), good);
  await checkLinks(root);
  for (const [html, error] of [
    [good.replace('#install', '#missing'), /missing anchor/],
    [good.replace('/arch-guard/guide/', '/guide/'), /escapes base/],
    [good.replace('image%20name.svg', 'missing.svg'), /missing target/],
    [good + '<h3>Skipped heading</h3>', /heading level skips/],
  ]) {
    await writeFile(join(root, 'index.html'), html);
    await assert.rejects(checkLinks(root), error);
  }
});
