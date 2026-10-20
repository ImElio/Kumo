"use client";

import { useEffect, useState } from "react";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { faApple, faLinux, faWindows } from "@fortawesome/free-brands-svg-icons";
import { faMicrochip, faQuestionCircle } from "@fortawesome/free-solid-svg-icons";
import { links } from "@/lib/site";

type Platform = "windows" | "linux" | "macos" | "other";

function detectPlatform(): Platform {
  const userAgent = navigator.userAgent.toLowerCase();
  const platform = navigator.platform.toLowerCase();
  const signature = `${userAgent} ${platform}`;

  if (signature.includes("windows")) return "windows";
  if (signature.includes("android")) return "other";
  if (signature.includes("linux") || signature.includes("x11")) return "linux";
  if (signature.includes("macintosh") || signature.includes("mac os") || signature.includes("macintel")) return "macos";
  return "other";
}

const platformDetails = {
  windows: { label: "Windows", icon: faWindows },
  linux: { label: "Linux", icon: faLinux },
  macos: { label: "macOS", icon: faApple },
  other: { label: "System not supported yet", icon: faQuestionCircle },
} satisfies Record<Platform, { label: string; icon: typeof faWindows }>;

export default function PlatformStatus() {
  const [platform, setPlatform] = useState<Platform | null>(null);

  useEffect(() => setPlatform(detectPlatform()), []);

  if (platform === null) {
    return (
      <p className="platform-status" role="status" aria-live="polite">
        <FontAwesomeIcon icon={faQuestionCircle} aria-hidden="true" />
        <span>Detecting your system…</span>
      </p>
    );
  }

  const details = platformDetails[platform];

  return (
    <div className={`platform-status platform-status-${platform}`} role="status" aria-live="polite">
      <FontAwesomeIcon icon={details.icon} aria-hidden="true" />
      <span>
        <strong>{details.label}</strong>
        {platform === "windows" && (
          <a className="platform-alternative" href={links.downloadArm64}>
            <FontAwesomeIcon icon={faMicrochip} aria-hidden="true" />
            ARM64 installer
          </a>
        )}
        {platform === "linux" && <span className="platform-detail">Linux support is coming in a future release.</span>}
        {platform === "macos" && <span className="platform-detail">Kumo is currently available for Windows.</span>}
        {platform === "other" && <span className="platform-detail">Kumo is currently available for Windows.</span>}
      </span>
    </div>
  );
}
