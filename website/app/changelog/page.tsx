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
          <p>
            Every release of Kumo for Windows. Kumo shows the same notes in Settings when an update is available, and installs
            it for you.
          </p>
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
        {releases?.map((release) => (
          <article className="release" key={release.tag} id={`v${release.version}`} aria-labelledby={`title-${release.version}`}>
            <header className="release-head">
              <h2 id={`title-${release.version}`}>{release.version}</h2>
              {release.publishedAt && (
                <p>
                  <time dateTime={release.publishedAt}>{dateFormat.format(new Date(release.publishedAt))}</time>
                </p>
              )}
              {release.prerelease && <span className="tag">Pre-release</span>}
            </header>
            <div className="notes">
              {release.html ? (
                <div dangerouslySetInnerHTML={{ __html: release.html }} />
              ) : (
                <p>No notes were published for this release.</p>
              )}
              <p className="release-links">
                <a href={release.url}>Downloads and checksums on GitHub</a>
              </p>
            </div>
          </article>
        ))}
      </div>
    </>
  );
}
