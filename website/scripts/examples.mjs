import { readFile, readdir, mkdir, rm, writeFile } from 'node:fs/promises';
import { dirname, resolve, relative } from 'node:path';
import { fileURLToPath } from 'node:url';
import { zipSync } from 'fflate';

const repository = resolve(dirname(fileURLToPath(import.meta.url)), '../..');
const fixtures = resolve(repository, 'tests/Fixtures/ReferenceSolutions');
export const examples = [
  { directory: 'CleanArchitecture', slug: 'clean-architecture', violation: 'AG002' },
  { directory: 'ModularMonolith', slug: 'modular-monolith', violation: 'AG001' },
];

export async function createExampleArchive(example) {
  const entries = {};
  const source = resolve(fixtures, example.directory);
  async function collect(directory) {
    for (const item of (await readdir(directory, { withFileTypes: true })).sort((a, b) => a.name.localeCompare(b.name, 'en'))) {
      const path = resolve(directory, item.name);
      if (item.isDirectory() && !['bin', 'obj'].includes(item.name)) await collect(path);
      else if (item.isFile() && /\.(cs|csproj|slnx|yaml)$/.test(item.name)) {
        entries[`${example.directory}/${relative(source, path).replaceAll('\\', '/')}`] = new Uint8Array(await readFile(path));
      }
    }
  }
  await collect(source);
  for (const name of ['Directory.Build.props', 'Directory.Packages.props']) {
    entries[`${example.directory}/${name}`] = new Uint8Array(await readFile(resolve(fixtures, name)));
  }
  entries[`${example.directory}/LICENSE`] = new Uint8Array(await readFile(resolve(repository, 'LICENSE')));
  entries[`${example.directory}/NuGet.Config`] = new TextEncoder().encode('<configuration><packageSources><clear /></packageSources></configuration>\n');
  entries[`${example.directory}/README.md`] = new TextEncoder().encode(`# ${example.directory} example\n\nInstall the Architecture Guardian CLI and a .NET 10 SDK. From this directory:\n\n\`\`\`sh\ndotnet restore ${example.directory}.slnx -p:Configuration=Compliant\narchguard scan ${example.directory}.slnx --policy architecture.guardian.yaml --configuration Compliant --trusted\n\`\`\`\n\nExpect exit 0. Repeat restore/scan with configuration ${example.violation} for exit 1.\nAuthorize this sample's build logic before restoring/scanning. No packages are required.\nSee https://kbourzayq.github.io/arch-guard/reference/examples/ for walkthroughs.\n`);
  // Use a fixed local calendar date; ZIP timestamps then match across time zones.
  return zipSync(entries, { level: 9, mtime: new Date(2020, 0, 1) });
}

export async function buildExamples() {
  const destination = resolve(repository, 'website/public/examples');
  await rm(destination, { recursive: true, force: true });
  await mkdir(destination, { recursive: true });
  for (const example of examples) await writeFile(resolve(destination, `${example.slug}.zip`), await createExampleArchive(example));
}
