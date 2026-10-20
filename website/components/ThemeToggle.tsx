"use client";

import { useEffect, useState } from "react";

type Theme = "light" | "dark";

/** Switches between light and dark. The choice is remembered; until then the system setting applies. */
export default function ThemeToggle() {
  const [theme, setTheme] = useState<Theme | null>(null);

  useEffect(() => {
    const stored = document.documentElement.dataset.theme as Theme | undefined;
    setTheme(stored ?? (window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light"));
  }, []);

  const toggle = () => {
    const next: Theme = theme === "dark" ? "light" : "dark";
    document.documentElement.dataset.theme = next;
    try {
      localStorage.setItem("kumo-theme", next);
    } catch {
      // Private browsing can refuse storage; the theme still changes for this visit.
    }
    setTheme(next);
  };

  const dark = theme === "dark";
  return (
    <button type="button" className="theme-toggle" onClick={toggle} aria-label={dark ? "Switch to light mode" : "Switch to dark mode"} title={dark ? "Light mode" : "Dark mode"}>
      <svg viewBox="0 0 24 24" width="18" height="18" aria-hidden="true">
        {dark ? (
          <g>
            <circle cx="12" cy="12" r="4.2" />
            <path d="M12 2.5v2.2M12 19.3v2.2M2.5 12h2.2M19.3 12h2.2M5.3 5.3l1.6 1.6M17.1 17.1l1.6 1.6M5.3 18.7l1.6-1.6M17.1 6.9l1.6-1.6" />
          </g>
        ) : (
          <path d="M20 14.5A8 8 0 0 1 9.5 4a8 8 0 1 0 10.5 10.5Z" />
        )}
      </svg>
    </button>
  );
}
