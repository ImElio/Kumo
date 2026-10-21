const stars = [
  [8, 18], [17, 42], [26, 12], [37, 30], [48, 8], [58, 36], [66, 16], [74, 44], [83, 10], [92, 28], [12, 62], [88, 58],
];

/** A small landscape under the hero: rolling hills, a sun that becomes the moon in dark mode, and drifting clouds. */
export default function Landscape() {
  return (
    <div className="landscape" aria-hidden="true">
      {stars.map(([x, y]) => (
        <span key={`${x}-${y}`} className="star" style={{ left: `${x}%`, top: `${y}%`, animationDelay: `${-((x * 7 + y * 3) % 40) / 10}s` }} />
      ))}
      <span className="sun" />
      <svg className="drift drift-one" viewBox="0 0 120 52">
        <path d="M18 50h86a16 16 0 0 0 0-32 24 24 0 0 0-44-10 18 18 0 0 0-30 12A15 15 0 0 0 18 50Z" />
      </svg>
      <svg className="drift drift-two" viewBox="0 0 120 52">
        <path d="M18 50h86a16 16 0 0 0 0-32 24 24 0 0 0-44-10 18 18 0 0 0-30 12A15 15 0 0 0 18 50Z" />
      </svg>
      <svg className="drift drift-three" viewBox="0 0 120 52">
        <path d="M18 50h86a16 16 0 0 0 0-32 24 24 0 0 0-44-10 18 18 0 0 0-30 12A15 15 0 0 0 18 50Z" />
      </svg>
      <svg className="hills" viewBox="0 0 1440 260" preserveAspectRatio="none">
        <path className="hill-back" d="M0 128C170 74 330 92 500 124S840 60 1030 96s300 52 410 6V260H0Z" />
        <path className="hill-mid" d="M0 178c190-44 400-28 590 4s390-58 560-30 250 42 290 30V260H0Z" />
        <path className="hill-front" d="M0 222c230-30 480-18 720 4s500-26 720-6V260H0Z" />
      </svg>
    </div>
  );
}
