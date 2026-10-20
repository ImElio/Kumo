import Link from "next/link";
import { links } from "@/lib/site";
import Doodle from "./Doodle";
import ThemeToggle from "./ThemeToggle";

export default function SiteHeader() {
  return (
    <header className="site-header">
      <div className="wrap header-inner">
        <Link className="brand" href="/" aria-label="Kumo, home">
          <Doodle name="cloud" size={38} accent={false} />
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
