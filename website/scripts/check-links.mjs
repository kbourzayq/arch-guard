import { readFile, readdir, stat } from 'node:fs/promises';
import path, { resolve, relative, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { parse } from 'parse5';
import { base, site, pages } from './site.mjs';

export function isWithinDirectory(directory, target, paths = path) {
  const relativePath = paths.relative(directory, target);
  return relativePath === '' || (relativePath !== '..' && !relativePath.startsWith(`..${paths.sep}`) && !paths.isAbsolute(relativePath));
}

export function inspectHtml(html) {
  const ids = new Set();
  const links = [];
  const headings = [];
  function walk(node) {
    const attrs = Object.fromEntries((node.attrs ?? []).map(({ name, value }) => [name, value]));
    if (attrs.id) ids.add(attrs.id);
    if (/^h[1-6]$/.test(node.tagName)) headings.push(Number(node.tagName[1]));
    if (attrs.href) links.push({ url: attrs.href, anchor: node.tagName === 'a' });
    if (attrs.src) links.push({ url: attrs.src });
    if (attrs.srcset) {
      for (const item of attrs.srcset.split(',')) links.push({ url: item.trim().split(/\s+/)[0] });
    }
    // Only metadata containing a URL is a resource reference.
    if (['og:image', 'twitter:image'].includes(attrs.property ?? attrs.name)) links.push({ url: attrs.content });
    for (const child of node.childNodes ?? []) walk(child);
  }
  walk(parse(html));
  return { ids, links, headings };
}

export async function checkLinks(directory, { expectedPages } = {}) {
  const documents = new Map();
  async function collect(path) {
    for (const item of await readdir(path, { withFileTypes: true })) {
      const file = resolve(path, item.name);
      if (item.isDirectory()) await collect(file);
      else if (item.name.endsWith('.html')) documents.set(file, inspectHtml(await readFile(file, 'utf8')));
    }
  }
  await collect(directory);
  if (!documents.size) throw new Error('No built HTML documents found');
  const errors = [];
  if (expectedPages) {
    const expected = new Set(expectedPages.map((file) => resolve(directory, file)));
    for (const file of documents.keys()) if (!expected.has(file)) errors.push(`Unexpected published page: ${relative(directory, file)}`);
    for (const file of expected) if (!documents.has(file)) errors.push(`Missing published page: ${relative(directory, file)}`);
  }
  for (const [file, { links, headings }] of documents) {
    const page = `${site}${base}${relative(directory, file).replaceAll('\\', '/').replace(/index\.html$/, '')}`;
    if (headings.filter((level) => level === 1).length !== 1) errors.push(`${file}: expected one h1`);
    for (let i = 1; i < headings.length; i++) {
      if (headings[i] > headings[i - 1] + 1) errors.push(`${file}: heading level skips h${headings[i - 1]} → h${headings[i]}`);
    }
    for (const { url, anchor } of links) {
      const target = new URL(url, page);
      if (target.origin !== site || !['http:', 'https:'].includes(target.protocol)) continue;
      if (!target.pathname.startsWith(base)) { errors.push(`${file}: URL escapes base: ${url}`); continue; }
      const pathname = decodeURIComponent(target.pathname.slice(base.length));
      let path = resolve(directory, pathname);
      if (!isWithinDirectory(directory, path)) { errors.push(`${file}: URL escapes output: ${url}`); continue; }
      try {
        if ((await stat(path)).isDirectory()) path = resolve(path, 'index.html');
        await stat(path);
      } catch { errors.push(`${file}: missing target ${url}`); continue; }
      if (anchor && target.hash && documents.has(path) && !documents.get(path).ids.has(decodeURIComponent(target.hash.slice(1)))) {
        errors.push(`${file}: missing anchor ${url}`);
      }
    }
  }
  if (errors.length) throw new Error(`Built site validation failed:\n${[...new Set(errors)].join('\n')}`);
  console.log(`Validated links, anchors, assets and headings in ${documents.size} built pages under ${base}.`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) {
  await checkLinks(resolve(dirname(fileURLToPath(import.meta.url)), '../dist'), {
    expectedPages: ['index.html', '404.html', ...pages.map(([, slug]) => `reference/${slug}/index.html`)],
  });
}
