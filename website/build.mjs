import { cpSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = dirname(fileURLToPath(import.meta.url));
const config = JSON.parse(readFileSync(join(root, "site.config.json"), "utf8"));

for (const key of ["version", "releaseTag", "repository"]) {
  if (!config[key]) throw new Error(`site.config.json: "${key}" is required.`);
}
if (!/^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$/.test(config.version)) throw new Error(`"${config.version}" is not a SemVer version.`);
if (config.releaseTag !== `windows/v${config.version}`) throw new Error(`releaseTag must be windows/v${config.version}.`);
if (!/^https:\/\/github\.com\/[\w.-]+\/[\w.-]+$/.test(config.repository)) throw new Error("repository must be an https://github.com/owner/name URL.");

const siteUrl = config.siteUrl.replace(/\/+$/, "");
const release = `${config.repository}/releases/tag/${config.releaseTag}`;
const asset = (arch) => `${config.repository}/releases/download/${config.releaseTag}/Kumo-${config.version}-win-${arch}-setup.exe`;

const values = {
  version: config.version,
  repository: config.repository,
  repositoryLabel: config.repository.replace(/^https:\/\//, ""),
  releasePage: release,
  releases: `${config.repository}/releases`,
  license: `${config.repository}/blob/main/LICENSE`,
  downloadX64: asset("x64"),
  downloadArm64: asset("arm64"),
  socialMeta: siteUrl
    ? [
        `<link rel="canonical" href="${siteUrl}/">`,
        `<meta property="og:url" content="${siteUrl}/">`,
        `<meta property="og:image" content="${siteUrl}/assets/og.jpg">`,
        `<meta name="twitter:image" content="${siteUrl}/assets/og.jpg">`,
      ].join("\n    ")
    : "",
};

let html = readFileSync(join(root, "src", "index.html"), "utf8");
html = html.replace(/\{\{(\w+)\}\}/g, (match, key) => {
  if (!(key in values)) throw new Error(`Unknown placeholder ${match} in src/index.html.`);
  return values[key];
});

const dist = join(root, "dist");
if (existsSync(dist)) rmSync(dist, { recursive: true });
mkdirSync(dist);
cpSync(join(root, "src"), dist, { recursive: true, filter: (path) => !path.endsWith("index.html") });
writeFileSync(join(dist, "index.html"), html);
if (!siteUrl) console.warn("siteUrl is empty: canonical and absolute Open Graph image tags were left out.");
console.log(`Built Kumo ${config.version} site into dist/.`);
