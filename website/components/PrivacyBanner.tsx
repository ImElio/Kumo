"use client";

import Link from "next/link";
import { useEffect, useState } from "react";

const NOTICE_KEY = "kumo-privacy-notice-v1";

export default function PrivacyBanner() {
  const [visible, setVisible] = useState(false);

  useEffect(() => {
    try {
      setVisible(localStorage.getItem(NOTICE_KEY) !== "seen");
    } catch {
      setVisible(true);
    }
  }, []);

  const dismiss = () => {
    try {
      localStorage.setItem(NOTICE_KEY, "seen");
    } catch {
      // The notice can still be dismissed for this visit when storage is unavailable.
    }
    setVisible(false);
  };

  if (!visible) return null;

  return (
    <aside className="privacy-banner" aria-label="Privacy and cookies notice">
      <div className="privacy-banner-copy">
        <strong>Privacy &amp; cookies</strong>
        <p>This site saves functional preferences in your browser. It does not use analytics or advertising cookies.</p>
        <Link href="/privacy/">Read the privacy notice</Link>
      </div>
      <button className="privacy-banner-dismiss" type="button" onClick={dismiss}>
        Got it
      </button>
    </aside>
  );
}
