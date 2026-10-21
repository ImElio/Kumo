import Link from "next/link";
import styles from "./DemoInvite.module.css";

/** The invitation to the interactive demo: a small copy of the demo desktop, playing on its own, and a way in. */
export default function DemoInvite() {
  return (
    <section className={styles.invite} aria-labelledby="try-title">
      <div className={`wrap ${styles.inner}`}>
        <Link className={styles.preview} href="/demo/" tabIndex={-1} aria-hidden="true">
          <span className={styles.monitor}>
            <span className={styles.screen}>
              <svg className={styles.wall} viewBox="0 0 800 500" preserveAspectRatio="xMidYMax slice">
                <circle cx="560" cy="300" r="42" className={styles.sun} />
                <path d="M0 330c120-40 260-34 400-10s280-50 400-24v204H0Z" className={styles.hillBack} />
                <path d="M0 400c160-30 320-20 480 4s220-20 320-10v106H0Z" className={styles.hillFront} />
              </svg>

              <span className={styles.icons}>
                <span className={`${styles.icon} ${styles.berry}`}>
                  {/* eslint-disable-next-line @next/next/no-img-element */}
                  <img src="/demo/strawberry-thumb.webp" alt="" />
                  <i>strawberry.jpg</i>
                </span>
                <span className={styles.icon}>
                  <b className={styles.pdf}>PDF</b>
                  <i>notes.pdf</i>
                </span>
                <span className={styles.icon}>
                  {/* eslint-disable-next-line @next/next/no-img-element */}
                  <img src="/demo/clouds-thumb.webp" alt="" />
                  <i>clouds.webp</i>
                </span>
                <span className={styles.icon}>
                  {/* eslint-disable-next-line @next/next/no-img-element */}
                  <img src="/demo/logo.webp" alt="" />
                  <i>logo.png</i>
                </span>
              </span>

              <span className={styles.note}>
                <b>Things to try</b>
                <span className={styles.tick}>Double-click a file</span>
                <span>Drag a file, tap Shift</span>
                <span>Make a drawing</span>
              </span>

              <span className={styles.viewer}>
                <span className={styles.viewerBar}>strawberry.jpg</span>
                {/* eslint-disable-next-line @next/next/no-img-element */}
                <img src="/demo/strawberry.webp" alt="" />
              </span>

              <svg className={styles.cursor} viewBox="0 0 24 24">
                <path d="M5 3l14 8-6.2 1.4L10 19z" />
              </svg>

              <span className={styles.taskbar}>
                <i />
                <i />
                <i />
              </span>
            </span>
          </span>
          <span className={styles.stand} />
        </Link>

        <div className={styles.copy}>
          <h2 id="try-title">Try it before you download it</h2>
          <p>
            A pretend Windows desktop, right in your browser. Open the sample pictures and PDFs, drag them around, make
            a drawing, and use Kumo on all of it.
          </p>
          <ul>
            <li>Sample files only; nothing leaves your browser.</li>
            <li>Works with a mouse and keyboard, like the real app.</li>
          </ul>
          <Link className={`button button-primary ${styles.go}`} href="/demo/">
            Open the demo <span aria-hidden="true">→</span>
          </Link>
        </div>
      </div>
    </section>
  );
}
