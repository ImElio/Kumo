import Link from "next/link";
import { links, site } from "@/lib/site";
import Doodle from "./Doodle";

export default function SiteFooter() {
  return (
    <footer className="site-footer">
      <div className="wrap footer-inner">
        <span className="footer-brand">
          <Doodle name="cloud" size={28} accent={false} />
          Kumo {site.version}
        </span>
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
            <a href={links.downloadArm64}>ARM64 installer</a>
          </li>
          <li>
            <a href={links.license}>License</a>
          </li>
        </ul>
      </div>
    </footer>
  );
}
