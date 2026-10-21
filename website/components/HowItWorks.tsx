"use client";

import { useEffect, useRef, type ReactNode } from "react";
import styles from "./HowItWorks.module.css";

type Icon = "picture" | "pdf" | "locked" | "folder";
type Row = { name: string; icon: Icon; className?: string };
type Caption = { title: string; line: string; className: string };

const MERGE_WEDGE = "M257.2 110.7A145 145 0 0 1 309.9 272.8L221.5 244.1A52 52 0 0 0 202.6 185.9Z";
const SPLIT_WEDGE = "M309.9 272.8A145 145 0 0 1 172.0 373.0L172.0 280.0A52 52 0 0 0 221.5 244.1Z";
const CONVERT_WEDGE = "M62.8 59.8A146 146 0 0 1 269.2 59.8L202.8 126.2A52 52 0 0 0 129.2 126.2Z";

/**
 * How Kumo is used, as four short looping scenes built from screenshots of the app's menus: opening Kumo, converting
 * a picture, splitting PDFs into folders, and merging a PDF that has a password. Each scene plays while it is on screen.
 */
export default function HowItWorks() {
  return (
    <section className={styles.how} aria-labelledby="how-title">
      <h2 id="how-title">How it works</h2>

      <Scene
        className={styles.s1}
        title="It opens where you already are"
        text="Start dragging files in File Explorer or on the desktop and tap Shift. Kumo's menu opens right under the pointer."
        points={["Only the actions that work for those files are shown.", "Hold Shift instead of tapping it and Windows moves the files as usual.", "Release outside the menu, or press Esc, and nothing happens."]}
        rows={[
          { name: "strawberry.jpg", icon: "picture", className: styles.sel },
          { name: "notes.pdf", icon: "pdf" },
          { name: "slides.pdf", icon: "pdf" },
        ]}
        ghost={<PictureGhost />}
        captions={[
          { title: "Drag files, then tap Shift", line: "From File Explorer or the desktop.", className: styles.k1 },
          { title: "Kumo opens under the pointer", line: "With only what works for those files.", className: styles.k2 },
        ]}
      >
        <Menu className={`${styles.radial} ${styles.m1}`} src="/assets/radial-menu.webp" />
        <kbd className={styles.shift}>Shift</kbd>
      </Scene>

      <Scene
        className={styles.s2}
        title="Your originals stay as they are"
        text="Convert a picture to PNG, JPEG, WebP, BMP or PDF. Kumo never overwrites the file you started from."
        points={["The result is saved as a new file, next to the original.", "If the name is already taken, Kumo numbers the new one: strawberry (2).webp.", "Rotate, crop and compress work the same way."]}
        rows={[
          { name: "strawberry.jpg", icon: "picture", className: styles.sel },
          { name: "strawberry.webp", icon: "picture", className: styles.made },
        ]}
        ghost={<PictureGhost />}
        captions={[
          { title: "Convert a picture", line: "PNG, WebP, BMP or PDF.", className: styles.k1 },
          { title: "Saved next to the original", line: "strawberry.jpg stays as it is.", className: styles.k2 },
        ]}
      >
        <Menu className={`${styles.radial} ${styles.m1}`} src="/assets/radial-menu.webp" wedge={CONVERT_WEDGE} viewBox="0 0 340 380" />
        <Menu className={`${styles.shot} ${styles.convert} ${styles.m2}`} src="/assets/shot-convert.webp" />
      </Scene>

      <Scene
        className={styles.s3}
        title="Tidy folders, pages in order"
        text="Split one or more PDFs and every page becomes its own PDF, gathered in a folder named after the file."
        points={["Pages are numbered 001, 002, 003, so they sort in the right order.", "Each PDF gets its own folder, such as notes (pages).", "You can also merge PDFs, or extract just the pages you need."]}
        rows={[
          { name: "notes.pdf", icon: "pdf", className: styles.sel },
          { name: "slides.pdf", icon: "pdf", className: styles.sel },
          { name: "notes (pages)", icon: "folder", className: styles.made },
          { name: "slides (pages)", icon: "folder", className: `${styles.made} ${styles.later}` },
        ]}
        ghost={<PdfGhost />}
        captions={[
          { title: "Two PDFs at once", line: "Drag them and tap Shift.", className: styles.k1 },
          { title: "Split", line: "Every page becomes its own PDF.", className: styles.k2 },
          { title: "One folder for each PDF", line: "The originals stay as they are.", className: styles.k3 },
        ]}
      >
        <Menu className={`${styles.shot} ${styles.pdfs} ${styles.m1}`} src="/assets/shot-pdfs.webp" wedge={SPLIT_WEDGE} viewBox="0 0 344 456" />
        <div className={styles.window}>
          <div className={styles.titlebar}>notes (pages)</div>
          {["001", "002", "003"].map((n) => (
            <div key={n} className={styles.row}>
              <FileIcon kind="pdf" />
              <span>notes - page {n}.pdf</span>
            </div>
          ))}
        </div>
        <kbd className={styles.shift}>Shift</kbd>
      </Scene>

      <Scene
        className={styles.s4}
        title="Passwords are respected"
        text="When a PDF you are merging has a password, Kumo notices and asks for it before it starts."
        points={["The password is only used to open that file; Kumo doesn't get around the protection.", "The merged copy is saved without a password, and the original keeps its own.", "Everything happens on your PC. Nothing is uploaded."]}
        rows={[
          { name: "plan.pdf", icon: "locked", className: styles.sel },
          { name: "slides.pdf", icon: "pdf", className: styles.sel },
          { name: "plan (merged).pdf", icon: "pdf", className: styles.made },
        ]}
        ghost={<PdfGhost locked />}
        captions={[
          { title: "This PDF has a password", line: "Merge it with another PDF.", className: styles.k1 },
          { title: "Kumo asks for the password", line: "It opens the file with it; nothing is bypassed.", className: styles.k2 },
          { title: "Merged on your PC", line: "The copy has no password; plan.pdf keeps its own.", className: styles.k3 },
        ]}
      >
        <Menu className={`${styles.shot} ${styles.pdfs} ${styles.m1}`} src="/assets/shot-pdfs.webp" wedge={MERGE_WEDGE} viewBox="0 0 344 456" />
        <div className={styles.dialog}>
          <div className={styles.dialogBar}>
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img src="/mascot/idle.png" alt="" />
            Unlock PDF
          </div>
          <p>Enter the password for plan.pdf to include it in the merged PDF.</p>
          <div className={styles.field}>
            <i />
            <i />
            <i />
            <i />
          </div>
          <p className={styles.hint}>The merged PDF will be saved without a password.</p>
          <div className={styles.buttons}>
            <span className={styles.continue}>Continue</span>
            <span>Cancel</span>
          </div>
        </div>
        <kbd className={styles.shift}>Shift</kbd>
      </Scene>
    </section>
  );
}

