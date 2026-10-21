"use client";

import { useEffect, useState } from "react";
import DesktopDemo, { Wallpaper } from "./DesktopDemo";
import styles from "./DemoBoot.module.css";

const MESSAGES = ["Waking up the desktop…", "Putting out the sample files…", "Warming up Kumo…"];
const MESSAGE_AT = [0, 1150, 1650];
const READY_MS = 2200;

export default function DemoBoot() {
  const [ready, setReady] = useState(false);
  const [message, setMessage] = useState(0);

  useEffect(() => {
    if (window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
      setReady(true);
      return;
    }
    const timers = [
      ...MESSAGE_AT.slice(1).map((at, i) => window.setTimeout(() => setMessage(i + 1), at)),
      window.setTimeout(() => setReady(true), READY_MS),
    ];
    return () => timers.forEach(clearTimeout);
  }, []);

  return (
    <div className={styles.stage}>
      <div className={`${styles.startup} ${ready ? styles.startupOff : ""}`} aria-hidden={ready}>
        <div className={styles.bootGroup}>
          <div className={styles.bootMonitor}>
            <div className={styles.bootScreen}>
              <div className={styles.bootWall}>
                <Wallpaper />
              </div>
              <div className={styles.bootContent}>
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img className={styles.bootMascot} src="/mascot/idle.png" width={80} height={80} alt="" />
                <p className={styles.bootTitle}>Starting Kumo</p>
                <p key={message} className={styles.bootMessage} role="status">
                  {MESSAGES[message]}
                </p>
                <div className={styles.progress} aria-hidden="true">
                  <span />
                </div>
              </div>
              <div className={styles.bootTaskbar} />
            </div>
          </div>
          <div className={styles.bootStand} aria-hidden="true" />
        </div>
        <p className={styles.bootCaption}>
          <span>
            Tip: drag a file, then tap <kbd>Shift</kbd>.
          </span>
        </p>
      </div>

      <div className={`${styles.desktop} ${ready ? styles.desktopOn : ""}`} aria-hidden={!ready} inert={!ready}>
        <DesktopDemo immersive entered={ready} />
      </div>
    </div>
  );
}
