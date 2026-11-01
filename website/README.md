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
These default commands preview development documentation with an unreleased notice.
To build published content locally after fetching its tag:

```sh
git fetch origin --tags
DOCS_RELEASE_TAG=v0.3.0 npm run build
```

In PowerShell, set `$env:DOCS_RELEASE_TAG = 'v0.3.0'` before the build and remove it
afterward with `Remove-Item Env:DOCS_RELEASE_TAG`. Release builds materialize tagged
Git content under ignored `artifacts/docs-source/` without switching the checkout.
The version banner and generated `public/release.json` identify the content tag,
version and commit. Missing/mismatched tags fail without substituting main's text.
Deployment additionally verifies public package availability.

No .NET runtime is needed to build the website. Dependencies and their lock file
live here, independently of the solution and NuGet setup. Set the optional
`ASTRO_TELEMETRY_DISABLED=1` environment variable to disable Astro's build telemetry;
CI already does. The website itself has no visitor telemetry.

## Content sources and navigation

`scripts/site.mjs` defines the source-to-page map and sidebar order. Each item holds
the repository source path, stable slug, navigation label and optional metadata
for generated reports without a source title. Each source appears
once. The groups reuse whole guides instead of duplicating their sections:

Public builds read guides, assets, the homepage sample policy and example ZIPs from
the selected released tag. Absent guides are omitted from navigation, pages, search
and sitemap; mixed guides such as the CLI reference use released contents. Releases
predating the website explicitly map the tagged root README to installation and
`docs/m1-demo.md` to examples. Source links stay pinned to the release. The renderer,
theme and navigation labels come from main; homepage feature claims must remain
compatible with the supported published release.

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

`.github/workflows/docs.yml` validates both development previews and published-release
builds for relevant PRs. PR jobs have only `contents: read` and cannot deploy.
Public builds select GitHub's latest published stable release and verify all three
matching NuGet versions before rendering tagged content. Drafts, prereleases,
missing packages and unavailable release metadata stop deployment and retain the
previous site. There is no development-content fallback.

Relevant pushes to main, stable GitHub release publication, successful completion
of the repository's tag-triggered NuGet release workflow, and manual runs on main
can refresh Pages. The renderer comes from main, including for older tags without
a website; the uploaded artifact always contains released content. Production
updates are serialized. Separate deployment uses `configure-pages`/`deploy-pages`
with `pages: write` and `id-token: write`. The npm cache keys off
`website/package-lock.json`; path filters include mapped sources, website inputs,
the published benchmark report/illustration, and all reference-example inputs.

`scripts/site.mjs` supplies Astro's origin `https://kbourzayq.github.io` and base
`/arch-guard/`. Canonicals, navigation, search, sitemap, robots and assets use this
destination. When changing it, update these constants and absolute edit links,
then repeat the build and browser checks.

Merging a feature updates source documentation and its preview; public content
advances after the corresponding stable release and packages are published. Theme
and renderer fixes can deploy while retaining released content. If a release event
precedes NuGet propagation, rerun the docs job after all three versions are visible.
GitHub loads [release-triggered workflow definitions](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#release)
from the release tag. When
publishing a legacy/backport tag without `.github/workflows/docs.yml`, manually run
the docs workflow on main after publication; checking out main within a job cannot
create a missing trigger. The main push introducing this workflow repairs the
current site using the latest verified release even when that release predates it.
Verify the Actions deployment result before claiming the site is updated.
