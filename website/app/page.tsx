import Landscape from "@/components/Landscape";
import Doodle from "@/components/Doodle";
import { links } from "@/lib/site";

export default function Home() {
  return (
    <>
      <div className="sky">
        <section className="hero wrap" aria-labelledby="hero-title">
          <div className="hero-text">
            <h1 id="hero-title">
              Your files.
              <br />A little <span className="scribble">easier</span>.
            </h1>
            <p className="lede">
              Kumo puts everyday file actions in a radial menu that opens where you are already working. Drag files in File
              Explorer, tap <kbd>Shift</kbd>, and release on what you want done.
            </p>
            <div className="actions" id="download">
              <a className="button button-primary" href="/download/">
                <Doodle name="download" size={22} accent={false} />
                Download for Windows
              </a>
              <a className="button button-secondary" href={links.repository}>
                <Doodle name="code" size={22} accent={false} />
                View on GitHub
              </a>
            </div>
          </div>
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img className="hero-mascot" src="/assets/kumo.webp" width={240} height={211} alt="Kumo, a small cloud wearing an orange scarf" />
        </section>
        <Landscape />
      </div>

      <section className="demo" aria-labelledby="how-title">
        <div className="wrap demo-details">
          <h2 id="how-title">How it works</h2>
          <ol className="steps">
            <li>
              <span className="step-title">Start dragging.</span> Pick up one or more files in File Explorer or on the
              desktop.
            </li>
            <li>
              <span className="step-title">Tap Shift.</span> Kumo&apos;s menu appears under the pointer, showing only what
              works for those files.
            </li>
            <li>
              <span className="step-title">Release on an action.</span> Results are saved as new files next to the
              originals.
            </li>
            <li className="aside-step">
              Holding Shift instead of tapping it keeps Windows&apos; usual move behaviour. You can also press{" "}
              <kbd>Ctrl</kbd>&nbsp;<kbd>Alt</kbd>&nbsp;<kbd>K</kbd> for the files selected in Explorer, or open Kumo from the
              notification area.
            </li>
          </ol>
          <div className="try-demo">
            {/* eslint-disable-next-line @next/next/no-img-element */}
            <img src="/mascot/idle.png" width={64} height={64} alt="" />
            <p>
              <strong>Want to try it first?</strong> There&apos;s a pretend Windows desktop with a few sample pictures and
              PDFs. Drag one and tap <kbd>Shift</kbd>.
            </p>
            <a className="button button-secondary" href="/demo/">
              Open the demo
            </a>
          </div>
        </div>
      </section>

      <section className="features wrap" id="features" aria-labelledby="features-title">
        <h2 id="features-title">What Kumo can do</h2>

        <div className="feature-row">
          <div className="feature-text">
            <h3>Images</h3>
            <dl>
              <dt>Convert</dt>
              <dd>PNG, JPEG, WebP, BMP and GIF to PNG, JPEG, WebP, BMP or PDF.</dd>
              <dt>Crop</dt>
              <dd>In a preview editor with handles, guides and ratios such as 1:1, 4:3 and 16:9.</dd>
              <dt>Rotate</dt>
              <dd>90° either way, or 180°, without opening an editor.</dd>
              <dt>Compress</dt>
              <dd>Smaller JPEG and WebP files; PNG compression is lossless.</dd>
            </dl>
          </div>
          <figure className="shot">
            <div className="shot-frame">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src="/assets/shot-convert.webp"
                width={344}
                height={456}
                loading="lazy"
                alt="Kumo's Convert submenu for one JPEG image, offering PNG, WEBP, BMP and PDF, with WEBP highlighted"
              />
            </div>
          </figure>
        </div>

        <div className="feature-row feature-row-flip">
          <div className="feature-text">
            <h3>PDFs</h3>
            <dl>
              <dt>Merge</dt>
              <dd>Combine several PDFs, or several images, into one document.</dd>
              <dt>Split</dt>
              <dd>One file per page, or only the pages you choose.</dd>
              <dt>Protect</dt>
              <dd>Save a password-protected copy.</dd>
              <dt>To images</dt>
              <dd>Turn pages into PNG or JPEG.</dd>
            </dl>
          </div>
          <figure className="shot">
            <div className="shot-frame">
              {/* eslint-disable-next-line @next/next/no-img-element */}
              <img
                src="/assets/shot-pdfs.webp"
                width={344}
                height={456}
                loading="lazy"
                alt="Kumo's menu for two PDFs, with Convert, Merge PDFs, Split, Extract and Protect"
              />
            </div>
          </figure>
        </div>

        <figure className="feature-shot">
          {/* eslint-disable-next-line @next/next/no-img-element */}
          <img
            src="/assets/crop-editor.webp"
            width={798}
            height={543}
            loading="lazy"
            decoding="async"
            alt="Kumo's crop editor showing a photo of a strawberry with crop handles, composition guides and aspect ratio presets"
          />
          <figcaption>The crop editor. Drag the frame, or pick a ratio; the original is left untouched.</figcaption>
        </figure>

        <div className="principles">
          <p>
            <strong>
              <Doodle name="home" />
              Your files stay on your PC.
            </strong>
            Everything is processed locally and nothing is uploaded. Kumo never overwrites an original; it writes new files
            beside it, or to a folder you choose.
          </p>
          <p>
            <strong>
              <Doodle name="hourglass" />
              Only relevant actions.
            </strong>
            The radial menu adapts to your selection and shows the actions available for those files.
          </p>
        </div>
      </section>

      <section className="open-source" aria-labelledby="oss-title">
        <div className="wrap oss-inner">
          <h2 id="oss-title">Open source</h2>
          <p>
            Kumo is free and released under the MIT License. The code, the issue tracker and every release live on GitHub.
            Bug reports and pull requests are welcome.
          </p>
          <p>
            <a className="text-link" href={links.repository}>
              <Doodle name="heart" size={22} />
              {links.repositoryLabel}
            </a>
          </p>
        </div>
      </section>
    </>
  );
}