function Scene({
  className,
  title,
  text,
  points,
  rows,
  ghost,
  captions,
  children,
}: {
  className: string;
  title: string;
  text: string;
  points: string[];
  rows: Row[];
  ghost: ReactNode;
  captions: Caption[];
  children: ReactNode;
}) {
  const ref = useRef<HTMLDivElement>(null);

  // Each scene fades in the first time it is reached and only animates while it is on screen.
  useEffect(() => {
    const el = ref.current!;
    const observer = new IntersectionObserver(
      ([entry]) => {
        el.classList.toggle(styles.playing, entry.isIntersecting);
        if (entry.isIntersecting) el.classList.add(styles.shown);
      },
      { threshold: 0.35 },
    );
    observer.observe(el);
    return () => observer.disconnect();
  }, []);

  return (
    <figure ref={ref} className={`${styles.scene} ${className}`}>
      <div className={styles.screen} aria-hidden="true">
        <svg className={styles.wall} viewBox="0 0 800 500" preserveAspectRatio="xMidYMax slice">
          <circle cx="690" cy="300" r="40" className={styles.wallSun} />
          <path d="M0 330c120-40 260-34 400-10s280-50 400-24v204H0Z" className={styles.wallBack} />
          <path d="M0 400c160-30 320-20 480 4s220-20 320-10v106H0Z" className={styles.wallFront} />
        </svg>
        <div className={styles.explorer}>
          <div className={styles.titlebar}>Documents</div>
          {rows.map((row) => (
            <div key={row.name} className={`${styles.row} ${row.className ?? ""}`}>
              <FileIcon kind={row.icon} />
              <span>{row.name}</span>
            </div>
          ))}
        </div>
        <div className={styles.pointer}>
          <span className={styles.ghost}>{ghost}</span>
        </div>
        {children}
        <div className={styles.pointer}>
          <svg className={styles.cursor} viewBox="0 0 24 24">
            <path d="M5 3l14 8-6.2 1.4L10 19z" />
          </svg>
        </div>
        <div className={styles.captions}>
          {captions.map((c) => (
            <div key={c.title} className={`${styles.caption} ${c.className}`}>
              <strong>{c.title}</strong>
              <span>{c.line}</span>
            </div>
          ))}
        </div>
      </div>
      <figcaption className={styles.about}>
        <h3>{title}</h3>
        <p>{text}</p>
        <ul>
          {points.map((point) => (
            <li key={point}>{point}</li>
          ))}
        </ul>
      </figcaption>
    </figure>
  );
}

function PictureGhost() {
  // eslint-disable-next-line @next/next/no-img-element
  return <img src="/demo/strawberry-thumb.webp" alt="" />;
}

function PdfGhost({ locked = false }: { locked?: boolean }) {
  return (
    <span className={styles.ghostPdf}>
      <FileIcon kind={locked ? "locked" : "pdf"} />
      <b>2</b>
    </span>
  );
}

function FileIcon({ kind }: { kind: Icon }) {
  if (kind === "picture") {
    // eslint-disable-next-line @next/next/no-img-element
    return <img className={styles.icon} src="/demo/strawberry-thumb.webp" alt="" />;
  }
  if (kind === "folder") return <i className={`${styles.icon} ${styles.folder}`} />;
  return (
    <i className={`${styles.icon} ${styles.pdf}`}>
      PDF
      {kind === "locked" && (
        <svg className={styles.lock} viewBox="0 0 24 24">
          <circle cx="12" cy="12" r="12" fill="#2c2826" />
          <path d="M8.5 11V8.5a3.5 3.5 0 0 1 7 0V11" fill="none" stroke="#f2c230" strokeWidth="2.2" strokeLinecap="round" />
          <rect x="7" y="11" width="10" height="7.5" rx="1.6" fill="#f2c230" />
        </svg>
      )}
    </i>
  );
}

function Menu({ className, src, wedge, viewBox }: { className: string; src: string; wedge?: string; viewBox?: string }) {
  return (
    <div className={`${styles.menu} ${className}`}>
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img src={src} alt="" />
      {wedge && (
        <svg className={styles.wedge} viewBox={viewBox}>
          <path d={wedge} />
        </svg>
      )}
    </div>
  );
}
