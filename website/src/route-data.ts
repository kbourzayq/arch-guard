import { defineRouteMiddleware } from '@astrojs/starlight/route-data';
import { content } from '../scripts/release-source.mjs';
import { github } from '../scripts/site.mjs';

export const onRequest = defineRouteMiddleware((context) => {
  context.locals.starlightRoute.entry.data.banner = {
    content: content.kind === 'release'
      ? `Documentation for released version <a href="${github}/releases/tag/${content.tag}">${content.version}</a>.`
      : 'Development documentation — unreleased. Commands may be unavailable in published packages.',
  };
  if (/\/404(?:\.html)?\/?$/.test(context.url.pathname)) {
    // Pages serves this fallback at unknown URLs; it has no indexable canonical route.
    context.locals.starlightRoute.head = context.locals.starlightRoute.head.filter(
      (entry) => entry.attrs?.rel !== 'canonical',
    );
    context.locals.starlightRoute.head.push({
      tag: 'meta', attrs: { name: 'robots', content: 'noindex, follow' },
    });
  }
});
