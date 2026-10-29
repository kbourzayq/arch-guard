import { defineConfig } from 'astro/config';
import { unified } from '@astrojs/markdown-remark';
import starlight from '@astrojs/starlight';
import { base, github, groups, site } from './scripts/site.mjs';

export default defineConfig({
  site,
  base,
  trailingSlash: 'always',
  output: 'static',
  markdown: { processor: unified() },
  integrations: [starlight({
    title: 'Architecture Guardian',
    description: 'Architecture enforcement for .NET. Define modules, layers and public contracts in YAML. Check C# dependencies with CLI, Roslyn analyzers and MCP.',
    logo: { src: './src/assets/logo.svg' },
    favicon: '/favicon.svg',
    social: [{ icon: 'github', label: 'GitHub', href: github }],
    customCss: ['./src/styles/custom.css'],
    components: {
      Hero: './src/components/HomeHero.astro',
      SocialIcons: './src/components/HeaderLinks.astro',
      Footer: './src/components/DocsFooter.astro',
    },
    routeMiddleware: './src/route-data.ts',
    disable404Route: true,
    head: [
      { tag: 'meta', attrs: { property: 'og:image', content: `${site}${base}social.png` } },
      { tag: 'meta', attrs: { property: 'og:image:width', content: '1200' } },
      { tag: 'meta', attrs: { property: 'og:image:height', content: '630' } },
      { tag: 'meta', attrs: { property: 'og:image:alt', content: 'Architecture Guardian — define your boundaries, enforce your architecture.' } },
      { tag: 'meta', attrs: { name: 'twitter:card', content: 'summary_large_image' } },
    ],
    sidebar: groups.map(({ label, items }) => ({
      label,
      collapsed: label === 'Project',
      items: [
        ...items.map(([, slug, label]) => ({ label, slug: `reference/${slug}` })),
        ...(label === 'Project' ? [{ label: 'Roadmap', link: `${github}/issues/44` }] : []),
      ],
    })),
    expressiveCode: { themes: ['github-dark', 'github-light'], defaultProps: { wrap: true } },
  })],
});
