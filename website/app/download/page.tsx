import type { Metadata } from "next";
import Link from "next/link";
import DownloadPicker from "@/components/DownloadPicker";
import Landscape from "@/components/Landscape";
import { site } from "@/lib/site";

export const metadata: Metadata = {
  title: "Download",
  description: `Download Kumo ${site.version} for Windows 10 and 11, for x64 and ARM64 PCs.`,
};

export default function Download() {
  return (
    <>
      <div className="sky download-hero">
        <div className="wrap download-head">
          <h1>Download</h1>
          <p className="download-sub">Pick your system to get started</p>
          <p className="download-version">
            Kumo {site.version} · <Link href="/changelog/">What&apos;s new</Link>
          </p>
        </div>
        <Landscape />
      </div>

      <div className="wrap download-page">
        <DownloadPicker />
      </div>
    </>
  );
}
