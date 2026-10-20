export const site = {
  version: "0.1.0",
  releaseTag: "windows/v0.1.0",
  repository: "https://github.com/ImElio/Kumo",
  /** Public address of the site, used for canonical and Open Graph links. Leave empty until the site is deployed. */
  url: "",
} as const;

const download = (arch: "x64" | "arm64") =>
  `${site.repository}/releases/download/${site.releaseTag}/Kumo-${site.version}-win-${arch}-setup.exe`;

export const links = {
  repository: site.repository,
  repositoryLabel: site.repository.replace(/^https:\/\//, ""),
  releases: `${site.repository}/releases`,
  releasePage: `${site.repository}/releases/tag/${site.releaseTag}`,
  license: `${site.repository}/blob/main/LICENSE`,
  downloadX64: download("x64"),
  downloadArm64: download("arm64"),
};
