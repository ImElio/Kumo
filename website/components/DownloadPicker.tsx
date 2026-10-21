"use client";

import { useEffect, useState } from "react";
import { links } from "@/lib/site";
import Doodle from "./Doodle";

type Os = "windows" | "linux" | "mac" | "mobile" | "other";
type Arch = "x64" | "arm64" | "x86" | null;
type Detected = { os: Os; name: string; arch: Arch };

type UserAgentData = {
  mobile?: boolean;
  getHighEntropyValues?: (hints: string[]) => Promise<{ architecture?: string; bitness?: string; platformVersion?: string }>;
};

async function detect(): Promise<Detected> {
  const ua = navigator.userAgent;
  const data = (navigator as Navigator & { userAgentData?: UserAgentData }).userAgentData;
  // iPadOS asks for desktop sites and reports itself as a Mac; touch support gives it away.
  if (data?.mobile || /Android|iPhone|iPad|iPod/i.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1)) {
    return { os: "mobile", name: "a phone or tablet", arch: null };
  }
  if (/Windows/.test(ua)) {
    let name = "Windows";
    let arch: Arch = /ARM64|aarch64/i.test(ua) ? "arm64" : null;
    try {
      // Chromium only. Every Windows browser reports "Windows NT 10.0", so Windows 11 is told apart by this version.
      const hints = await data?.getHighEntropyValues?.(["architecture", "bitness", "platformVersion"]);
      if (hints?.architecture === "arm") arch = "arm64";
      else if (hints?.architecture === "x86") arch = hints.bitness === "32" ? "x86" : "x64";
      const major = Number(hints?.platformVersion?.split(".")[0]);
      if (major >= 13) name = "Windows 11";
      else if (major > 0) name = "Windows 10";
    } catch {
      // Fall back to the user agent.
    }
    return { os: "windows", name, arch };
  }
  if (/Mac/.test(ua)) return { os: "mac", name: "macOS", arch: null };
  if (/Linux|CrOS/.test(ua)) return { os: "linux", name: "Linux", arch: null };
  return { os: "other", name: "", arch: null };
}

const archName = { x64: "x64", arm64: "ARM64", x86: "32-bit" };

export default function DownloadPicker() {
  const [detected, setDetected] = useState<Detected | null>(null);
  const [tab, setTab] = useState<"windows" | "linux">("windows");

  useEffect(() => {
    detect().then((d) => {
      setDetected(d);
      if (d.os === "linux") setTab("linux");
    });
  }, []);

  const os = detected?.os;
  const preferred = os === "windows" ? (detected?.arch === "arm64" ? "arm64" : "x64") : null;
  const yours = detected && (os === "windows" || os === "linux") ? `${detected.name}${detected.arch ? ` · ${archName[detected.arch]}` : ""}` : null;

  return (
    <>
      <div className="os-cards">
        <article className={`os-card os-card-windows ${os === "windows" ? "os-card-detected" : ""}`} aria-labelledby="os-windows">
          <WindowsScene />
          {os === "windows" && <p className="os-yours">Your system · {yours}</p>}
          <div className="os-card-body">
            <WindowsLogo />
            <h2 id="os-windows">Windows</h2>
            <div className="os-split" role="group" aria-label="Kumo for Windows installers">
              <a className={preferred === "x64" ? "os-split-main" : undefined} href={links.downloadX64}>
                <Doodle name="download" size={20} accent={false} /> x64
              </a>
              <a className={preferred === "arm64" ? "os-split-main" : undefined} href={links.downloadArm64}>
                <Doodle name="download" size={20} accent={false} /> ARM64
              </a>
            </div>
          </div>
          <p className="os-card-foot">Supports Windows 10 (2004) and above</p>
        </article>

        <article className={`os-card os-card-linux ${os === "linux" ? "os-card-detected" : ""}`} aria-labelledby="os-linux">
          <LinuxScene />
          {os === "linux" && <p className="os-yours">Your system · {yours}</p>}
          <div className="os-card-body">
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img className="os-logo os-tux" src="/assets/tux.svg" width={216} height={256} alt="" />
            <h2 id="os-linux">Linux</h2>
            <span className="os-soon">Coming soon</span>
          </div>
          <p className="os-card-foot">Not released yet</p>
        </article>
      </div>

      <p className="os-note" aria-live="polite">
        {detected?.arch === "x86" && "Kumo needs a 64-bit version of Windows. "}
        {preferred === "x64" && !detected?.arch && "Not sure which one? x64 fits most PCs; ARM64 is for PCs with an ARM processor, such as a Snapdragon."}
        {(os === "mac" || os === "mobile") && `You're on ${detected?.name}. Kumo is a Windows app for now, but you can download the installer here for your PC.`}
      </p>

      <section className="install" aria-labelledby="install-title">
        <div className="install-head">
          <h2 id="install-title">Installation instructions</h2>
          <div className="install-tabs" role="tablist" aria-label="System">
            {(["windows", "linux"] as const).map((t) => (
              <button key={t} type="button" role="tab" id={`tab-${t}`} aria-selected={tab === t} aria-controls={`panel-${t}`} onClick={() => setTab(t)}>
                {t === "windows" ? "Windows" : "Linux"}
              </button>
            ))}
          </div>
        </div>

        {tab === "windows" ? (
          <div className="install-grid" role="tabpanel" id="panel-windows" aria-labelledby="tab-windows">
            <ol className="install-steps">
              <li>
                <Doodle name="hourglass" size={34} />
                <div>
                  <strong>Wait for the download to finish</strong>
                  <p>It&apos;s a single setup file with everything Kumo needs, .NET included.</p>
                </div>
              </li>
              <li>
                <Doodle name="download" size={34} />
                <div>
                  <strong>Run the installer</strong>
                  <p>
                    It isn&apos;t code-signed yet, so SmartScreen may stop it the first time. Choose <em>More info</em>, then{" "}
                    <em>Run anyway</em>.
                  </p>
                </div>
              </li>
              <li>
                <Doodle name="cloud" size={34} />
                <div>
                  <strong>Drag some files and tap Shift</strong>
                  <p>Kumo waits in the notification area, next to the clock. Its menu opens wherever you are dragging.</p>
                </div>
              </li>
            </ol>
          </div>
        ) : (
          <div className="install-grid install-later" role="tabpanel" id="panel-linux" aria-labelledby="tab-linux">
            <p>
              Kumo for Linux isn&apos;t out yet. When it is, it will be on this page and in the{" "}
              <a href="/changelog/">changelog</a>.
            </p>
          </div>
        )}
      </section>
    </>
  );
}

