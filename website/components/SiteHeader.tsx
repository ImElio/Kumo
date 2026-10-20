import Link from "next/link";
import { links } from "@/lib/site";
import ThemeToggle from "./ThemeToggle";

export default function SiteHeader() {
  return (
    <header className="site-header">
      <div className="wrap header-inner">
        <Link className="brand" href="/" aria-label="Kumo, home">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img src="/assets/kumo-64.png" width={32} height={32} alt="" />
          <span>Kumo</span>
        </Link>
        <nav aria-label="Main">
          <ul>
            <li>
              <Link href="/#features">Features</Link>
            </li>
            <li>
              <Link href="/changelog/">Changelog</Link>
            </li>
            <li className="nav-github">
              <a href={links.repository}>GitHub</a>
            </li>
            <li>
              <Link className="nav-download" href="/#download">
                Download
              </Link>
            </li>
            <li>
              <ThemeToggle />
            </li>
          </ul>
        </nav>
      </div>
    </header>
  );
}
