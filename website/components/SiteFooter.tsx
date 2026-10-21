import Link from "next/link";
import { links, site } from "@/lib/site";
import Doodle from "./Doodle";

export default function SiteFooter() {
  return (
    <footer className="site-footer">
      <div className="footer-hills" aria-hidden="true">
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img className="footer-mascot" src="/mascot/idle.png" width={88} height={88} alt="" />
        <svg viewBox="0 0 1440 120" preserveAspectRatio="none">
          <path className="footer-hill-back" d="M0 70C180 30 360 40 540 64s380-46 560-30 250 34 340 20V120H0Z" />
          <path className="footer-hill-front" d="M0 96c200-34 420-30 640-6s470-26 800-4V120H0Z" />
        </svg>
      </div>

      <div className="footer-body">
        <div className="wrap footer-inner">
          <div className="footer-about">
            <Link className="footer-brand" href="/" aria-label="Kumo, home">
              <Doodle name="cloud" size={36} accent={false} />
              <span>Kumo</span>
            </Link>
            <p>Everyday file actions, right where you drag. Free and open source, for Windows.</p>
            <Link className="button button-primary footer-download" href="/download/">
              <Doodle name="download" size={20} accent={false} />
              Download
            </Link>
          </div>

          <nav className="footer-nav" aria-label="Footer navigation">
            <div>
              <h2>Kumo</h2>
              <ul>
                <li>
                  <Link href="/#features">Features</Link>
                </li>
                <li className="footer-demo">
                  <Link href="/demo/">Try the demo</Link>
                </li>
                <li>
                  <Link href="/changelog/">Changelog</Link>
                </li>
                <li>
                  <Link href="/download/">Download</Link>
                </li>
              </ul>
            </div>
            <div>
              <h2>Project</h2>
              <ul>
                <li>
                  <a href={links.repository}>GitHub</a>
                </li>
                <li>
                  <a href={links.releases}>Releases</a>
                </li>
                <li>
                  <a href={`${links.repository}/issues`}>Report a problem</a>
                </li>
                <li>
                  <a href={links.license}>MIT License</a>
                </li>
              </ul>
            </div>
          </nav>
        </div>

        <div className="wrap footer-bottom">
          <span>
            Kumo {site.version} · Made with <Doodle name="heart" size={18} /> by ImElio
          </span>
          <span className="footer-bottom-links">
            <Link href="/privacy/">Privacy &amp; cookies</Link>
            <a href="#top">Back to top ↑</a>
          </span>
        </div>
      </div>
    </footer>
  );
}