function WindowsLogo() {
  return (
    <svg className="os-logo" viewBox="0 0 64 64" aria-hidden="true" focusable="false">
      <path d="M6 10.5 28 7.4v22.4H6ZM31.5 6.9 58 3v26.8H31.5ZM6 33.3h22v22.4L6 52.6ZM31.5 33.3H58V61l-26.5-3.8Z" fill="#fff" />
    </svg>
  );
}

function WindowsScene() {
  return (
    <svg className="os-scene" viewBox="0 0 400 520" preserveAspectRatio="xMidYMid slice" aria-hidden="true" focusable="false">
      <defs>
        <linearGradient id="scene-day" x1="0" x2="0" y1="0" y2="1">
          <stop offset="0" stopColor="#e3907a" />
          <stop offset="1" stopColor="#f7cfa6" />
        </linearGradient>
      </defs>
      <rect width="400" height="520" fill="url(#scene-day)" />
      <circle cx="300" cy="300" r="58" fill="#ffe3b8" />
      <path d="M44 120h86a16 16 0 0 0 0-32 24 24 0 0 0-44-10 18 18 0 0 0-30 12 15 15 0 0 0-12 30Z" fill="#fff" opacity=".55" />
      <path d="M250 70h60a11 11 0 0 0 0-22 17 17 0 0 0-31-7 13 13 0 0 0-21 8 10 10 0 0 0-8 21Z" fill="#fff" opacity=".45" />
      <path d="M0 330c70-30 140-24 210-6s130-30 190-14v210H0Z" fill="#d6866a" />
      <path d="M0 390c90-26 170-18 250 2s110-14 150-8v136H0Z" fill="#c06e57" />
      <path d="M0 450c110-22 220-14 300 4 40 9 70 6 100 2v64H0Z" fill="#a65c49" />
    </svg>
  );
}

function LinuxScene() {
  return (
    <svg className="os-scene" viewBox="0 0 400 520" preserveAspectRatio="xMidYMid slice" aria-hidden="true" focusable="false">
      <defs>
        <linearGradient id="scene-night" x1="0" x2="0" y1="0" y2="1">
          <stop offset="0" stopColor="#261c33" />
          <stop offset="1" stopColor="#5b3b58" />
        </linearGradient>
        <mask id="scene-crescent">
          <circle cx="96" cy="150" r="34" fill="#fff" />
          <circle cx="112" cy="139" r="30" fill="#000" />
        </mask>
      </defs>
      <rect width="400" height="520" fill="url(#scene-night)" />
      <rect width="400" height="520" fill="#f4e6cf" mask="url(#scene-crescent)" />
      {[
        [40, 60], [150, 40], [230, 90], [320, 50], [360, 140], [270, 190], [190, 160], [30, 230], [350, 250],
      ].map(([x, y]) => (
        <circle key={`${x}-${y}`} cx={x} cy={y} r="2" fill="#f4e6cf" opacity=".8" />
      ))}
      <path d="M0 330c70-30 140-24 210-6s130-30 190-14v210H0Z" fill="#45304d" />
      <path d="M0 390c90-26 170-18 250 2s110-14 150-8v136H0Z" fill="#36263f" />
      <path d="M0 450c110-22 220-14 300 4 40 9 70 6 100 2v64H0Z" fill="#271c2f" />
    </svg>
  );
}
