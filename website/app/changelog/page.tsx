import type { Metadata } from "next";
import { getWindowsReleases } from "@/lib/releases";
import { links } from "@/lib/site";

export const metadata: Metadata = {
  title: "Changelog",
  description: "What changed in each release of Kumo for Windows.",
};

const dateFormat = new Intl.DateTimeFormat("en-US", { year: "numeric", month: "long", day: "numeric", timeZone: "UTC" });

export default async function Changelog() {
  const releases = await getWindowsReleases();

  return (
    <>
      <div className="page-head sky sky-soft">
        <div className="wrap">
          <h1 className="page-title">Changelog</h1>
        </div>
      </div>

      <div className="releases wrap">
        {releases === null && (
          <p className="empty">
            The release notes couldn&apos;t be loaded when this page was built. They are all on{" "}
            <a href={links.releases}>GitHub</a>.
          </p>
        )}
        {releases?.length === 0 && <p className="empty">No releases yet.</p>}
        {releases?.map((release, index) => (
          <details className="release" key={release.tag} id={`v${release.version}`} open={index === 0}>
            <summary className="release-summary">
              <span className="release-meta">
                <span className="release-version">{release.version}</span>
                {release.publishedAt && (
                  <span className="release-date">
                    <time dateTime={release.publishedAt}>{dateFormat.format(new Date(release.publishedAt))}</time>
                  </span>
                )}
                {release.prerelease && <span className="tag">Pre-release</span>}
              </span>
              <span className="release-name">{release.name}</span>
              <span className="release-chevron" aria-hidden="true" />
            </summary>
            <div className="notes">
              {release.html ? (
                <div dangerouslySetInnerHTML={{ __html: release.html }} />
              ) : (
                <p>No notes were published for this release.</p>
              )}
            </div>
          </details>
        ))}
      </div>
    </>
  );
}
