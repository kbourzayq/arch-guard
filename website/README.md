# Documentation website

The static Astro/Starlight site targets
[kbourzayq.github.io/arch-guard](https://kbourzayq.github.io/arch-guard/).
It has no backend, analytics, trackers, cookies or external runtime services.
Search uses a locally built Pagefind index. IBM Plex Sans and JetBrains Mono are
self-hosted with system fallbacks; no font CDN is used.

## Local development

Install Node.js **24.17.0** (recorded in `.node-version`) and its bundled npm.
From the repository root:

```sh
cd website
npm ci
npm run dev
```

Open the printed URL, including `/arch-guard/`. To validate and preview production
output, including search:

```sh
npm run build
npm run preview
```

The build runs failure-path tests, synchronizes source documents, checks
Astro/TypeScript, renders static pages and search/sitemap output, then checks every
built HTML page for missing internal links, fragment anchors, images/scripts/styles,
base-path escapes, skipped heading levels and pages outside the explicit publication
manifest. Synchronization or link errors fail
the build. External websites are not network-checked; repository-relative source
targets must exist before they can become GitHub links.

Use `npm run sync` to regenerate content alone and `npm test` for fixture tests.
No .NET runtime is needed to build the website. Dependencies and their lock file
live here, independently of the solution and NuGet setup. Set the optional
`ASTRO_TELEMETRY_DISABLED=1` environment variable to disable Astro's build telemetry;
CI already does. The website itself has no visitor telemetry.

## Content sources and navigation

`scripts/site.mjs` defines the source-to-page map and sidebar order. Each item holds
the repository source path, stable slug, navigation label and optional metadata
for generated reports without a source title. Each source appears
once. The groups reuse whole guides instead of duplicating their sections:

| Source | Website route or role |
| --- | --- |
| `docs/install.md` | `/arch-guard/reference/installation/`; released-package installation |
| `docs/getting-started.md` | `/arch-guard/reference/getting-started/`; first policy, scan and violation |
| `docs/policy.md` | `/arch-guard/reference/policy/`; modules, layers, contracts and YAML |
| `docs/concepts/` and other mapped `docs/` guides | `/arch-guard/reference/<slug>/`; consumer concepts, integrations, benchmarks, advanced guides and reference |
| `docs/releases.md`, `CHANGELOG.md` | Consumer compatibility and release notes |
| `website/src/content/docs/index.mdx`, `src/components/` | Website-specific homepage |
| `website/public/` and `src/assets/` | Favicon, social preview and SVG identity |

Edit the authoritative Markdown and rebuild. Generated Markdown in
`src/content/docs/reference/` and copied images in `public/source-assets/` are
ignored and replaced on each sync. Never edit or commit them. “Edit this page”
points to the original source on GitHub; the homepage has its own edit link.

The manifest deliberately excludes ADRs, coding conventions, local development,
repository validation, benchmark development, release engineering and website
development. Those remain on GitHub. Mixed guides are split at source: public
guides retain operational/product behavior and separate maintenance documents
retain implementation procedures. Removing a source from the manifest removes
its generated route, search entry and sitemap entry. Built HTML outside that
manifest fails validation, catching accidental or stale publication.

`scripts/examples.mjs` packages the two existing reference solutions with their
standalone build/package settings and license into deterministic ZIP downloads.
Only source/project/policy inputs are included; `bin/` and `obj/` are excluded.
Archives under `public/examples/` are ignored and rebuilt during synchronization.
Validate extracted downloads with the installed CLI whenever sample inputs or
packaging change. Source Markdown uses public Pages URLs; synchronization converts same-site URLs
to base-prefixed paths so previews use their local downloads.

The synchronizer parses Markdown/GFM without changing code-fence contents. It
retains tables, examples, reference links and raw HTML, uses the original title
as the page title, and keeps its GitHub fragment anchor. Mapped document links
become base-prefixed website links with fragments preserved. Images are copied
to base-prefixed asset paths. Other existing repository files/directories become
GitHub blob/tree links. Absolute external links remain unchanged. Missing targets,
missing titles and duplicate mappings fail synchronization; missing rendered
fragments fail production validation. The generated changelog receives the
website title “Release notes” without changing its version headings.

To add a guide, write its authoritative source and add one mapping to the relevant
group. Do not add placeholder pages for navigation labels. Update incoming
website-specific links when changing a slug. Development synchronizes at startup;
rerun `npm run sync` after editing a source outside `website/`, or restart the dev
server.

The homepage policy imports the committed Clean Architecture fixture directly.
Its diagnostic excerpt is the actual first diagnostic line and completed summary
from this command (run from the repository root after building the CLI):

```sh
dotnet restore tests/Fixtures/ReferenceSolutions/CleanArchitecture/CleanArchitecture.slnx -p:Configuration=AG002
dotnet src/ArchitectureGuardian.Cli/bin/Release/net10.0/ArchitectureGuardian.Cli.dll scan tests/Fixtures/ReferenceSolutions/CleanArchitecture/CleanArchitecture.slnx --policy tests/Fixtures/ReferenceSolutions/CleanArchitecture/architecture.guardian.yaml --configuration AG002 --trusted
```

Expected exit: `1`. Update the excerpt when the fixture or output changes. Keep
versioned installation examples in their current authoritative files so `.cz.toml`
continues synchronizing releases.

## Visual and interaction checks

Preview the homepage and a long guide at desktop and mobile widths in both light
and dark themes. Verify:

- No horizontal page overflow, usable tables, and wrapping code blocks.
- Keyboard search opens, returns results with base-prefixed links, and closes with
  Escape. Search requires the production build/preview.
- Theme switching, mobile menu, sidebar, table of contents, previous/next links,
  authoritative edit links and code-copy buttons work.
- Tab navigation shows visible focus, skip navigation reaches main content,
  controls have labels, text contrast is readable, and headings follow a logical order.
- Reduced-motion preferences are respected.

Starlight supplies search, navigation, theme switching and copy controls.
Mermaid fences are syntax-validated during synchronization and rendered by the
official Mermaid package, bundled locally and loaded only on diagram pages. No
CDN or diagram service is used. Diagrams follow theme changes and provide a
keyboard-accessible expanded view with zoom/fit controls, SVG download and
collapsible copyable source.
If JavaScript is unavailable, the original source remains readable. Check the diagrams in the consumer concept guides under `docs/concepts/` after
changing Mermaid, including both themes and the mobile expanded view.
`src/styles/custom.css` adds the shared palette and responsive homepage layouts.
The design follows a restrained Swiss-style grid with the Obsidian & Coral palette:
`#111419` background, `#1D232B` sidebar/panels, `#FF7868` accents and `#F4F2F0` text.
The light variant uses a darker coral for readable links. Both themes retain
IBM Plex Sans for reading, JetBrains Mono for code, visible focus and a bounded
reading column. Font license texts are included under `public/font-licenses/`.
The 1200×630 social PNG is exported from its adjacent editable SVG; update and
commit both when changing it.

## GitHub Pages deployment

One time, a repository administrator must:

1. Select **Settings → Pages → Build and deployment → Source → GitHub Actions**.
2. Allow Actions and the official checkout, Node setup and Pages actions in this
   repository. No personal token or deployment secret is needed.
3. If the `github-pages` environment has protection rules, permit `main` and
   satisfy any required maintainer approval.

`.github/workflows/docs.yml` runs `npm ci` and full production validation for
relevant pull requests. PR jobs have only `contents: read` and cannot deploy.
Relevant pushes to `main` build and upload a Pages artifact; a separate job uses
`configure-pages` and `deploy-pages` with `pages: write` and `id-token: write`.
Manual runs deploy only when the selected ref is `main`. The npm cache keys off
`website/package-lock.json`; path filters include mapped sources, website inputs,
the published benchmark report/illustration, and all reference-example inputs.

`scripts/site.mjs` supplies Astro's origin `https://kbourzayq.github.io` and base
`/arch-guard/`. Canonicals, navigation, search, sitemap, robots and assets use this
destination. When changing it, update these constants and absolute edit links,
then repeat the build and browser checks.

Creating the workflow does not enable Pages settings or deploy a feature branch.
Deployment happens after merge and repository setup. Verify the Actions deployment
result before claiming the site is live.
