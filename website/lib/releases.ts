import { site } from "./site";

export type Release = {
  tag: string;
  version: string;
  name: string;
  publishedAt: string | null;
  prerelease: boolean;
  /** Release notes as HTML rendered and sanitized by GitHub. */
  html: string;
};

type GitHubRelease = {
  tag_name: string;
  name: string | null;
  published_at: string | null;
  draft: boolean;
  prerelease: boolean;
  body_html?: string;
};

const PREFIX = "windows/v";

/**
 * Published Windows releases, newest version first. Read once at build time; the site is rebuilt for each release.
 * Returns null when GitHub cannot be reached, so the page can link to GitHub instead of failing the build.
 */
export async function getWindowsReleases(): Promise<Release[] | null> {
  const [owner, name] = new URL(site.repository).pathname.slice(1).split("/");
  const headers: Record<string, string> = {
    Accept: "application/vnd.github.html+json",
    "X-GitHub-Api-Version": "2022-11-28",
    "User-Agent": "kumo-website",
  };
  if (process.env.GITHUB_TOKEN) headers.Authorization = `Bearer ${process.env.GITHUB_TOKEN}`;

  let response: Response;
  try {
    response = await fetch(`https://api.github.com/repos/${owner}/${name}/releases?per_page=100`, { headers, cache: "force-cache" });
  } catch (error) {
    console.warn(`Releases could not be loaded from GitHub: ${(error as Error).message}`);
    return null;
  }
  if (!response.ok) {
    console.warn(`Releases could not be loaded from GitHub: HTTP ${response.status}`);
    return null;
  }

  const releases = (await response.json()) as GitHubRelease[];
  return releases
    .filter((r) => !r.draft && r.tag_name.startsWith(PREFIX) && parse(r.tag_name.slice(PREFIX.length)))
    .map((r) => ({
      tag: r.tag_name,
      version: r.tag_name.slice(PREFIX.length),
      name: r.name || `Kumo ${r.tag_name.slice(PREFIX.length)}`,
      publishedAt: r.published_at,
      prerelease: r.prerelease,
      html: r.body_html ?? "",
    }))
    .sort((a, b) => compare(b.version, a.version));
}

type SemVer = { core: number[]; pre: string[] };

function parse(version: string): SemVer | null {
  const match = /^(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?$/.exec(version);
  if (!match) return null;
  return { core: [match[1], match[2], match[3]].map(Number), pre: match[4] ? match[4].split(".") : [] };
}

/** SemVer 2.0.0 precedence, so 1.0.0-rc.10 sorts after 1.0.0-rc.2. */
export function compare(a: string, b: string): number {
  const x = parse(a)!;
  const y = parse(b)!;
  for (let i = 0; i < 3; i++) if (x.core[i] !== y.core[i]) return x.core[i] - y.core[i];
  if (!x.pre.length || !y.pre.length) return y.pre.length - x.pre.length;
  for (let i = 0; i < Math.max(x.pre.length, y.pre.length); i++) {
    const p = x.pre[i];
    const q = y.pre[i];
    if (p === undefined) return -1;
    if (q === undefined) return 1;
    const pn = /^\d+$/.test(p);
    const qn = /^\d+$/.test(q);
    if (pn && qn && Number(p) !== Number(q)) return Number(p) - Number(q);
    if (pn !== qn) return pn ? -1 : 1;
    if (p !== q) return p < q ? -1 : 1;
  }
  return 0;
}
