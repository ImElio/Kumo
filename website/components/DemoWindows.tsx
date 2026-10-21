"use client";

import { useEffect, useRef, useState, type PointerEvent as ReactPointerEvent, type ReactNode } from "react";
import type { DemoFile } from "@/lib/kumo";
import styles from "./DesktopDemo.module.css";

const FULL = { x: 0, y: 0, width: 1, height: 1 };

/** A picture as it is now: the source picture cropped, then rotated. Fills its positioned parent. */
export function ImageView({ file }: { file: DemoFile }) {
  const image = file.image!;
  const view = file.view ?? { rotation: 0, crop: FULL };
  const quarter = view.rotation % 180 !== 0;
  const c = view.crop;
  return (
    <span className={styles.view}>
      <span
        className={styles.viewFrame}
        style={{
          width: quarter ? `${(image.height / image.width) * 100}%` : "100%",
          height: quarter ? `${(image.width / image.height) * 100}%` : "100%",
          transform: `translate(-50%, -50%) rotate(${view.rotation}deg)`,
        }}
      >
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img
          src={image.full}
          alt=""
          draggable={false}
          style={{ width: `${100 / c.width}%`, height: `${100 / c.height}%`, left: `${(-c.x / c.width) * 100}%`, top: `${(-c.y / c.height) * 100}%` }}
        />
      </span>
    </span>
  );
}

/** A picture fitted inside a box of the given size, keeping its proportions. */
export function Picture({ file, width, height, className }: { file: DemoFile; width: number; height: number; className?: string }) {
  const image = file.image!;
  const scale = Math.min(width / image.width, height / image.height);
  return (
    <span className={`${styles.picture} ${className ?? ""}`} style={{ width: Math.max(1, image.width * scale), height: Math.max(1, image.height * scale) }}>
      <ImageView file={file} />
    </span>
  );
}

export function Thumb({ file, size = 48 }: { file: DemoFile; size?: number }) {
  if (file.kind === "folder") return <span className={styles.folder} style={{ width: size, height: size * 0.78 }} aria-hidden="true" />;
  if (file.kind === "pdf")
    return (
      <span className={styles.pdf} aria-hidden="true">
        PDF
        {file.password && <Lock className={styles.pdfBadge} />}
      </span>
    );
  if (file.kind === "txt") return <span className={styles.txt} aria-hidden="true" />;
  if (!file.image) return <span className={styles.blankPicture} aria-hidden="true" />;
  const scale = Math.max(size / file.image.width, size / file.image.height);
  return (
    <span className={styles.thumbPicture} style={{ width: size, height: size }} aria-hidden="true">
      <span className={styles.picture} style={{ width: file.image.width * scale, height: file.image.height * scale }}>
        <ImageView file={file} />
      </span>
    </span>
  );
}

function Lock({ className }: { className?: string }) {
  return (
    <svg className={className} viewBox="0 0 24 24" aria-hidden="true" focusable="false">
      <path d="M7.5 10.5V8a4.5 4.5 0 0 1 9 0v2.5" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" />
      <rect x="5" y="10.5" width="14" height="10" rx="2" fill="currentColor" />
    </svg>
  );
}

export type WindowState = { wid: string; fileId?: string; file: DemoFile; app: "file" | "paint"; x: number; y: number; w: number; h: number; z: number };

const TITLE_H = 34;

