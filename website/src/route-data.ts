import { defineRouteMiddleware } from '@astrojs/starlight/route-data';

// The documented release is shown by the header version label, not a page banner.
export const onRequest = defineRouteMiddleware((context) => {
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
