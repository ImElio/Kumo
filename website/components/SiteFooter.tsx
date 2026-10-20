import Link from "next/link";
import { links, site } from "@/lib/site";

export default function SiteFooter() {
  return (
    <footer className="site-footer">
      <div className="wrap footer-inner">
        <span className="footer-brand">Kumo {site.version}</span>
        <ul>
          <li>
            <a href={links.repository}>GitHub</a>
          </li>
          <li>
            <Link href="/changelog/">Changelog</Link>
          </li>
          <li>
            <a href={links.releases}>Releases</a>
          </li>
          <li>
            <a href={links.license}>License</a>
          </li>
        </ul>
      </div>
    </footer>
  );
}
