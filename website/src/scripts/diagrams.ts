/** Render only diagram pages; Mermaid and its layouts are bundled locally and loaded on demand. */
export async function initializeDiagrams() {
  const figures = [...document.querySelectorAll<HTMLElement>('[data-mermaid]')];
  if (!figures.length) return;
  try {
    const { default: mermaid } = await import('mermaid');
    await document.fonts.ready;
    const cache = new Map<string, string>();
    const downloads = new Map<HTMLElement, string>();
    let rendering = false;
    let pending = false;
    async function render() {
      pending = true;
      if (rendering) return;
      rendering = true;
      try {
        do {
          pending = false;
          const dark = document.documentElement.dataset.theme === 'dark';
          mermaid.initialize({
            startOnLoad: false, securityLevel: 'strict', suppressErrorRendering: true,
            theme: 'base', fontFamily: 'IBM Plex Sans Variable, system-ui, sans-serif',
            themeVariables: {
              darkMode: dark,
              background: dark ? '#1d232b' : '#f4f2f0',
              primaryColor: dark ? '#111419' : '#fffaf7',
              primaryTextColor: dark ? '#f4f2f0' : '#1d232b',
              primaryBorderColor: dark ? '#ff7868' : '#b64234',
              lineColor: dark ? '#b9c0c9' : '#515b68',
              secondaryColor: dark ? '#1d232b' : '#f4f2f0',
              tertiaryColor: dark ? '#412b2a' : '#fbe5df',
              noteBkgColor: dark ? '#412b2a' : '#fbe5df',
              noteTextColor: dark ? '#f4f2f0' : '#1d232b',
            },
          });
          for (const [index, figure] of figures.entries()) {
            const key = `${index}-${dark ? 'dark' : 'light'}`;
            let svg = cache.get(key);
            if (!svg) {
              ({ svg } = await mermaid.render(`guardian-diagram-${key}`, decodeURIComponent(figure.dataset.mermaid!)));
              cache.set(key, svg);
            }
            const dialog = figure.querySelector<HTMLDialogElement>('dialog')!;
            const destination = figure.querySelector(dialog.open ? '.diagram-dialog-svg' : '[data-diagram-output]')!;
            destination.innerHTML = svg;
            if (dialog.open && dialog.dataset.width) destination.querySelector('svg')!.style.width = `${dialog.dataset.width}px`;
            const download = figure.querySelector<HTMLAnchorElement>('[data-diagram-download]')!;
            const previous = downloads.get(figure);
            if (previous) URL.revokeObjectURL(previous);
            download.href = URL.createObjectURL(new Blob([svg], { type: 'image/svg+xml' }));
            downloads.set(figure, download.href);
            download.download = `architecture-diagram-${index + 1}.svg`;
            download.hidden = false;
            figure.querySelector<HTMLElement>('[data-diagram-expand]')!.hidden = false;
          }
        } while (pending);
      } finally { rendering = false; }
    }
    for (const figure of figures) {
      const dialog = figure.querySelector<HTMLDialogElement>('dialog')!;
      function zoom(scale?: number) {
        const svg = dialog.querySelector('svg')!;
        const naturalWidth = svg.viewBox.baseVal.width;
        const availableWidth = Math.max(1, dialog.querySelector('.diagram-dialog-svg')!.clientWidth - 64);
        const factor = scale === undefined ? Math.min(1, availableWidth / naturalWidth) : Math.max(.2, Math.min(3, scale));
        dialog.dataset.scale = String(factor);
        dialog.dataset.width = String(naturalWidth * factor);
        svg.style.width = `${naturalWidth * factor}px`;
        dialog.querySelector('[data-diagram-scale]')!.textContent = `${Math.round(factor * 100)}%`;
      }
      figure.querySelector('[data-diagram-expand]')!.addEventListener('click', () => {
        dialog.querySelector('.diagram-dialog-svg')!.append(figure.querySelector('[data-diagram-output] svg')!);
        dialog.showModal();
        zoom();
      });
      figure.querySelector('[data-diagram-zoom-in]')!.addEventListener('click', () => zoom(Number(dialog.dataset.scale) + .25));
      figure.querySelector('[data-diagram-zoom-out]')!.addEventListener('click', () => zoom(Number(dialog.dataset.scale) - .25));
      figure.querySelector('[data-diagram-fit]')!.addEventListener('click', () => zoom());
      figure.querySelector('[data-diagram-close]')!.addEventListener('click', () => dialog.close());
      dialog.addEventListener('close', () => {
        const svg = dialog.querySelector('svg');
        if (svg) {
          svg.style.removeProperty('width');
          figure.querySelector('[data-diagram-output]')!.append(svg);
        }
      });
    }
    const reportError = (error: unknown) => {
      for (const figure of figures) {
        const output = figure.querySelector('[data-diagram-output]')!;
        if (!output.querySelector('svg')) output.textContent = 'Diagram preview unavailable. Open the source below.';
      }
      console.error('Unable to render architecture diagram', error);
    };
    await render().catch(reportError);
    new MutationObserver(() => { void render().catch(reportError); }).observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
  } catch (error) {
    for (const figure of figures) figure.querySelector('[data-diagram-output]')!.textContent = 'Diagram preview unavailable. Open the source below.';
    console.error('Unable to load the local diagram renderer', error);
  }
}
