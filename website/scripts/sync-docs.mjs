import { mkdir, readFile, rm, writeFile, cp, stat } from 'node:fs/promises';
import { dirname, resolve, relative, sep } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { unified } from 'unified';
import remarkParse from 'remark-parse';
import remarkGfm from 'remark-gfm';
import remarkStringify from 'remark-stringify';
import { visit } from 'unist-util-visit';
import { toString } from 'mdast-util-to-string';
import GithubSlugger from 'github-slugger';
import { base, github, pages, pagePath, site } from './site.mjs';
import { prepareDiagrams } from './diagrams.mjs';
import { buildExamples } from './examples.mjs';

export const websiteRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const repositoryRoot = resolve(websiteRoot, '..');
const markdown = unified().use(remarkParse).use(remarkGfm).use(remarkStringify, { fences: true });

// Exported for fixture tests: never consult or mutate a generated page as source.
export async function transformDocument(source, file, root = repositoryRoot, mapping = new Map(pages.map(([path, slug]) => [path, slug])), metadata = {}) {
  const tree = markdown.parse(source);
  const heading = tree.children[0];
  const hasTitle = heading?.type === 'heading' && heading.depth === 1;
  if (!hasTitle && file !== 'CHANGELOG.md' && !metadata.title) {
    throw new Error(`${file}: expected a leading level-one title`);
  }
  const title = hasTitle ? toString(heading) : (metadata.title ?? 'Release notes');
  const titleAnchor = new GithubSlugger().slug(title);
  const paragraph = tree.children.find((node) => node.type === 'paragraph');
  const description = metadata.description ?? toString(paragraph ?? heading).replace(/\s+/g, ' ').slice(0, 180);
  const assets = new Set();
  const references = [];
  const imageDefinitions = new Set();
  visit(tree, 'imageReference', (node) => { imageDefinitions.add(node.identifier); });
  visit(tree, (node) => {
    if (['link', 'image', 'definition'].includes(node.type)) {
      references.push({ node, image: node.type === 'image' || imageDefinitions.has(node.identifier) });
    }
  });

  async function rewrite(url, image = false) {
    if (/^https?:/i.test(url)) {
      const target = new URL(url);
      if (target.origin === site && target.pathname.startsWith(base)) {
        return `${target.pathname}${target.search}${target.hash}`;
      }
    }
    if (/^(?:[a-z][\w+.-]*:|\/\/|#)/i.test(url)) return url;
    const match = /^([^?#]*)(\?[^#]*)?(#.*)?$/.exec(url);
    if (!match) throw new Error(`${file}: malformed link ${url}`);
    const [, pathname, query = '', hash = ''] = match;
    const target = pathname.startsWith('/')
      ? resolve(root, `.${decodeURIComponent(pathname)}`)
      : resolve(root, dirname(file), decodeURIComponent(pathname));
    const path = relative(root, target).split(sep).join('/');
    if (path.startsWith('../') || path === '..') throw new Error(`${file}: link escapes repository: ${url}`);
    let info;
    try { info = await stat(target); }
    catch { throw new Error(`${file}: missing link target: ${url}`); }
    if (image || /\.(svg|png|jpe?g|gif|webp)$/i.test(path)) {
      if (!info.isFile()) throw new Error(`${file}: image is not a file: ${url}`);
      assets.add(path);
      return `${base}source-assets/${path.split('/').map(encodeURIComponent).join('/')}${query}${hash}`;
    }
    const slug = mapping.get(path);
    if (slug) return `${pagePath(slug)}${query}${hash}`;
    return `${github}/${info.isDirectory() ? 'tree' : 'blob'}/main/${path.split('/').map(encodeURIComponent).join('/')}${query}${hash}`;
  }

  for (const { node, image } of references) node.url = await rewrite(node.url, image);
  // Markdown permits raw HTML links/images too. Preserve the markup and rewrite its URLs.
  const htmlNodes = [];
  visit(tree, 'html', (node) => { htmlNodes.push(node); });
  for (const node of htmlNodes) {
    const matches = [...node.value.matchAll(/\b(href|src)=(['"])(.*?)\2/gi)];
    for (const match of matches.reverse()) {
      const value = await rewrite(match[3], match[1].toLowerCase() === 'src');
      const start = match.index + match[1].length + 2;
      node.value = node.value.slice(0, start) + value + node.value.slice(start + match[3].length);
    }
  }
  await prepareDiagrams(tree, file);
  // Starlight supplies the visible h1. Keep the source title's GitHub anchor addressable.
  if (hasTitle) tree.children[0] = { type: 'html', value: `<span id="${titleAnchor}"></span>` };
  const frontmatter = {
    title, description,
    editUrl: `${github}/edit/main/${file}`,
  };
  const header = Object.entries(frontmatter).map(([key, value]) => `${key}: ${JSON.stringify(value)}`).join('\n');
  return { content: `---\n${header}\n---\n\n${markdown.stringify(tree)}`, assets };
}

export async function syncDocs() {
  const destination = resolve(websiteRoot, 'src/content/docs/reference');
  const assetDirectory = resolve(websiteRoot, 'public/source-assets');
  const files = new Set();
  const slugs = new Set();
  // Validate all inputs before replacing the previous generated content.
  const documents = [];
  for (const [file, slug, , metadata] of pages) {
    if (files.has(file) || slugs.has(slug)) throw new Error(`Duplicate content mapping: ${file} / ${slug}`);
    files.add(file);
    slugs.add(slug);
    documents.push({ slug, ...await transformDocument(await readFile(resolve(repositoryRoot, file), 'utf8'), file, undefined, undefined, metadata) });
  }
  await rm(destination, { recursive: true, force: true });
  await rm(assetDirectory, { recursive: true, force: true });
  await mkdir(destination, { recursive: true });
  for (const { slug, content, assets } of documents) {
    await writeFile(resolve(destination, `${slug}.md`), content);
    for (const asset of assets) {
      const output = resolve(assetDirectory, asset);
      await mkdir(dirname(output), { recursive: true });
      await cp(resolve(repositoryRoot, asset), output);
    }
  }
  await buildExamples();
  console.log(`Synchronized ${documents.length} authoritative documents and two runnable example downloads.`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(resolve(process.argv[1])).href) await syncDocs();