export function Window({
  win,
  title,
  active,
  onFocus,
  onMove,
  onClose,
  children,
}: {
  win: WindowState;
  title: string;
  active: boolean;
  onFocus: () => void;
  onMove: (x: number, y: number) => void;
  onClose: () => void;
  children: ReactNode;
}) {
  const grab = useRef<{ dx: number; dy: number } | null>(null);
  const begin = (e: ReactPointerEvent) => {
    if ((e.target as HTMLElement).closest("button")) return;
    e.currentTarget.setPointerCapture(e.pointerId);
    grab.current = { dx: e.clientX - win.x, dy: e.clientY - win.y };
  };
  const move = (e: ReactPointerEvent) => {
    if (grab.current) onMove(e.clientX - grab.current.dx, e.clientY - grab.current.dy);
  };
  return (
    <section
      className={`${styles.window} ${active ? styles.windowActive : ""}`}
      style={{ left: win.x, top: win.y, width: win.w, height: win.h, zIndex: win.z }}
      role="dialog"
      aria-label={title}
      onPointerDown={(e) => {
        e.stopPropagation();
        onFocus();
      }}
      onContextMenu={(e) => e.stopPropagation()}
      onKeyDown={(e) => e.key === "Escape" && onClose()}
    >
      <header className={styles.windowBar} onPointerDown={begin} onPointerMove={move} onPointerUp={() => (grab.current = null)}>
        <span className={styles.windowTitle}>{title}</span>
        <button type="button" className={styles.windowClose} onClick={onClose} aria-label={`Close ${title}`}>
          ×
        </button>
      </header>
      <div className={styles.windowBody} style={{ height: win.h - TITLE_H }}>
        {children}
      </div>
    </section>
  );
}

export function ImageViewer({ file, width, height }: { file: DemoFile; width: number; height: number }) {
  return (
    <div className={styles.imageViewer}>
      {file.image ? <Picture file={file} width={width - 24} height={height - TITLE_H - 24} /> : <span className={styles.viewerNote}>This image was made from a PDF page with no picture on it.</span>}
    </div>
  );
}

export function PdfViewer({ file, width }: { file: DemoFile; width: number }) {
  const [unlocked, setUnlocked] = useState(!file.password);
  const [attempt, setAttempt] = useState("");
  const [wrong, setWrong] = useState(false);
  const pages = file.pages ?? 1;
  const pageWidth = Math.min(width - 48, 340);

  if (!unlocked) {
    return (
      <form
        className={styles.pdfLock}
        onSubmit={(e) => {
          e.preventDefault();
          if (attempt === file.password) setUnlocked(true);
          else setWrong(true);
        }}
      >
        <Lock className={styles.lockIcon} />
        <p>This PDF is password-protected.</p>
        <input type="password" autoFocus value={attempt} placeholder="Password" onChange={(e) => setAttempt(e.target.value)} aria-label="Password" />
        <p className={styles.dialogError}>{wrong ? "That isn't the password." : ""}</p>
        <button type="submit">Open</button>
      </form>
    );
  }

  return (
    <div className={styles.pdfViewer}>
      {Array.from({ length: pages }, (_, i) => {
        const picture = file.pageImages?.[i];
        return (
          <figure key={i} className={styles.pdfPage} style={{ width: pageWidth, height: pageWidth * 1.29 }}>
            {picture?.image ? (
              <Picture file={picture} width={pageWidth - 32} height={pageWidth * 1.29 - 48} />
            ) : (
              <span className={styles.pdfText} aria-hidden="true">
                <b />
                {Array.from({ length: 9 }, (_, l) => (
                  <i key={l} style={{ width: `${62 + ((l * 37 + i * 13) % 34)}%` }} />
                ))}
              </span>
            )}
            <figcaption>
              Page {i + 1} of {pages}
            </figcaption>
          </figure>
        );
      })}
    </div>
  );
}

export function TextEditor({ file, onChange }: { file: DemoFile; onChange: (text: string) => void }) {
  return <textarea className={styles.textEditor} value={file.text ?? ""} onChange={(e) => onChange(e.target.value)} spellCheck={false} aria-label={file.name} />;
}

