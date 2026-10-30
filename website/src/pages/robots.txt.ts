import { base, site } from '../../scripts/site.mjs';

export function GET() {
  return new Response(`User-agent: *\nAllow: ${base}\nSitemap: ${site}${base}sitemap-index.xml\n`, {
    headers: { 'Content-Type': 'text/plain; charset=utf-8' },
  });
}
