"use client";

import { useRef, useState, type PointerEvent as ReactPointerEvent } from "react";
import type { CropRegion, DemoFile } from "@/lib/kumo";
import styles from "./DesktopDemo.module.css";

type Ratio = "free" | "original" | number;
type Edges = { l?: boolean; r?: boolean; t?: boolean; b?: boolean };
type Gesture = { edges: Edges | "move"; startX: number; startY: number; start: CropRegion };

const FULL: CropRegion = { x: 0, y: 0, width: 1, height: 1 };
const MIN = 0.04;
const RATIOS: { label: string; value: Ratio }[] = [
  { label: "Free", value: "free" },
  { label: "Original", value: "original" },
  { label: "1:1", value: 1 },
  { label: "4:3", value: 4 / 3 },
  { label: "3:2", value: 3 / 2 },
  { label: "16:9", value: 16 / 9 },
];
const HANDLES: Edges[] = [{ t: true, l: true }, { t: true }, { t: true, r: true }, { r: true }, { b: true, r: true }, { b: true }, { b: true, l: true }, { l: true }];

const clamp = (v: number, lo: number, hi: number) => Math.min(Math.max(v, lo), hi);

/** The crop editor of Kumo (src/Kumo.App/Windows/CropWindow) for the first selected image. */
export default function CropEditor({ file, onCancel, onCrop }: { file: DemoFile; onCancel: () => void; onCrop: (crop: CropRegion) => void }) {
  const image = file.image!;
  const stage = useRef<HTMLDivElement>(null);
  const [rect, setRect] = useState<CropRegion>(FULL);
  const [ratio, setRatio] = useState<Ratio>("free");
  const gesture = useRef<Gesture | null>(null);

  /** Width ÷ height in pixels for a fixed ratio, expressed in normalized units of this image. */
  const normalized = (r: Ratio) => {
    const pixels = r === "original" ? image.width / image.height : r === "free" ? null : r;
    return pixels === null ? null : (pixels * image.height) / image.width;
  };

  const fitRatio = (r: Ratio) => {
    const n = normalized(r);
    if (n === null) return FULL;
    const width = n >= 1 ? 1 : n;
    const height = n >= 1 ? 1 / n : 1;
    return { x: (1 - width) / 2, y: (1 - height) / 2, width, height };
  };

  const point = (event: ReactPointerEvent) => {
    const box = stage.current!.getBoundingClientRect();
    return { x: clamp((event.clientX - box.left) / box.width, 0, 1), y: clamp((event.clientY - box.top) / box.height, 0, 1) };
  };

  const begin = (event: ReactPointerEvent, edges: Gesture["edges"]) => {
    event.stopPropagation();
    event.preventDefault();
    stage.current!.setPointerCapture(event.pointerId);
    const p = point(event);
    let start = rect;
    if (edges !== "move" && !edges.l && !edges.r && !edges.t && !edges.b) {
      start = { x: p.x, y: p.y, width: 0, height: 0 };
      edges = { r: true, b: true };
    }
    gesture.current = { edges, startX: p.x, startY: p.y, start };
  };

  const move = (event: ReactPointerEvent) => {
    const g = gesture.current;
    if (!g) return;
    const p = point(event);
    const dx = p.x - g.startX;
    const dy = p.y - g.startY;
    const s = g.start;
    if (g.edges === "move") {
      setRect({ ...s, x: clamp(s.x + dx, 0, 1 - s.width), y: clamp(s.y + dy, 0, 1 - s.height) });
      return;
    }
    let left = s.x;
    let top = s.y;
    let right = s.x + s.width;
    let bottom = s.y + s.height;
    if (g.edges.l) left = clamp(s.x + dx, 0, right - MIN);
    if (g.edges.r) right = clamp(right + dx, left + MIN, 1);
    if (g.edges.t) top = clamp(s.y + dy, 0, bottom - MIN);
    if (g.edges.b) bottom = clamp(bottom + dy, top + MIN, 1);
    const n = normalized(ratio);
    if (n !== null) {
      let width = right - left;
      let height = width / n;
      const maxHeight = g.edges.t ? bottom : 1 - top;
      if (height > maxHeight) {
        height = maxHeight;
        width = height * n;
      }
      if (g.edges.l) left = right - width;
      else right = left + width;
      if (g.edges.t) top = bottom - height;
      else bottom = top + height;
    }
    setRect({ x: left, y: top, width: right - left, height: bottom - top });
  };

  const end = () => {
    gesture.current = null;
    setRect((r) => (r.width < MIN || r.height < MIN ? fitRatio(ratio) : r));
  };

  const pixels = `${Math.max(1, Math.round(rect.width * image.width))} × ${Math.max(1, Math.round(rect.height * image.height))} px`;
  const whole = rect.width >= 0.999 && rect.height >= 0.999;

  return (
    <div className={`${styles.dialog} ${styles.cropDialog}`} role="dialog" aria-modal="true" aria-labelledby="crop-title" onKeyDown={(e) => e.key === "Escape" && onCancel()}>
      <h3 id="crop-title">Crop — {file.name}</h3>
      <div
        ref={stage}
        className={styles.cropStage}
        style={{ aspectRatio: `${image.width} / ${image.height}`, width: `min(100%, ${Math.round((260 * image.width) / image.height)}px)` }}
        onPointerDown={(e) => begin(e, {})}
        onPointerMove={move}
        onPointerUp={end}
        onPointerCancel={end}
      >
        {/* eslint-disable-next-line @next/next/no-img-element */}
        <img src={image.full} alt="" draggable={false} />
        <div
          className={styles.cropFrame}
          style={{ left: `${rect.x * 100}%`, top: `${rect.y * 100}%`, width: `${rect.width * 100}%`, height: `${rect.height * 100}%` }}
          onPointerDown={(e) => begin(e, "move")}
        >
          {HANDLES.map((edges, i) => (
            <span
              key={i}
              className={styles.cropHandle}
              data-edges={`${edges.t ? "t" : ""}${edges.b ? "b" : ""}${edges.l ? "l" : ""}${edges.r ? "r" : ""}`}
              onPointerDown={(e) => begin(e, edges)}
            />
          ))}
        </div>
      </div>
      <div className={styles.cropInfo}>
        <span>Drag the corners or edges, drag inside to move, or draw a new frame outside it.</span>
        <strong>{pixels}</strong>
      </div>
      <div className={styles.cropFooter}>
        <div className={styles.ratios} role="radiogroup" aria-label="Aspect ratio">
          {RATIOS.map((r) => (
            <label key={r.label}>
              <input
                type="radio"
                name="crop-ratio"
                checked={ratio === r.value}
                onChange={() => {
                  setRatio(r.value);
                  setRect(fitRatio(r.value));
                }}
              />
              {r.label}
            </label>
          ))}
        </div>
        <div className={styles.dialogActions}>
          <button
            type="button"
            onClick={() => {
              setRatio("free");
              setRect(FULL);
            }}
          >
            Reset
          </button>
          <button type="button" className={styles.primary} disabled={whole} onClick={() => onCrop(rect)}>
            Crop
          </button>
          <button type="button" onClick={onCancel}>
            Cancel
          </button>
        </div>
      </div>
    </div>
  );
}
