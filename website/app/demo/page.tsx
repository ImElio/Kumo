import type { Metadata } from "next";
import Link from "next/link";
import DemoBoot from "@/components/DemoBoot";
import Doodle from "@/components/Doodle";

export const metadata: Metadata = {
  title: "Try Kumo",
  description: "Explore Kumo's image and PDF actions on an interactive sample desktop.",
};

export default function DemoPage() {
  return (
    <section className="demo-page" aria-label="Kumo interactive demo">
      <header className="demo-window-bar">
        <Link className="demo-window-brand" href="/" aria-label="Back to Kumo home">
          <Doodle name="cloud" size={26} accent={false} />
          <span>Kumo</span>
        </Link>
        <span className="demo-window-title">Interactive preview · sample files · simulated actions · nothing uploaded</span>
        <Link className="demo-window-exit" href="/">
          Back to website
        </Link>
      </header>

      <div className="demo-window-stage">
        <DemoBoot />
      </div>

      <div className="demo-phone-note">
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img src="/mascot/idle.png" width={96} height={96} alt="" />
        <h1>Best on a computer</h1>
        <p>
          Kumo is a Windows app: you drag files with the mouse and tap <kbd>Shift</kbd>. Open this page on your PC to try the
          demo.
        </p>
        <Link className="button button-primary" href="/">
          Back to the website
        </Link>
      </div>
    </section>
  );
}
