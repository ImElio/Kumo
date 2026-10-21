import type { Metadata } from "next";
import { links } from "@/lib/site";

export const metadata: Metadata = {
  title: "Privacy & cookies",
  description: "How the Kumo website, demo and Windows app handle data and browser storage.",
};

export default function PrivacyPage() {
  return (
    <>
      <div className="page-head sky sky-soft">
        <div className="wrap">
          <h1 className="page-title">Privacy &amp; cookies</h1>
          <p>What the Kumo website and Windows app store, and what they send over the network.</p>
        </div>
      </div>

      <article className="privacy-page wrap">
        <p className="privacy-updated">Last updated: October 21, 2026</p>

        <h2>In short</h2>
        <p>
          Kumo has no account system, advertising or analytics. The Windows app processes files on your computer. The
          website stores a few interface preferences in your browser, and the app can optionally contact GitHub to check
          for updates.
        </p>

        <h2>Website storage</h2>
        <p>
          Kumo does not set advertising or analytics cookies. It uses browser local storage for functional preferences:
        </p>
        <ul>
          <li>
            <code>kumo-theme</code> remembers your light or dark theme.
          </li>
          <li>
            <code>kumo-demo-tips</code> remembers if you hid the demo instructions.
          </li>
          <li>
            <code>kumo-privacy-notice-v1</code> remembers that you dismissed the privacy notice banner.
          </li>
        </ul>
        <p>These values stay in your browser and can be removed by clearing the site&apos;s stored data.</p>

        <h2>Interactive demo</h2>
        <p>
          The demo uses sample files included with the website. Its actions create simulated results in the page; it does
          not upload or process files from your computer.
        </p>

        <h2>Windows app and update checks</h2>
        <p>
          Kumo processes your files locally and does not send their contents or paths to us. If automatic update checks
          are enabled, Kumo contacts the GitHub Releases API no more than once a day. The request identifies Kumo and its
          version in its User-Agent; GitHub also receives ordinary connection information such as your IP address. GitHub
          handles that request under its own <a href="https://docs.github.com/en/site-policy/privacy-policies/github-privacy-statement">Privacy Statement</a>.
        </p>

        <h2>Hosting and external links</h2>
        <p>
          The website is a static export. Like other websites, the service hosting it may receive technical connection
          data when your browser requests a page. The exact data and retention depend on the hosting provider. Links to
          GitHub, including downloads and release notes, take you to a service governed by GitHub&apos;s policies.
        </p>
        <p>
          During the site build, release notes are read from GitHub. Visitors&apos; browsers do not make that release-notes
          API request.
        </p>

        <h2>Questions</h2>
        <p>
          For questions about Kumo, use the <a href={links.repository}>project repository</a>. Please do not post personal
          or confidential information in a public issue.
        </p>
      </article>
    </>
  );
}
