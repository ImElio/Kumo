"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { useEffect, useState } from "react";
import { links } from "@/lib/site";
import Doodle from "./Doodle";
import ThemeToggle from "./ThemeToggle";

export default function SiteHeader() {
  const pathname = usePathname();
  const [hash, setHash] = useState("");

  useEffect(() => {
    const updateHash = () => setHash(window.location.hash);
    updateHash();
    window.addEventListener("hashchange", updateHash);
    return () => window.removeEventListener("hashchange", updateHash);
  }, []);

  const featuresActive = pathname === "/" && hash === "#features";
  const changelogActive = pathname.startsWith("/changelog");
  const downloadActive = pathname.startsWith("/download");

  return (
    <header className="site-header" id="top">
      <div className="wrap header-inner">
        <Link className="brand" href="/" aria-label="Kumo, home">
          <Doodle name="cloud" size={38} accent={false} />
          <span>Kumo</span>
        </Link>
        <nav aria-label="Main">
          <ul>
            <li>
              <Link className={featuresActive ? "nav-active" : undefined} aria-current={featuresActive ? "page" : undefined} href="/#features">
                Features
              </Link>
            </li>
            <li>
              <Link className={changelogActive ? "nav-active" : undefined} aria-current={changelogActive ? "page" : undefined} href="/changelog/">
                Changelog
              </Link>
            </li>
            <li className="nav-github">
              <a href={links.repository}>GitHub</a>
            </li>
            <li>
              <Link className={downloadActive ? "nav-active" : undefined} aria-current={downloadActive ? "page" : undefined} href="/download/">
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
