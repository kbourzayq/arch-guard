import { JSDOM } from 'jsdom';
import { toString } from 'mdast-util-to-string';

// Mermaid's strict parser sanitizes HTML labels; provide a DOM without executing scripts.
const dom = new JSDOM('');
globalThis.window = dom.window;
globalThis.document = dom.window.document;
const { default: mermaid } = await import('mermaid');
mermaid.initialize({ startOnLoad: false, securityLevel: 'strict', suppressErrorRendering: true });
const escape = (value) => value.replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;');

// Keep the original fence as copyable source, with a separate locally rendered diagram.
export async function prepareDiagrams(tree, file) {
  let heading = 'Architecture diagram';
  async function walk(parent) {
    if (!parent.children) return;
    const children = [];
    for (const child of parent.children) {
      if (child.type === 'heading') heading = toString(child);
      if (child.type === 'code' && child.lang === 'mermaid') {
        try { await mermaid.parse(child.value); }
        catch (error) { throw new Error(`${file}:${child.position?.start.line ?? '?'}: invalid Mermaid diagram: ${error.message}`); }
        const label = escape(`${heading} diagram`);
        children.push({ type: 'html', value: `<figure class="guardian-diagram" data-mermaid="${encodeURIComponent(child.value)}">
<figcaption><span>${label}</span><span class="diagram-actions"><button type="button" data-diagram-expand hidden>Expand</button><a data-diagram-download hidden>Download SVG</a></span></figcaption>
<div class="diagram-output" data-diagram-output tabindex="0" role="img" aria-label="${label}"><p class="diagram-status">Loading diagram. Its source is available below.</p></div>
<dialog class="diagram-dialog" aria-label="${label}, expanded"><div class="diagram-dialog-bar"><span>${label}</span><div class="diagram-zoom"><button type="button" data-diagram-zoom-out aria-label="Zoom out">−</button><output data-diagram-scale aria-live="polite">100%</output><button type="button" data-diagram-zoom-in aria-label="Zoom in">+</button><button type="button" data-diagram-fit>Fit</button><button type="button" data-diagram-close>Close</button></div></div><div class="diagram-dialog-svg" tabindex="0" aria-label="Scrollable full-size diagram"></div></dialog>
<details class="diagram-source"><summary>View diagram source</summary>` });
        children.push({ ...child, lang: 'text' });
        children.push({ type: 'html', value: '</details>\n</figure>' });
      } else {
        await walk(child);
        children.push(child);
      }
    }
    parent.children = children;
  }
  await walk(tree);
}
