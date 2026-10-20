import type { Metadata, Viewport } from "next";
import { config } from "@fortawesome/fontawesome-svg-core";
import "@fortawesome/fontawesome-svg-core/styles.css";
import SiteFooter from "@/components/SiteFooter";
import SiteHeader from "@/components/SiteHeader";
import { site } from "@/lib/site";
import "./globals.css";

// Font Awesome's CSS is bundled above, so icons have their size on the first paint instead of after it is injected.
config.autoAddCss = false;

const description = "Kumo is a free, open-source Windows utility. Drag files, tap Shift, and convert, crop, compress or combine them from a radial menu.";

export const metadata: Metadata = {
  ...(site.url ? { metadataBase: new URL(site.url), alternates: { canonical: "/" } } : {}),
  title: { default: "Kumo — quick file actions for Windows", template: "%s — Kumo" },
  description,
  icons: {
    icon: [{ url: "/favicon.ico", sizes: "any" }, { url: "/assets/favicon-32.png", type: "image/png" }],
    apple: "/assets/apple-touch-icon.png",
  },
  openGraph: {
    type: "website",
    title: "Kumo — quick file actions for Windows",
    description: "Drag files, tap Shift, and convert, crop, compress or combine them from a radial menu. Free and open source.",
    ...(site.url ? { url: site.url, images: [{ url: "/assets/og.jpg", width: 1200, height: 630 }] } : {}),
  },
  twitter: { card: "summary_large_image" },
};

export const viewport: Viewport = {
  themeColor: [
    { media: "(prefers-color-scheme: light)", color: "#fbe9da" },
    { media: "(prefers-color-scheme: dark)", color: "#241b22" },
  ],
};

// Applies a remembered theme before the first paint, so a dark-mode visitor never sees a light flash.
const themeScript = `try{var t=localStorage.getItem("kumo-theme");if(t==="light"||t==="dark")document.documentElement.dataset.theme=t}catch(e){}`;

export default function RootLayout({ children }: { children: React.ReactNode }) {
  return (
    <html lang="en" suppressHydrationWarning>
      <head>
        <script dangerouslySetInnerHTML={{ __html: themeScript }} />
      </head>
      <body>
        <a className="skip" href="#main">
          Skip to content
        </a>
        <svg width="0" height="0" style={{ position: "absolute" }} aria-hidden="true" focusable="false">
          <filter id="ink">
            <feTurbulence type="fractalNoise" baseFrequency="0.9" numOctaves="2" seed="7" result="noise" />
            <feDisplacementMap in="SourceGraphic" in2="noise" scale="1.1" />
          </filter>
        </svg>
        <SiteHeader />
        <main id="main">{children}</main>
        <SiteFooter />
      </body>
    </html>
  );
}
