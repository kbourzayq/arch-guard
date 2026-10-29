import { defineRouteMiddleware } from '@astrojs/starlight/route-data';

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