export function FolderView({ file, onOpen, onMenu }: { file: DemoFile; onOpen: (child: DemoFile) => void; onMenu: (child: DemoFile, x: number, y: number) => void }) {
  const children = file.children ?? [];
  if (children.length === 0) return <p className={styles.viewerNote}>This folder is empty. Drop files on its icon to move them here.</p>;
  return (
    <ul className={styles.folderView}>
      {children.map((child) => (
        <li key={child.id}>
          <button
            type="button"
            className={styles.folderItem}
            onDoubleClick={() => onOpen(child)}
            onKeyDown={(e) => e.key === "Enter" && onOpen(child)}
            onContextMenu={(e) => {
              e.preventDefault();
              e.stopPropagation();
              onMenu(child, e.clientX, e.clientY);
            }}
          >
            <Thumb file={child} size={40} />
            <span>{child.name}</span>
          </button>
        </li>
      ))}
    </ul>
  );
}

const COLORS = ["#2b2523", "#e8742b", "#d64545", "#3b82c4", "#3f9e5a", "#f2c230", "#ffffff"];
const SIZES = [3, 8, 18];
const CANVAS_W = 560;
const CANVAS_H = 360;

/** A small paint program. Saving turns the drawing into a PNG on the desktop. */
export function PaintApp({ onSave }: { onSave: (dataUrl: string, width: number, height: number) => void }) {
  const canvas = useRef<HTMLCanvasElement>(null);
  const [color, setColor] = useState(COLORS[1]);
  const [size, setSize] = useState(SIZES[1]);
  const [saved, setSaved] = useState(false);
  const last = useRef<{ x: number; y: number } | null>(null);

  const clear = () => {
    const ctx = canvas.current!.getContext("2d")!;
    ctx.fillStyle = "#ffffff";
    ctx.fillRect(0, 0, CANVAS_W, CANVAS_H);
  };

  useEffect(clear, []);

  const point = (e: ReactPointerEvent) => {
    const r = canvas.current!.getBoundingClientRect();
    return { x: ((e.clientX - r.left) / r.width) * CANVAS_W, y: ((e.clientY - r.top) / r.height) * CANVAS_H };
  };

  const draw = (e: ReactPointerEvent) => {
    if (!last.current) return;
    const p = point(e);
    const ctx = canvas.current!.getContext("2d")!;
    ctx.strokeStyle = color;
    ctx.lineWidth = size;
    ctx.lineCap = "round";
    ctx.lineJoin = "round";
    ctx.beginPath();
    ctx.moveTo(last.current.x, last.current.y);
    ctx.lineTo(p.x, p.y);
    ctx.stroke();
    last.current = p;
    setSaved(false);
  };

  return (
    <div className={styles.paint}>
      <div className={styles.paintTools} role="toolbar" aria-label="Brush">
        {COLORS.map((c) => (
          <button key={c} type="button" className={`${styles.swatch} ${c === color ? styles.swatchOn : ""}`} style={{ background: c }} onClick={() => setColor(c)} aria-label={c === "#ffffff" ? "Eraser" : `Colour ${c}`} />
        ))}
        <span className={styles.paintGap} />
        {SIZES.map((s) => (
          <button key={s} type="button" className={`${styles.brush} ${s === size ? styles.swatchOn : ""}`} onClick={() => setSize(s)} aria-label={`Brush size ${s}`}>
            <i style={{ width: Math.min(s, 14), height: Math.min(s, 14) }} />
          </button>
        ))}
        <span className={styles.paintGap} />
        <button type="button" className={styles.paintButton} onClick={clear}>
          Clear
        </button>
        <button
          type="button"
          className={`${styles.paintButton} ${styles.paintSave}`}
          onClick={() => {
            onSave(canvas.current!.toDataURL("image/png"), CANVAS_W, CANVAS_H);
            setSaved(true);
          }}
        >
          {saved ? "Saved" : "Save"}
        </button>
      </div>
      <canvas
        ref={canvas}
        width={CANVAS_W}
        height={CANVAS_H}
        className={styles.paintCanvas}
        onPointerDown={(e) => {
          e.currentTarget.setPointerCapture(e.pointerId);
          last.current = point(e);
          draw(e);
        }}
        onPointerMove={draw}
        onPointerUp={() => (last.current = null)}
        onPointerCancel={() => (last.current = null)}
        aria-label="Drawing canvas"
      />
    </div>
  );
}
