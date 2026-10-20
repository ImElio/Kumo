"use client";

import { useEffect, useState } from "react";
import Doodle from "./Doodle";

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
      <Doodle name={dark ? "sun" : "moon"} size={20} accent={false} />
    </button>
  );
}
